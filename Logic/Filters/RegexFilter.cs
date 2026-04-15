using System.Text.RegularExpressions;
using System.Xml.Linq;

public class RegexFilter : FilterBase, IFilter
{
    private readonly ILogger<RegexFilter> _logger;

    public RegexFilter(ILogger<RegexFilter> logger)
    {
        _logger = logger;
    }

    public bool Keep(XElement item, FilterRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Regex))
            return true;

        bool exclude = false;

        // Exclude based on title
        var title = GetTitle(item);
        if (rule.Match == "title" || rule.Match == "all")
            exclude |= Match("title", title, rule, title);

        // Exclude based on content
        var content = GetContent(item);
        var contentText = GetContentText(content);
        if (rule.Match == "content" || rule.Match == "all")
            exclude |= Match("content", title, rule, contentText) || Match("content (raw)", title, rule, content);

        return !exclude;
    }

    private bool Match(string kind, string title, FilterRule rule, string text)
    {
        var options = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        if (!Regex.IsMatch(text, rule.Regex, options))
            return false;

        _logger.LogDebug(
            $"Excluded '{title}' based on rule '{rule.Name}' due to {kind} match with regex '{rule.Regex}'"
        );
        return true;
    }
}
