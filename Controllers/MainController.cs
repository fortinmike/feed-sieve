using System.ServiceModel.Syndication;
using System.Web;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class MainController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MainController> _logger;
    private readonly Filter _filter;

    public MainController(IHttpClientFactory httpClientFactory, ILogger<MainController> logger, Filter filter)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _filter = filter;
    }

    public IActionResult Index()
    {
        return Content("feed-sieve is running!", "text/html");
    }

    [HttpGet("filter")]
    public async Task<IActionResult> Filter([FromQuery] string url)
    {
        var feedUrl = HttpUtility.UrlDecode(url);
        var feed = await FetchFeed(feedUrl);

        var rawItems = feed.Items.ToList();
        var filteredItems = _filter.Process(feedUrl, feed.Items).ToList();

        _logger.LogInformation($"Filtered feed '{feedUrl}' (Before: {rawItems.Count}, After: {filteredItems.Count})");

        feed.Items = filteredItems; // Replace the feed items with the filtered items

        return Content(Rss.Serialize(feed), "application/rss+xml");
    }

    private async Task<SyndicationFeed> FetchFeed(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var xmlString = await client.GetStringAsync(url);
        return Rss.Parse(xmlString);
    }
}
