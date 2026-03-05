using System.Net;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Stopwatch = System.Diagnostics.Stopwatch;

[ApiController]
[Route("/")]
public class FilterController : ControllerBase
{
    private static readonly TimeSpan DefaultRateLimitCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxRateLimitCooldown = TimeSpan.FromHours(24);
    private static int _inFlightRequests;

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilterController> _logger;
    private readonly UpstreamFeedClient _upstreamFeedClient;
    private readonly Processor _processor;
    private readonly ICache _cache;
    private readonly FailureStore _failureStore;

    public FilterController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<FilterController> logger,
        UpstreamFeedClient upstreamFeedClient,
        Processor processor,
        ICache cache,
        FailureStore failureStore
    )
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _upstreamFeedClient = upstreamFeedClient;
        _processor = processor;
        _cache = cache;
        _failureStore = failureStore;
    }

    [HttpGet("filter")]
    public async Task<IActionResult> Filter([FromQuery] string url, [FromQuery] string? secret)
    {
        var feedUrl = HttpUtility.UrlDecode(url);
        var stopwatch = Stopwatch.StartNew();
        Interlocked.Increment(ref _inFlightRequests);
        var outcome = "unhandled";
        int? statusCode = null;

        IActionResult Complete(IActionResult result, string completedOutcome, int completedStatusCode)
        {
            outcome = completedOutcome;
            statusCode = completedStatusCode;
            return result;
        }

        try
        {
            var correctSecret = _configuration.GetRequiredSection("Secret").Value!;
            if (secret != correctSecret)
            {
                var attemptedSecret = secret == null ? "no secret" : $"attempted secret '{secret}'";
                _logger.LogWarning(
                    $"Unauthorized access attempted by {HttpContext.Connection.RemoteIpAddress} with {attemptedSecret}"
                );
                return Complete(Unauthorized(), "unauthorized", StatusCodes.Status401Unauthorized);
            }

            var ruleset = "default";
            var returnCachedFeedOnUpstreamFailure = _configuration
                .GetRequiredSection("ReturnCachedFeedOnUpstreamFailure")
                .Get<bool>();

            if (
                _failureStore.GetDoNotUpdateBeforeUtc(feedUrl) is DateTimeOffset doNotUpdateBeforeUtc
                && doNotUpdateBeforeUtc > DateTimeOffset.UtcNow
            )
            {
                _logger.LogWarning(
                    "Skipping upstream fetch for {FeedUrl} until {DoNotUpdateBeforeUtc} because upstream previously returned 429",
                    feedUrl,
                    doNotUpdateBeforeUtc
                );

                if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
                    return Complete(cachedResponse, "rate-limited-skipped-cached", StatusCodes.Status200OK);

                return Complete(
                    StatusCode(StatusCodes.Status503ServiceUnavailable),
                    "rate-limited-skipped",
                    StatusCodes.Status503ServiceUnavailable
                );
            }

            // Fetch the RSS feed XML and get the XML
            string originalRss;
            try
            {
                originalRss = await _upstreamFeedClient.GetStringAsync(feedUrl, HttpContext.RequestAborted);
                _failureStore.ClearDoNotUpdateBeforeUtc(feedUrl);
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected while we were fetching the upstream feed
                return Complete(new EmptyResult(), "client-canceled", StatusCodes.Status200OK);
            }
            catch (UpstreamHttpStatusException ex)
            {
                // Handle HTTP 429 (Too Many Requests)
                // We don't forward HTTP 429 to the client because in some cases
                // it can throttle per host and we don't want it to backoff from
                // fetching all feeds because they have the same feed-sieve host.
                if (ex.FailureInfo.HttpStatusCode == HttpStatusCode.TooManyRequests)
                {
                    var doNotUpdateBefore = GetDoNotUpdateBeforeUtc(ex.FailureInfo);
                    var failureInfo = ex.FailureInfo with { DoNotUpdateBeforeUtc = doNotUpdateBefore };
                    _failureStore.RecordFailure(failureInfo, ex);
                    _logger.LogWarning(
                        "Upstream feed rate-limited {FeedUrl}; skipping updates until {DoNotUpdateBeforeUtc}",
                        feedUrl,
                        doNotUpdateBefore
                    );

                    if (
                        returnCachedFeedOnUpstreamFailure
                        && CreateCachedResponse(feedUrl) is ContentResult cachedRateLimitedResponse
                    )
                    {
                        return Complete(
                            cachedRateLimitedResponse,
                            "rate-limited-cache-fallback",
                            StatusCodes.Status200OK
                        );
                    }

                    return Complete(
                        StatusCode(StatusCodes.Status503ServiceUnavailable),
                        "rate-limited",
                        StatusCodes.Status503ServiceUnavailable
                    );
                }

                // Handle other upstream errors

                _failureStore.RecordFailure(ex.FailureInfo, ex);
                _logger.LogWarning(
                    "Upstream feed returned HTTP {StatusCode} for {FeedUrl}",
                    (int?)ex.FailureInfo.HttpStatusCode,
                    feedUrl
                );

                if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
                {
                    _logger.LogWarning("Returned cached RSS");
                    return Complete(cachedResponse, "upstream-http-cached", StatusCodes.Status200OK);
                }

                return Complete(
                    StatusCode(StatusCodes.Status502BadGateway),
                    "upstream-http",
                    StatusCodes.Status502BadGateway
                );
            }
            catch (OperationCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                _failureStore.RecordFailure(UpstreamFailureInfo.FromException(feedUrl, ex), ex);
                _logger.LogWarning(ex, "Timeout while fetching upstream feed {FeedUrl}", feedUrl);
                if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
                {
                    _logger.LogWarning(ex, "Returned cached RSS");
                    return Complete(cachedResponse, "timeout-cached", StatusCodes.Status200OK);
                }
                return Complete(
                    StatusCode(StatusCodes.Status504GatewayTimeout),
                    "timeout",
                    StatusCodes.Status504GatewayTimeout
                );
            }
            catch (HttpRequestException ex)
            {
                _failureStore.RecordFailure(UpstreamFailureInfo.FromException(feedUrl, ex), ex);
                _logger.LogWarning(ex, "Failed to fetch upstream feed {FeedUrl}", feedUrl);
                if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
                {
                    _logger.LogWarning(ex, "Returned cached RSS");
                    return Complete(cachedResponse, "fetch-error-cached", StatusCodes.Status200OK);
                }
                return Complete(
                    StatusCode(StatusCodes.Status502BadGateway),
                    "fetch-error",
                    StatusCodes.Status502BadGateway
                );
            }

            // Load the rules
            var rulesString = System.IO.File.ReadAllText($"rules.{ruleset}.yaml");
            var rules = Rules.Parse(rulesString);

            var rssHash = originalRss.Hash();
            var rulesHash = rulesString.Hash();
            var hash = rssHash + rulesHash;

            // Results are cached as long as neither the original RSS nor the rules string have changed
            var cachedRss = _cache.Get(feedUrl, hash);
            if (cachedRss != null)
            {
                // Return the cached RSS document
                _failureStore.RecordSuccess(feedUrl);
                WriteDebugFilesInDevMode(originalRss, cachedRss);
                _logger.LogInformation($"Returned cached RSS for feed {feedUrl} because nothing changed");
                return Complete(Content(cachedRss, "application/rss+xml"), "cache-hit", StatusCodes.Status200OK);
            }

            try
            {
                // Modify the original RSS document by processing it with the loaded rules
                var originalDocument = XDocument.Parse(originalRss.TrimStart());
                var modifiedDocument = _processor.Process(originalDocument, rules, feedUrl);
                var modifiedRss = modifiedDocument.ToString();
                _cache.Set(feedUrl, hash, modifiedRss);
                _failureStore.RecordSuccess(feedUrl);
                WriteDebugFilesInDevMode(originalRss, modifiedRss);
                return Complete(Content(modifiedRss, "application/rss+xml"), "processed", StatusCodes.Status200OK);
            }
            catch (XmlException ex)
            {
                _failureStore.RecordParseFailure(feedUrl, originalRss, ex);
                _logger.LogWarning(
                    ex,
                    "Invalid XML from upstream feed {FeedUrl} at line {LineNumber}, position {LinePosition}",
                    feedUrl,
                    ex.LineNumber,
                    ex.LinePosition
                );
                return Complete(
                    StatusCode(StatusCodes.Status502BadGateway),
                    "xml-parse-error",
                    StatusCodes.Status502BadGateway
                );
            }
        }
        finally
        {
            var inFlightAfter = Interlocked.Decrement(ref _inFlightRequests);
            LogRequestDuration(feedUrl, outcome, statusCode, stopwatch.ElapsedMilliseconds, inFlightAfter);
        }
    }

    private void LogRequestDuration(string feedUrl, string outcome, int? statusCode, long elapsedMs, int inFlightAfter)
    {
        _logger.LogInformation(
            "Completed in {ElapsedMs}ms ({Outcome}, {StatusCode}, inFlightAfter={InFlightAfter}) for {FeedUrl}",
            elapsedMs,
            outcome,
            statusCode,
            inFlightAfter,
            feedUrl
        );
    }

    private void WriteDebugFilesInDevMode(string original, string modified)
    {
        if (_env.IsDevelopment())
        {
            var dir = "./storage/debug";
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText($"{dir}/original.xml", original);
            System.IO.File.WriteAllText($"{dir}/modified.xml", modified);
        }
    }

    private ContentResult? CreateCachedResponse(string feedUrl)
    {
        if (_cache.GetLast(feedUrl) is string cachedRss)
            return Content(cachedRss, "application/rss+xml");

        return null;
    }

    private DateTimeOffset GetDoNotUpdateBeforeUtc(UpstreamFailureInfo failureInfo)
    {
        var now = DateTimeOffset.UtcNow;
        var retryAfter = failureInfo.GetResponseHeaderValue("Retry-After");

        if (retryAfter != null)
        {
            if (int.TryParse(retryAfter, out var seconds) && seconds >= 0)
                return ClampDoNotUpdateBefore(now.AddSeconds(seconds), now);

            if (DateTimeOffset.TryParse(retryAfter, out var retryAfterDate))
                return ClampDoNotUpdateBefore(retryAfterDate, now);
        }

        return now.Add(DefaultRateLimitCooldown);
    }

    private static DateTimeOffset ClampDoNotUpdateBefore(DateTimeOffset value, DateTimeOffset now)
    {
        if (value <= now)
            return now.Add(DefaultRateLimitCooldown);

        var max = now.Add(MaxRateLimitCooldown);
        return value > max ? max : value;
    }
}
