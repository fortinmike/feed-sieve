using System.Text.RegularExpressions;
using System.Xml.Linq;

public class RegexFilter : FilterBase, IFilter
{
    private readonly ILogger<RegexFilter> _logger;

    public RegexFilter(ILogger<RegexFilter> logger)
    {
        _logger = logger;
    }

    public bool Keep(XElement item, Rule rule)
    {
        // Rule does not apply if there's no regex
        if (rule.Regex == null)
            return true;

        bool exclude = false;

        // Exclude based on title
        var title = GetTitle(item);
        if (rule.Match == "title" || rule.Match == "all")
            exclude |= Match("title", title, rule, title);

        // Exclude based on content
        var content = GetContent(item);
        if (rule.Match == "content" || rule.Match == "all")
            exclude |= Match("content", title, rule, content);

        return !exclude;
    }

    private bool Match(string kind, string title, Rule rule, string text)
    {
        if (!Regex.IsMatch(text, rule.Regex, RegexOptions.IgnoreCase))
            return false;

        _logger.LogDebug(
            $"Excluded '{title}' from feed '{rule.Feed}' based on rule '{rule.Name}' due to {kind} match with regex '{rule.Regex}'"
        );
        return true;
    }
}
