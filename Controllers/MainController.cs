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

    var rules = LoadRules(); // Load rules from YAML

    foreach (var rule in rules)
    {
      if (Regex.IsMatch(decodedUrl, rule.Match, RegexOptions.IgnoreCase))
        ApplyRule(feed, rule);
    }

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

  private List<Rule> LoadRules()
  {
    var yaml = System.IO.File.ReadAllText("rules.yaml");
    var deserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .Build();

    return deserializer.Deserialize<List<Rule>>(yaml);
  }

  private void ApplyRule(SyndicationFeed feed, Rule rule)
  {
    var kept = feed
      .Items.Where(item =>
      {
        bool exclude = false;

        var itemName = item.Title.Text;

        if (rule.Exclude.MatchTitle)
          exclude |= Match("title", rule, itemName, item.Title.Text);

        if (rule.Exclude.MatchContent)
          exclude |= Match("description", rule, itemName, item.Summary.Text);

        return !exclude;
      })
      .ToList();

    feed.Items = kept;
  }

  private bool Match(string itemTitle, Rule rule, string kind, string text)
  {
    foreach (var regex in rule.Exclude.Regexes)
    {
      if (Regex.IsMatch(text, regex, RegexOptions.IgnoreCase))
      {
        _logger.LogInformation(
          $"Excluded item '{itemTitle}' from feed matching '{rule.Match}' due to {kind} match with regex '{regex}'."
        );
        return true;
      }
    }
    return false;
  }
}
