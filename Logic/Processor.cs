using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;

public class Processor
{
    private readonly ILogger<Processor> _logger;
    private readonly IEnumerable<IFilter> _filters;

    public Processor(ILogger<Processor> logger, IEnumerable<IFilter> filters)
    {
        _logger = logger;
        _filters = filters;
    }

    public XDocument Process(XDocument originalDocument, List<Rule> rules, string feedUrl)
    {
        // Loading rules is a very fast operation and doing it here ensures
        // that we can modify them and they will be applied instantly.
        var rulesForFeed = rules.Where(r => RuleAppliesToFeed(r.Feed, feedUrl)).ToList();

        _logger.LogInformation($"Found {rulesForFeed.Count} rules matching feed {feedUrl}:");
        rulesForFeed.ForEach(r => _logger.LogDebug($"- {r.Name}"));

        var itemsBefore = GetItems(originalDocument).ToList();
        var filteredItems = rulesForFeed.Aggregate(itemsBefore, FilterWithRule);

        // Create a new document and modify it
        var modifiedDocument = new XDocument(originalDocument);
        ReplaceItemsInDocument(modifiedDocument, filteredItems);

        _logger.LogInformation(
            $"Filtered feed '{feedUrl}' (Before: {itemsBefore.Count}, After: {filteredItems.Count})"
        );

        return modifiedDocument;
    }

    private bool RuleAppliesToFeed(string? ruleFeed, string feedUrl)
    {
        // When a rule has no feed specified, then it applies to all feeds
        if (ruleFeed == null)
            return true;

        var normalizedRule = NormalizeUri(ruleFeed);
        var normalizedFeed = NormalizeUri(feedUrl);
        if (normalizedRule == null || normalizedFeed == null)
            return false;

        var ruleHost = normalizedRule.Host;
        var feedHost = normalizedFeed.Host;
        if (!string.Equals(ruleHost, feedHost, StringComparison.OrdinalIgnoreCase))
            return false;

        if (HasSpecificPath(normalizedRule) && !PathMatches(normalizedRule, normalizedFeed))
            return false;

        return !HasQuery(normalizedRule) || QueryMatches(normalizedRule, normalizedFeed);
    }

    private bool HasSpecificPath(Uri uri)
    {
        var path = NormalizePath(uri.AbsolutePath);
        return path != "";
    }

    private string NormalizePath(string path)
    {
        if (path == "/" || string.IsNullOrWhiteSpace(path))
            return "";

        return path.TrimEnd('/').ToLowerInvariant();
    }

    private bool PathMatches(Uri ruleUri, Uri feedUri)
    {
        var rulePath = NormalizePath(ruleUri.AbsolutePath);
        var feedPath = NormalizePath(feedUri.AbsolutePath);
        return string.Equals(rulePath, feedPath, StringComparison.Ordinal);
    }

    private bool HasQuery(Uri uri)
    {
        return !string.IsNullOrWhiteSpace(uri.Query);
    }

    private bool QueryMatches(Uri ruleUri, Uri feedUri)
    {
        var ruleQuery = QueryHelpers.ParseQuery(ruleUri.Query);
        var feedQuery = QueryHelpers.ParseQuery(feedUri.Query);

        foreach (var queryParam in ruleQuery)
        {
            if (!feedQuery.TryGetValue(queryParam.Key, out var values))
                return false;

            if (!queryParam.Value.All(v => values.Contains(v)))
                return false;
        }

        return true;
    }

    private Uri? NormalizeUri(string url)
    {
        try
        {
            // Add scheme if not present
            url = Regex.Replace(url, @"^(?!https?://)", "https://", RegexOptions.IgnoreCase);

            var uri = new Uri(url);
            return new UriBuilder(uri)
            {
                Scheme = "https",
                Port = 443,
                Host = uri.Host.ToLowerInvariant()
            }.Uri;
        }
        catch (UriFormatException ex)
        {
            _logger.LogError(ex.Message);
            _logger.LogError($"Invalid URI is {url}");
            return null;
        }
    }

    private IEnumerable<XElement> GetItems(XDocument doc)
    {
        if (doc.Root == null)
            yield break;

        // RSS 2.0: <rss><channel><item>…</item></channel></rss>
        if (doc.Root.Name.LocalName == "rss")
        {
            var channel = doc.Root.Element("channel");
            if (channel != null)
                foreach (var item in channel.Elements("item"))
                    yield return item;
            yield break;
        }

        // Atom: <feed xmlns="http://www.w3.org/2005/Atom"><entry>…</entry></feed>
        if (doc.Root.Name.LocalName == "feed" && doc.Root.Name.NamespaceName == Constants.AtomNamespace)
        {
            foreach (var entry in doc.Root.Elements(XName.Get("entry", Constants.AtomNamespace)))
                yield return entry;
        }
    }

    private List<XElement> FilterWithRule(List<XElement> items, Rule rule)
    {
        return items.Where(i => _filters.All(filter => filter.Keep(i, rule))).ToList();
    }

    private void ReplaceItemsInDocument(XDocument document, List<XElement> filteredItems)
    {
        var channel = document.Root?.Element("channel");
        if (channel != null) // RSS
        {
            channel.Elements("item").Remove();
            channel.Add(filteredItems);
        }
        else // Atom
        {
            document.Root?.Elements(XName.Get("entry", Constants.AtomNamespace)).Remove();
            document.Root?.Add(filteredItems);
        }
    }
}
