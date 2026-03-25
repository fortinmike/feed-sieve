using System.Net;
using System.Text.RegularExpressions;

public sealed partial class FeedDiscoveryService
{
    private static readonly HashSet<string> FeedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/rss+xml",
        "application/atom+xml",
        "application/feed+json",
        "application/rdf+xml"
    };

    private static readonly HashSet<string> DiscoverableFeedLinkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/rss+xml",
        "application/atom+xml",
        "application/feed+json",
        "application/rdf+xml"
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public FeedDiscoveryService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<FeedDiscoveryResult> DiscoverFeedUrlAsync(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("upstream-feed");
        using var response = await GetWithRedirectsAsync(httpClient, url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return FeedDiscoveryResult.Error("Couldn't fetch the URL for feed discovery");

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (IsDirectFeed(mediaType, body))
            return FeedDiscoveryResult.Success(finalUrl);

        if (!IsHtml(mediaType, body))
            return FeedDiscoveryResult.Error("The URL did not return a feed or an HTML page with feed links");

        var discoveredFeedUrls = DiscoverFeedUrls(body, finalUrl);
        if (discoveredFeedUrls.Count == 0)
        {
            if (TryCreateYouTubeFeedUrl(finalUrl, body, out var youTubeFeedUrl))
                return FeedDiscoveryResult.Success(youTubeFeedUrl);

            return FeedDiscoveryResult.Error("No feed link was found on that page");
        }

        var preferredFeedUrls = FilterOutCommentFeeds(discoveredFeedUrls);
        if (preferredFeedUrls.Count == 1)
            return FeedDiscoveryResult.Success(preferredFeedUrls[0].Url);

        if (preferredFeedUrls.Count > 1)
            return FeedDiscoveryResult.Error("Multiple feed links were found on that page");

        return FeedDiscoveryResult.Success(discoveredFeedUrls[0].Url);
    }

    private static bool IsDirectFeed(string? mediaType, string body)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) && FeedContentTypes.Contains(mediaType))
            return true;

        var trimmed = SkipXmlDeclaration(body.TrimStart());
        if (trimmed.StartsWith("<rss", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.StartsWith("<feed", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.StartsWith("<rdf:RDF", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("<channel", StringComparison.OrdinalIgnoreCase))
            return true;

        return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.Contains("jsonfeed.org/version/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtml(string? mediaType, string body)
    {
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.EndsWith("+html", StringComparison.OrdinalIgnoreCase))
            return true;

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
            return true;

        return trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static string SkipXmlDeclaration(string text)
    {
        if (!text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            return text;

        var xmlDeclarationEnd = text.IndexOf("?>", StringComparison.Ordinal);
        return xmlDeclarationEnd < 0 ? text : text[(xmlDeclarationEnd + 2)..].TrimStart();
    }

    private static List<DiscoveredFeedLink> DiscoverFeedUrls(string html, string pageUrl)
    {
        var discoveredUrls = new List<DiscoveredFeedLink>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan the full document because YouTube sometimes places feed links after <head>
        foreach (Match linkMatch in LinkTagRegex().Matches(html))
        {
            if (!TryCreateFeedLink(linkMatch.Groups["attributes"].Value, pageUrl, out var feedLink))
                continue;

            if (seenUrls.Add(feedLink.Url))
                discoveredUrls.Add(feedLink);
        }

        return discoveredUrls;
    }

    private static List<DiscoveredFeedLink> FilterOutCommentFeeds(List<DiscoveredFeedLink> feedLinks)
    {
        var preferredLinks = feedLinks.Where(link => !LooksLikeCommentFeed(link)).ToList();
        return preferredLinks.Count == 0 ? feedLinks : preferredLinks;
    }

    private static bool LooksLikeCommentFeed(DiscoveredFeedLink link)
    {
        if (link.Url.Contains("/comments", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(link.Title) && link.Title.Contains("comment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateFeedLink(
        string rawAttributes,
        string pageUrl,
        out DiscoveredFeedLink feedLink
    )
    {
        feedLink = new("", null);
        var attributes = ParseAttributes(rawAttributes);
        if (!attributes.TryGetValue("rel", out var rel))
            return false;

        var relTokens = rel
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!relTokens.Contains("alternate", StringComparer.OrdinalIgnoreCase))
            return false;

        if (!attributes.TryGetValue("type", out var type) || !DiscoverableFeedLinkTypes.Contains(type))
            return false;

        if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href))
            return false;

        if (!Uri.TryCreate(new Uri(pageUrl), href, out var resolvedUrl))
            return false;

        if (resolvedUrl.Scheme != Uri.UriSchemeHttp && resolvedUrl.Scheme != Uri.UriSchemeHttps)
            return false;

        feedLink = new DiscoveredFeedLink(
            resolvedUrl.ToString(),
            attributes.TryGetValue("title", out var title) ? title : null
        );
        return true;
    }

    private static Dictionary<string, string> ParseAttributes(string rawAttributes)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attributeMatch in AttributeRegex().Matches(rawAttributes))
        {
            var name = attributeMatch.Groups["name"].Value;
            var value = attributeMatch.Groups["doubleQuoted"].Success
                ? attributeMatch.Groups["doubleQuoted"].Value
                : attributeMatch.Groups["singleQuoted"].Success
                    ? attributeMatch.Groups["singleQuoted"].Value
                    : attributeMatch.Groups["unquoted"].Value;
            attributes[name] = WebUtility.HtmlDecode(value);
        }

        return attributes;
    }

    private static async Task<HttpResponseMessage> GetWithRedirectsAsync(
        HttpClient httpClient,
        string url,
        CancellationToken cancellationToken
    )
    {
        const int maxRedirects = 10;
        var currentUri = new Uri(url);

        for (var redirectCount = 0; ; redirectCount++)
        {
            var response = await httpClient.GetAsync(currentUri, cancellationToken);
            if (!IsRedirect(response.StatusCode))
                return response;

            if (redirectCount >= maxRedirects || response.Headers.Location is not Uri location)
                return response;

            var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (nextUri.Scheme != Uri.UriSchemeHttp && nextUri.Scheme != Uri.UriSchemeHttps)
                return response;

            response.Dispose();
            currentUri = nextUri;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode
            is HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
    }

    private static bool TryCreateYouTubeFeedUrl(string pageUrl, string html, out string feedUrl)
    {
        feedUrl = "";
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            return false;

        if (!pageUri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var channelIdMatch = YouTubeChannelIdRegex().Match(html);
        if (!channelIdMatch.Success)
            return false;

        feedUrl = $"https://www.youtube.com/feeds/videos.xml?channel_id={channelIdMatch.Groups["channelId"].Value}";
        return true;
    }

    [GeneratedRegex("<link\\b(?<attributes>[^>]*?)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex("(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)\\s*=\\s*(?:\"(?<doubleQuoted>[^\"]*)\"|'(?<singleQuoted>[^']*)'|(?<unquoted>[^\\s\"'=<>`]+))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("\"browseId\":\"(?<channelId>UC[0-9A-Za-z_-]{22})\"|\"channelId\":\"(?<channelId>UC[0-9A-Za-z_-]{22})\"|itemprop=\"channelId\" content=\"(?<channelId>UC[0-9A-Za-z_-]{22})\"", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeChannelIdRegex();
}

public sealed record FeedDiscoveryResult(string? FeedUrl, string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null && FeedUrl is not null;

    public static FeedDiscoveryResult Success(string feedUrl) => new(feedUrl, null);

    public static FeedDiscoveryResult Error(string errorMessage) => new(null, errorMessage);
}

public sealed record DiscoveredFeedLink(string Url, string? Title);
