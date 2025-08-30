using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

    [HttpGet("feed")]
    public async Task<IActionResult> Feed([FromQuery] string url)
    {
        var feedUrl = HttpUtility.UrlDecode(url);
        var feed = await FetchFeed(feedUrl);

        var rawItems = feed.Items.ToList();
        var filteredItems = _filter.Process(feedUrl, feed.Items);

        _logger.LogInformation($"Filtered feed '{feedUrl}' ({rawItems.Count} -> {filteredItems.Count})");

        return Content(Rss.Serialize(feed), "application/rss+xml");
    }

    private async Task<SyndicationFeed> FetchFeed(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var xmlString = await client.GetStringAsync(url);
        return Rss.Parse(xmlString);
    }
}
