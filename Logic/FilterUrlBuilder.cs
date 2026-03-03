using Microsoft.AspNetCore.WebUtilities;

public class FilterUrlBuilder
{
    private readonly string _secret;

    public FilterUrlBuilder(IConfiguration configuration)
    {
        _secret = configuration.GetRequiredSection("Secret").Value!;
    }

    public string Build(HttpRequest request, string feedUrl)
    {
        if (IsFeedSieveUrl(feedUrl))
            return feedUrl;

        var endpoint = $"{BuildHttpsBaseUrl(request)}/filter";
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

    private static string BuildHttpsBaseUrl(HttpRequest request)
    {
        var authority = request.Host.Port is null or 80 or 443 ? request.Host.Host : request.Host.Value;
        return $"https://{authority}{request.PathBase}";
    }
}
