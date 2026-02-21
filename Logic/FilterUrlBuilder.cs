using Microsoft.AspNetCore.WebUtilities;

public class FilterUrlBuilder
{
    private readonly string _secret;

    public FilterUrlBuilder(IConfiguration configuration)
    {
        _secret = configuration["Secret"] ?? "";
        if (string.IsNullOrWhiteSpace(_secret))
            throw new InvalidOperationException("Secret must be set in configuration");
    }

    public string Build(HttpRequest request, string feedUrl)
    {
        if (IsFeedSieveUrl(feedUrl))
            return feedUrl;

        var endpoint = $"{request.Scheme}://{request.Host}{request.PathBase}/filter";
        var encodedFeedUrl = Uri.EscapeDataString(feedUrl);
        var encodedSecret = Uri.EscapeDataString(_secret);
        return $"{endpoint}?url={encodedFeedUrl}&secret={encodedSecret}";
    }

    private static bool IsFeedSieveUrl(string candidateUrl)
    {
        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var candidateUri))
            return false;

        var path = candidateUri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/filter", StringComparison.OrdinalIgnoreCase))
            return false;

        var query = QueryHelpers.ParseQuery(candidateUri.Query);
        return query.ContainsKey("url");
    }
}
