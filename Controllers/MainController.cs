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

  public MainController(IHttpClientFactory httpClientFactory, ILogger<MainController> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  [HttpGet("filter")]
  public async Task<IActionResult> FilterFeed([FromQuery] string url)
  {
    var decodedUrl = HttpUtility.UrlDecode(url);
    var feed = await FetchFeed(decodedUrl);

    var rules = LoadFilteringRules(); // Load rules from YAML

    var rawItems = feed.Items.ToList();
    var filteredItems = rawItems.ToList();
    foreach (var rule in rules)
    {
      if (Regex.IsMatch(decodedUrl, rule.Feed, RegexOptions.IgnoreCase))
        filteredItems = ApplyRule(filteredItems, rule);
    }

    _logger.LogInformation(
      $"Filtered feed '{decodedUrl}' ({rawItems.Count - filteredItems.Count} items filtered out)."
    );

    var rss = GenerateRss(feed);
    return Content(rss, "application/rss+xml");
  }

  private async Task<SyndicationFeed> FetchFeed(string url)
  {
    var client = _httpClientFactory.CreateClient();
    var response = await client.GetStringAsync(url);

    using var reader = XmlReader.Create(new StringReader(response));
    var feed = SyndicationFeed.Load(reader);

    return feed;
  }

  private string GenerateRss(SyndicationFeed feed)
  {
    using var stringWriter = new StringWriter();
    using var xmlWriter = XmlWriter.Create(stringWriter);
    feed.SaveAsRss20(xmlWriter);
    return stringWriter.ToString();
  }

  private List<Rule> LoadFilteringRules()
  {
    var yaml = System.IO.File.ReadAllText("filtering.yaml");
    var deserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .Build();

    return deserializer.Deserialize<List<Rule>>(yaml);
  }

  private List<SyndicationItem> ApplyRule(List<SyndicationItem> items, Rule rule)
  {
    return items
      .Where(item =>
      {
        bool exclude = false;

        var itemName = item.Title.Text;

        if (rule.Match == "title" || rule.Match == "all")
          exclude |= Match("title", itemName, rule, item.Title.Text);

        if (rule.Match == "content" || rule.Match == "all")
          exclude |= Match("content", itemName, rule, item.Summary.Text);

        return !exclude;
      })
      .ToList();
  }

  private bool Match(string kind, string itemTitle, Rule rule, string text)
  {
    if (Regex.IsMatch(text, rule.Regex, RegexOptions.IgnoreCase))
    {
      _logger.LogInformation(
        $"Excluded item '{itemTitle}' from feed matching '{rule.Feed}' due to {kind} match with regex '{rule.Regex}'."
      );
      return true;
    }
    return false;
  }
}
