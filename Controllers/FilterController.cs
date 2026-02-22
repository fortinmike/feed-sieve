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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Processor _processor;
    private readonly Cache _cache;

    public FilterController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<FilterController> logger,
        IHttpClientFactory httpClientFactory,
        Processor processor,
        Cache cache
    )
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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
            var httpClient = _httpClientFactory.CreateClient("upstream-feed");
            using var response = await httpClient.GetAsync(feedUrl, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
                WriteErrorInfoOutput(feedUrl, response, responseBody);
                _logger.LogWarning(
                    "Upstream feed returned HTTP {StatusCode} for {FeedUrl}",
                    (int)response.StatusCode,
                    feedUrl
                );
                if (returnCachedFeedOnUpstreamFailure && CreateCachedResponse(feedUrl) is ContentResult cachedResponse)
                {
                    _logger.LogWarning("Returned cached RSS");
                    return cachedResponse;
                }
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            originalRss = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected while we were fetching the upstream feed
            return new EmptyResult();
        }
        catch (OperationCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            WriteExceptionLogOutput(feedUrl, ex);
            WriteErrorInfoOutput(feedUrl, ex);
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
            WriteErrorInfoOutput(feedUrl, ex);
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

    private void WriteErrorInfoOutput(string feedUrl, Exception exception)
    {
        var dir = Path.Combine("./logs", "errors", feedUrl.ToSafeFileName());
        Directory.CreateDirectory(dir);
        var info = new StringBuilder();
        info.AppendLine($"Feed URL: {feedUrl}");
        info.AppendLine($"Failure: {exception.GetType().FullName}");
        info.AppendLine($"Message: {exception.Message}");

        if (exception is HttpRequestException httpRequestException && httpRequestException.StatusCode is { } statusCode)
            info.AppendLine($"HTTP Status: {(int)statusCode} {statusCode}");

        System.IO.File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString());
    }

    private void WriteErrorInfoOutput(string feedUrl, HttpResponseMessage response, string responseBody)
    {
        var dir = Path.Combine("./logs", "errors", feedUrl.ToSafeFileName());
        Directory.CreateDirectory(dir);

        var info = new StringBuilder();
        info.AppendLine($"Feed URL: {feedUrl}");
        info.AppendLine($"Final URL: {response.RequestMessage?.RequestUri}");
        info.AppendLine($"HTTP Status: {(int)response.StatusCode} {response.ReasonPhrase}");
        info.AppendLine();
        info.AppendLine("Response headers:");
        foreach (var header in response.Headers)
            info.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");

        foreach (var header in response.Content.Headers)
            info.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");

        var responseBodyPreview =
            responseBody.Length > 2048 ? responseBody[..2048] + Environment.NewLine + "... (truncated)" : responseBody;

        info.AppendLine();
        info.AppendLine("Response body preview:");
        info.AppendLine(responseBodyPreview);

        System.IO.File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString());
    }

    private ContentResult? CreateCachedResponse(string feedUrl)
    {
        if (_cache.GetLast(feedUrl) is string cachedRss)
            return Content(cachedRss, "application/rss+xml");

        return null;
    }
}
