using System.Net;

public sealed record UpstreamFailureInfo(
    string FeedUrl,
    string FailureType,
    string Message,
    string? FinalUrl = null,
    HttpStatusCode? HttpStatusCode = null,
    string? HttpReasonPhrase = null,
    IReadOnlyList<string>? Redirects = null,
    IReadOnlyList<string>? ResponseHeaders = null,
    string? ResponseBody = null
)
{
    public static UpstreamFailureInfo FromException(string feedUrl, Exception exception)
    {
        HttpStatusCode? statusCode = null;
        if (exception is HttpRequestException httpRequestException)
            statusCode = httpRequestException.StatusCode;

        return new UpstreamFailureInfo(
            FeedUrl: feedUrl,
            FailureType: exception.GetType().FullName ?? exception.GetType().Name,
            Message: exception.Message,
            HttpStatusCode: statusCode
        );
    }

    public string? GetResponseHeaderValue(string name)
    {
        if (ResponseHeaders == null)
            return null;

        var prefix = name + ":";

        foreach (var header in ResponseHeaders)
        {
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            return header[prefix.Length..].Trim();
        }

        return null;
    }
}
