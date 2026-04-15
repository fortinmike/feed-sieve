using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;

public class Processor
{
    private readonly ILogger<Processor> _logger;
    private readonly IEnumerable<IFilter> _filters;
    private readonly SummaryCache _summaryCache;
    private readonly ArticleSummarizer _summarizer;

    public Processor(
        ILogger<Processor> logger,
        IEnumerable<IFilter> filters,
        SummaryCache summaryCache,
        ArticleSummarizer summarizer
    )
    {
        _logger = logger;
        _filters = filters;
        _summaryCache = summaryCache;
        _summarizer = summarizer;
    }

    #region Public API

    public async Task<XDocument> Process(
        XDocument originalDocument,
        RulesConfig rules,
        string feedUrl,
        CancellationToken cancellationToken
    )
    {
        var feedRule = GetFeedRule(rules.Feeds, feedUrl);
        var filterRules = rules.GlobalFilters.Concat(feedRule?.Filters ?? []).ToList();

        _logger.LogInformation(
            "Found {FilterRuleCount} filter rules for {FeedUrl} and summary is {SummaryState}",
            filterRules.Count,
            feedUrl,
            feedRule?.Summary is null ? "disabled" : "enabled"
        );
        filterRules.ForEach(rule => _logger.LogDebug("- {RuleName}", rule.Name));

        var itemsBefore = GetItems(originalDocument).ToList();
        itemsBefore.ForEach(PrefixYouTubeShortTitles);
        var filteredItems = filterRules.Aggregate(itemsBefore, FilterWithRule);
        var summarizedItems = await SummarizeItemsAsync(filteredItems, feedRule, feedUrl, cancellationToken);

        var modifiedDocument = new XDocument(originalDocument);
        ReplaceItemsInDocument(modifiedDocument, summarizedItems);

        _logger.LogInformation(
            $"Processed feed '{feedUrl}' (Before: {itemsBefore.Count}, After: {summarizedItems.Count})"
        );

        return modifiedDocument;
    }

    #endregion

    #region Rule Matching

    private FeedRule? GetFeedRule(List<FeedRule> rules, string feedUrl)
    {
        var matchingRules = rules.Where(rule => RuleAppliesToFeed(rule.Feed, feedUrl)).ToList();
        if (matchingRules.Count <= 1)
            return matchingRules.FirstOrDefault();

        _logger.LogWarning(
            "Found {Count} matching feed configs for {FeedUrl}; using the first one",
            matchingRules.Count,
            feedUrl
        );
        return matchingRules[0];
    }

    private bool RuleAppliesToFeed(string ruleFeed, string feedUrl)
    {
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

    private List<XElement> FilterWithRule(List<XElement> items, FilterRule rule)
    {
        return items.Where(i => _filters.All(filter => filter.Keep(i, rule))).ToList();
    }

    private async Task<List<XElement>> SummarizeItemsAsync(
        List<XElement> items,
        FeedRule? feedRule,
        string feedUrl,
        CancellationToken cancellationToken
    )
    {
        if (feedRule?.Summary is null)
            return items;

        if (!_summarizer.IsConfigured)
            return items;

        var prompt = GetSummaryPrompt(feedRule.Summary);
        if (string.IsNullOrWhiteSpace(prompt))
            return items;

        _logger.LogInformation(
            "Starting summarization for {ItemCount} item(s) in {FeedUrl}",
            items.Count,
            feedUrl
        );

        var summarizedItems = new List<XElement>(items.Count);
        foreach (var item in items)
        {
            summarizedItems.Add(await SummarizeItemAsync(item, feedUrl, prompt, cancellationToken));
        }

        return summarizedItems;
    }

    private async Task<XElement> SummarizeItemAsync(
        XElement item,
        string feedUrl,
        string prompt,
        CancellationToken cancellationToken
    )
    {
        var content = GetContent(item);
        if (string.IsNullOrWhiteSpace(content))
            return item;

        var contentText = HtmlContentTextExtractor.Extract(content);
        if (contentText.Length < _summarizer.MinimumContentLength)
            return item;

        var itemKey = GetItemKey(item);
        var hash = $"{content}\n{prompt}".Hash();
        var summary = _summaryCache.Get(feedUrl, itemKey, hash);
        if (summary is null)
        {
            var title = GetTitle(item);
            _logger.LogDebug("Starting summarization for item '{ItemTitle}'", title);

            summary = await _summarizer.SummarizeAsync(title, contentText, prompt, cancellationToken);
            if (summary is null)
                return item;

            summary = NormalizeSummaryHtml(summary);
            _summaryCache.Set(feedUrl, itemKey, hash, summary);
        }

        if (!TryPrependSummary(item, summary, content))
            return item;

        return item;
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

    #region Summary Rewriting

    private string GetSummaryPrompt(SummaryRule summary)
    {
        return string.IsNullOrWhiteSpace(summary.Prompt) ? _summarizer.DefaultPrompt : summary.Prompt.Trim();
    }

    private string GetItemKey(XElement item)
    {
        var key = GetValue(item, "guid");
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        key = GetValue(item, "id", Constants.AtomNamespace);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        key = GetLink(item);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        key = GetTitle(item);
        return string.IsNullOrWhiteSpace(key) ? item.ToString(SaveOptions.DisableFormatting) : key;
    }

    private bool TryPrependSummary(XElement item, string summaryHtml, string originalContent)
    {
        var rewrittenContent = $"{summaryHtml}<hr>{originalContent}";

        var rssContent = item.Element(XName.Get("encoded", "http://purl.org/rss/1.0/modules/content/"));
        if (rssContent != null)
        {
            rssContent.ReplaceNodes(new XCData(rewrittenContent));
            return true;
        }

        var rssDescription = item.Element("description");
        if (rssDescription != null)
        {
            rssDescription.ReplaceNodes(new XCData(rewrittenContent));
            return true;
        }

        var atomContent = item.Element(XName.Get("content", Constants.AtomNamespace));
        if (atomContent != null)
        {
            if (IsAtomXhtml(atomContent))
                return false;

            atomContent.SetAttributeValue("type", "html");
            atomContent.Value = rewrittenContent;
            return true;
        }

        var atomSummary = item.Element(XName.Get("summary", Constants.AtomNamespace));
        if (atomSummary != null)
        {
            if (IsAtomXhtml(atomSummary))
                return false;

            atomSummary.SetAttributeValue("type", "html");
            atomSummary.Value = rewrittenContent;
            return true;
        }

        return false;
    }

    private bool IsAtomXhtml(XElement element)
    {
        var type = (string?)element.Attribute("type");
        return string.Equals(type, "xhtml", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeSummaryHtml(string summary)
    {
        var trimmed = summary.Trim();
        if (trimmed.Contains('<') && trimmed.Contains('>'))
            return trimmed;

        return $"<p>{HttpUtility.HtmlEncode(trimmed)}</p>";
    }

    #endregion

    #region YouTube Short Tagging

    private void PrefixYouTubeShortTitles(XElement item)
    {
        var linkUrl = GetLink(item);
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

    private string GetLink(XElement item)
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

    private string GetValue(XElement parent, string localName, string? ns = null)
    {
        return (string?)parent.Element(ns is null ? localName : XName.Get(localName, ns)) ?? "";
    }

    private string GetTitle(XElement item)
    {
        var title = GetValue(item, "title");
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        return GetValue(item, "title", Constants.AtomNamespace);
    }

    private string GetContent(XElement item)
    {
        var content = GetValue(item, "encoded", "http://purl.org/rss/1.0/modules/content/");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        content = GetValue(item, "description");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        content = GetValue(item, "content", Constants.AtomNamespace);
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        return GetValue(item, "summary", Constants.AtomNamespace);
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
