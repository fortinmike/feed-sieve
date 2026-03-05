using System.Net;

public sealed class UpstreamFeedClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public UpstreamFeedClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("upstream-feed");
        var upstreamResponse = await GetWithRedirectsAsync(httpClient, feedUrl, cancellationToken);
        using var response = upstreamResponse.Response;

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new UpstreamHttpStatusException(
            CreateFailureInfo(feedUrl, response, responseBody, upstreamResponse.Redirects)
        );
    }

    private static UpstreamFailureInfo CreateFailureInfo(
        string feedUrl,
        HttpResponseMessage response,
        string responseBody,
        IReadOnlyList<string> redirects
    )
    {
        var headers = new List<string>();
        foreach (var header in response.Headers)
            headers.Add($"{header.Key}: {string.Join(", ", header.Value)}");

        foreach (var header in response.Content.Headers)
            headers.Add($"{header.Key}: {string.Join(", ", header.Value)}");

        return new UpstreamFailureInfo(
            FeedUrl: feedUrl,
            FailureType: "HttpStatus",
            Message: $"Upstream feed returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
            FinalUrl: response.RequestMessage?.RequestUri?.ToString(),
            HttpStatusCode: response.StatusCode,
            HttpReasonPhrase: response.ReasonPhrase,
            Redirects: redirects,
            ResponseHeaders: headers,
            ResponseBody: responseBody
        );
    }

    private static async Task<UpstreamResponse> GetWithRedirectsAsync(
        HttpClient httpClient,
        string feedUrl,
        CancellationToken cancellationToken
    )
    {
        const int maxRedirects = 10;

        var redirects = new List<string>();
        var currentUri = new Uri(feedUrl);

        for (var redirectCount = 0; ; redirectCount++)
        {
            var response = await httpClient.GetAsync(currentUri, cancellationToken);
            if (!IsRedirect(response.StatusCode))
                return new UpstreamResponse(response, redirects);

            if (redirectCount >= maxRedirects || response.Headers.Location is not Uri location)
                return new UpstreamResponse(response, redirects);

            var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (nextUri.Scheme != Uri.UriSchemeHttp && nextUri.Scheme != Uri.UriSchemeHttps)
                return new UpstreamResponse(response, redirects);

            redirects.Add($"{(int)response.StatusCode} {currentUri} to {nextUri}");
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

    private sealed record UpstreamResponse(HttpResponseMessage Response, IReadOnlyList<string> Redirects);
}

public sealed class UpstreamHttpStatusException : Exception
{
    public UpstreamHttpStatusException(UpstreamFailureInfo failureInfo)
        : base(failureInfo.Message)
    {
        FailureInfo = failureInfo;
    }

    public UpstreamFailureInfo FailureInfo { get; }
}
