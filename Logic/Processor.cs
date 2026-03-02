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

    #region Public API

    public XDocument Process(XDocument originalDocument, List<Rule> rules, string feedUrl)
    {
        // Loading rules is a very fast operation and doing it here ensures
        // that we can modify them and they will be applied instantly.
        var rulesForFeed = rules.Where(r => RuleAppliesToFeed(r.Feed, feedUrl)).ToList();

        _logger.LogInformation($"Found {rulesForFeed.Count} rules matching feed {feedUrl}:");
        rulesForFeed.ForEach(r => _logger.LogDebug($"- {r.Name}"));

        var itemsBefore = GetItems(originalDocument).ToList();
        itemsBefore.ForEach(PrefixYouTubeShortTitles);
        var filteredItems = rulesForFeed.Aggregate(itemsBefore, FilterWithRule);

        // Create a new document and modify it
        var modifiedDocument = new XDocument(originalDocument);
        ReplaceItemsInDocument(modifiedDocument, filteredItems);

        _logger.LogInformation(
            $"Filtered feed '{feedUrl}' (Before: {itemsBefore.Count}, After: {filteredItems.Count})"
        );

        return modifiedDocument;
    }

    #endregion

    #region Rule Matching

    private bool RuleAppliesToFeed(string? ruleFeed, string feedUrl)
    {
        // When a rule has no feed specified, then it applies to all feeds
        if (ruleFeed == null)
            return true;

        var normalizedRule = NormalizeUri(ruleFeed);
        var normalizedFeed = NormalizeUri(feedUrl);
        if (normalizedRule == null || normalizedFeed == null)
            return false;

        var ruleHost = NormalizeHostForMatching(normalizedRule.Host);
        var feedHost = NormalizeHostForMatching(normalizedFeed.Host);
        if (!HostMatches(ruleHost, feedHost))
            return false;

        if (HasSpecificPath(normalizedRule) && !PathMatches(normalizedRule, normalizedFeed))
            return false;

        return !HasQuery(normalizedRule) || QueryMatches(normalizedRule, normalizedFeed);
    }

    private string NormalizeHostForMatching(string host)
    {
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host["www.".Length..]
            : host;
    }

    private bool HostMatches(string ruleHost, string feedHost)
    {
        return string.Equals(ruleHost, feedHost, StringComparison.OrdinalIgnoreCase)
            || feedHost.EndsWith($".{ruleHost}", StringComparison.OrdinalIgnoreCase);
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
        if (!feedPath.StartsWith(rulePath, StringComparison.Ordinal))
            return false;

        return feedPath.Length == rulePath.Length || feedPath[rulePath.Length] == '/';
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

    #endregion

    #region URI Normalization

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

    #endregion

    #region Item Processing

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

    #endregion

    #region Document Rewriting

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

    #endregion

    #region YouTube Short Tagging

    private void PrefixYouTubeShortTitles(XElement item)
    {
        var linkUrl = GetTitleUrl(item);
        if (!IsYouTubeShortUrl(linkUrl))
            return;

        var titleElement = GetTitleElement(item);
        if (titleElement == null)
            return;

        var title = titleElement.Value;
        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("[Short] ", StringComparison.Ordinal))
            return;

        titleElement.Value = $"[Short] {title}";
    }

    private XElement? GetTitleElement(XElement item)
    {
        return item.Elements().FirstOrDefault(e => e.Name.LocalName == "title");
    }

    private string GetTitleUrl(XElement item)
    {
        var linkElement = item.Elements()
            .Where(e => e.Name.LocalName == "link")
            .OrderByDescending(e =>
                string.Equals((string?)e.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase)
            )
            .FirstOrDefault();
        if (linkElement == null)
            return "";

        var href = (string?)linkElement.Attribute("href");
        if (!string.IsNullOrWhiteSpace(href))
            return href;

        return linkElement.Value;
    }

    private bool IsYouTubeShortUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Contains("youtube.com/shorts", StringComparison.OrdinalIgnoreCase);

        return uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/shorts", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
