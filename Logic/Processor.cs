using System.Text.RegularExpressions;
using System.Xml.Linq;

public class Processor
{
    private readonly ILogger<MainController> _logger;
    private readonly IEnumerable<IFilter> _filters;

    public Processor(ILogger<MainController> logger, IEnumerable<IFilter> filters)
    {
        _logger = logger;
        _filters = filters;
    }

    public XDocument Process(XDocument originalDocument, List<Rule> rules, string feedUrl)
    {
        // Loading rules is a very fast operation and doing it here ensures
        // that we can modify the rules and they will be applied instantly.
        var rulesForFeed = rules
            .Where(r =>
                r.Host == null // When a rule has no feed specified, then it applies to all feeds
                || NormalizeUri(r.Host) == NormalizeUri(feedUrl) // Otherwise check if the rule applies to the feed we're processing
            )
            .ToList();

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

    private Uri? NormalizeUri(string url)
    {
        try
        {
            // Add scheme if not present
            url = Regex.Replace(url, @"^(?!https?://)", "https://", RegexOptions.IgnoreCase);

            var uri = new Uri(url);
            return new UriBuilder(uri)
            {
                Scheme = "https", // Make sure differing schemes still match
                Port = 443, // Same for port, otherwise UriBuilder adds `:80` under certain conditions
                Host = uri.Host.ToLowerInvariant(),
                Path = "" // Don't consider the path (host is granual enough for 99% of cases and simpler to configure)
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
