using System.Web;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class FilterController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilterController> _logger;
    private readonly UpstreamFeedClient _upstreamFeedClient;
    private readonly Processor _processor;
    private readonly Cache _cache;
    private readonly FailureStore _failureStore;

    public FilterController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<FilterController> logger,
        UpstreamFeedClient upstreamFeedClient,
        Processor processor,
        Cache cache,
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
        var correctSecret = _configuration["Secret"];
        if (correctSecret != "" && secret != correctSecret)
        {
            var attemptedSecret = secret == null ? "no secret" : $"attempted secret '{secret}'";
            _logger.LogWarning(
                $"Unauthorized access attempted by {HttpContext.Connection.RemoteIpAddress} with {attemptedSecret}"
            );
            return Unauthorized();
        }

        var ruleset = "default";
        var returnCachedFeedOnUpstreamFailure =
            _configuration.GetValue<bool?>("ReturnCachedFeedOnUpstreamFailure") ?? !_env.IsDevelopment();

        // Fetch the RSS feed XML and get the XML
        var feedUrl = HttpUtility.UrlDecode(url);
        string originalRss;
        try
        {
            originalRss = await _upstreamFeedClient.GetStringAsync(feedUrl, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected while we were fetching the upstream feed
            return new EmptyResult();
        }
        catch (UpstreamHttpStatusException ex)
        {
            _failureStore.RecordFailure(ex.FailureInfo, ex);
            _logger.LogWarning(
                "Upstream feed returned HTTP {StatusCode} for {FeedUrl}",
                (int?)ex.FailureInfo.HttpStatusCode,
                feedUrl
            );
            if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
            {
                _logger.LogWarning("Returned cached RSS");
                return cachedResponse;
            }
            return StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _failureStore.RecordFailure(UpstreamFailureInfo.FromException(feedUrl, ex), ex);
            _logger.LogWarning(ex, "Timeout while fetching upstream feed {FeedUrl}", feedUrl);
            if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
            {
                _logger.LogWarning(ex, "Returned cached RSS");
                return cachedResponse;
            }
            return StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _failureStore.RecordFailure(UpstreamFailureInfo.FromException(feedUrl, ex), ex);
            _logger.LogWarning(ex, "Failed to fetch upstream feed {FeedUrl}", feedUrl);
            if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
            {
                _logger.LogWarning(ex, "Returned cached RSS");
                return cachedResponse;
            }
            return StatusCode(StatusCodes.Status502BadGateway);
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
            _failureStore.ClearFailure(feedUrl);
            WriteDebugFilesInDevMode(originalRss, cachedRss);
            _logger.LogInformation($"Returned cached RSS for feed {feedUrl} because nothing changed");
            return Content(cachedRss, "application/rss+xml");
        }
        else
        {
            try
            {
                // Modify the original RSS document by processing it with the loaded rules
                var originalDocument = XDocument.Parse(originalRss.TrimStart());
                var modifiedDocument = _processor.Process(originalDocument, rules, feedUrl);
                var modifiedRss = modifiedDocument.ToString();
                _cache.Set(feedUrl, hash, modifiedRss);
                _failureStore.ClearFailure(feedUrl);
                WriteDebugFilesInDevMode(originalRss, modifiedRss);
                return Content(modifiedRss, "application/rss+xml");
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
                return StatusCode(StatusCodes.Status502BadGateway);
            }
        }
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
}
