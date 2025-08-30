using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;

public class Filter
{
    private readonly ILogger<MainController> _logger;

    public Filter(ILogger<MainController> logger)
    {
        _logger = logger;
    }

    public List<SyndicationItem> Process(string feedUrl, IEnumerable<SyndicationItem> items)
    {
        // Loading rules is a very fast operation and doing it here ensures
        // that we can modify the rules and they will be applied instantly.
        var rules = Rules.Load("ruleset.default.yaml");

        var filteredItems = items.ToList();
        foreach (var rule in rules)
        {
            if (Regex.IsMatch(feedUrl, rule.Feed, RegexOptions.IgnoreCase))
                filteredItems = ApplyRule(filteredItems, rule);
        }

        return filteredItems;
    }

    private List<SyndicationItem> ApplyRule(List<SyndicationItem> items, Rule rule)
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
            _logger.LogDebug(
                $"Excluded '{itemTitle}' from '{rule.Feed}' due to {kind} match with regex '{rule.Regex}'."
            );
            return true;
        }
        return false;
    }
}
