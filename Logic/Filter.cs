using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;

public class Filter
{
    private readonly ILogger<MainController> _logger;

    public Filter(ILogger<MainController> logger)
    {
        _logger = logger;
    }

    public IEnumerable<SyndicationItem> Process(string feedUrl, IEnumerable<SyndicationItem> unfilteredItems)
    {
        // Loading rules is a very fast operation and doing it here ensures
        // that we can modify the rules and they will be applied instantly.
        var rules = Rules.Load("ruleset.default.yaml");

        var rulesForFeed = rules
            .Where(r =>
                r.Feed == null // When a rule has no feed specified, then it applies to all feeds
                || MatchFeedUrl(r.Feed, feedUrl) // Otherwise check if the rule matches the feed we're processing
            )
            .ToList();

        _logger.LogInformation($"Found {rulesForFeed.Count} rules matching feed {feedUrl}:");
        rulesForFeed.ForEach(r => _logger.LogDebug($"- {r.Name}"));

        return rulesForFeed.Aggregate(unfilteredItems, FilterWithRule);
    }

    private bool MatchFeedUrl(string url1, string url2)
    {
        try
        {
            return NormalizeUri(url1) == NormalizeUri(url2);
        }
        catch (UriFormatException ex)
        {
            _logger.LogError(ex.Message);
            _logger.LogError($"Invalid URI is either {url1} or {url2}");
            return false;
        }
    }

    private IEnumerable<SyndicationItem> FilterWithRule(IEnumerable<SyndicationItem> items, Rule rule)
    {
        return items
            .Where(item =>
            {
                bool exclude = false;

                var itemName = item.Title.Text;

                // Exclude based on item title
                if (rule.Match == "title" || rule.Match == "all")
                    exclude |= Match("title", itemName, rule, item.Title.Text);

                // Exclude based on item content
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
            _logger.LogDebug("Excluded '{Item}'", itemTitle);
            _logger.LogDebug("  from feed '{Feed}'", rule.Feed);
            _logger.LogDebug("  based on rule '{Rule}'", rule.Name);
            _logger.LogDebug("  due to {Kind} match with regex '{Regex}'", kind, rule.Regex);
            return true;
        }
        return false;
    }

    private Uri NormalizeUri(string url)
    {
        // Add scheme if not present
        url = Regex.Replace(url, @"^(?!https?://)", "https://", RegexOptions.IgnoreCase);

        var uri = new Uri(url);

        return new UriBuilder(uri)
        {
            Scheme = "https", // Make sure differing schemes still match
            Port = 443, // Same for port, otherwise UriBuilder adds `:80` under certain conditions
            Host = uri.Host.ToLowerInvariant(),
            Path = uri.AbsolutePath.ToLowerInvariant().TrimEnd('/'), // Case insensitive and ignore trailing slash
        }.Uri;
    }
}
