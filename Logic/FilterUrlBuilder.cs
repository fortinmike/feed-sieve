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
        var endpoint = $"{request.Scheme}://{request.Host}{request.PathBase}/filter";
        var encodedFeedUrl = Uri.EscapeDataString(feedUrl);
        var encodedSecret = Uri.EscapeDataString(_secret);
        return $"{endpoint}?url={encodedFeedUrl}&secret={encodedSecret}";
    }
}
