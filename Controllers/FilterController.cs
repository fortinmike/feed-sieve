using System.Text;
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

    public FilterController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<FilterController> logger,
        UpstreamFeedClient upstreamFeedClient,
        Processor processor,
        Cache cache
    )
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _upstreamFeedClient = upstreamFeedClient;
        _processor = processor;
        _cache = cache;
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
            WriteErrorInfoOutput(ex.FailureInfo);
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
            WriteExceptionLogOutput(feedUrl, ex);
            WriteErrorInfoOutput(UpstreamFailureInfo.FromException(feedUrl, ex));
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
            WriteExceptionLogOutput(feedUrl, ex);
            WriteErrorInfoOutput(UpstreamFailureInfo.FromException(feedUrl, ex));
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
            WriteLogsInDevMode(originalRss, cachedRss);
            _logger.LogInformation($"Returned cached RSS for feed {feedUrl} because nothing changed");
            return Content(cachedRss, "application/rss+xml");
        }
        else
        {
            try
            {
                // Modify the original RSS document by processing it with the loaded rules
                var originalDocument = XDocument.Parse(originalRss);
                var modifiedDocument = _processor.Process(originalDocument, rules, feedUrl);
                var modifiedRss = modifiedDocument.ToString();
                _cache.Set(feedUrl, hash, modifiedRss);
                WriteLogsInDevMode(originalRss, modifiedRss);
                return Content(modifiedRss, "application/rss+xml");
            }
            catch (XmlException ex)
            {
                WriteParseErrorOutput(feedUrl, originalRss, ex);
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

    private void WriteLogsInDevMode(string original, string modified)
    {
        if (_env.IsDevelopment())
        {
            var dir = "./logs";
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText($"{dir}/original.xml", original);
            System.IO.File.WriteAllText($"{dir}/modified.xml", modified);
        }
    }

    private void WriteParseErrorOutput(string feedUrl, string feedXml, XmlException exception)
    {
        var dir = Path.Combine("./logs", "errors", feedUrl.ToSafeFileName());
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "feed.xml"), feedXml);
        WriteExceptionLogOutput(feedUrl, exception);
    }

    private void WriteExceptionLogOutput(string feedUrl, Exception exception)
    {
        var dir = Path.Combine("./logs", "errors", feedUrl.ToSafeFileName());
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "exception.log"), exception.ToString());
    }

    private void WriteErrorInfoOutput(UpstreamFailureInfo failureInfo)
    {
        var dir = Path.Combine("./logs", "errors", failureInfo.FeedUrl.ToSafeFileName());
        Directory.CreateDirectory(dir);
        var info = new StringBuilder();
        info.AppendLine($"Feed URL: {failureInfo.FeedUrl}");
        info.AppendLine($"Failure: {failureInfo.FailureType}");
        info.AppendLine($"Message: {failureInfo.Message}");

        if (failureInfo.FinalUrl != null)
            info.AppendLine($"Final URL: {failureInfo.FinalUrl}");

        if (failureInfo.HttpStatusCode is { } statusCode)
            info.AppendLine($"HTTP Status: {(int)statusCode} {failureInfo.HttpReasonPhrase ?? statusCode.ToString()}");

        if (failureInfo.Redirects is { Count: > 0 })
        {
            info.AppendLine();
            info.AppendLine("Redirects:");
            foreach (var redirect in failureInfo.Redirects)
                info.AppendLine(redirect);
        }

        if (failureInfo.ResponseHeaders is { Count: > 0 })
        {
            info.AppendLine();
            info.AppendLine("Response headers:");
            foreach (var header in failureInfo.ResponseHeaders)
                info.AppendLine(header);
        }

        if (!string.IsNullOrEmpty(failureInfo.ResponseBodyPreview))
        {
            info.AppendLine();
            info.AppendLine("Response body preview:");
            info.AppendLine(failureInfo.ResponseBodyPreview);
        }

        System.IO.File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString());
    }

    private ContentResult? CreateCachedResponse(string feedUrl)
    {
        if (_cache.GetLast(feedUrl) is string cachedRss)
            return Content(cachedRss, "application/rss+xml");

        return null;
    }
}
