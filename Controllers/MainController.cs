using System.Runtime.InteropServices;
using System.Web;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class MainController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MainController> _logger;
    private readonly Processor _processor;
    private readonly Cache _cache;

    public MainController(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<MainController> logger,
        Processor processor,
        Cache cache
    )
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _processor = processor;
        _cache = cache;
    }

    public IActionResult Index()
    {
        return Content("feed-sieve is running!", "text/html");
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

        // Fetch the RSS feed XML and get the XML
        var feedUrl = HttpUtility.UrlDecode(url);
        var originalRss = await new HttpClient().GetStringAsync(feedUrl);

        // Load the rules
        var rulesString = System.IO.File.ReadAllText($"rules.{ruleset}.yaml");
        var rules = Rules.Parse(rulesString);

        var rssHash = originalRss.Hash();
        var rulesHash = rulesString.Hash();
        var hash = rssHash + rulesHash;

        // Results are cached as long as neither the original RSS nor the rules string change
        var cacheEnabled = _configuration["Cache"] == "True";
        var cachedRss = _cache.Get(feedUrl, hash);
        if (cacheEnabled && cachedRss != null)
        {
            // Return the cached RSS document
            WriteOutputInDevMode(originalRss, cachedRss);
            _logger.LogInformation($"Returned cached RSS for feed {feedUrl} because nothing changed");
            return Content(cachedRss, "application/rss+xml");
        }
        else
        {
            // Modify the original RSS document by processing it with the loaded rules
            var originalDocument = XDocument.Parse(originalRss);
            var modifiedDocument = _processor.Process(originalDocument, rules, feedUrl);
            var modifiedRss = modifiedDocument.ToString();
            if (cacheEnabled)
                _cache.Set(feedUrl, hash, modifiedRss);
            WriteOutputInDevMode(originalRss, modifiedRss);
            return Content(modifiedRss, "application/rss+xml");
        }
    }

    private void WriteOutputInDevMode(string original, string modified)
    {
        if (_env.IsDevelopment())
        {
            var dir = "./output";
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText($"{dir}/original.xml", original);
            System.IO.File.WriteAllText($"{dir}/modified.xml", modified);
        }
    }
}
