using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class FeedConverterModel : PageModel
{
    private readonly FeedDiscoveryService _feedDiscoveryService;
    private readonly FilterUrlBuilder _filterUrlBuilder;

    [BindProperty]
    public string FeedUrl { get; set; } = "";

    public string? ResolvedFeedUrl { get; private set; }

    public bool ShowResolvedFeedUrl =>
        ResolvedFeedUrl is not null && !string.Equals(ResolvedFeedUrl, FeedUrl, StringComparison.OrdinalIgnoreCase);

    public string? FilterUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public FeedConverterModel(FeedDiscoveryService feedDiscoveryService, FilterUrlBuilder filterUrlBuilder)
    {
        _feedDiscoveryService = feedDiscoveryService;
        _filterUrlBuilder = filterUrlBuilder;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var feedUrl = FeedUrl.Trim();
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var parsedFeedUrl))
        {
            ErrorMessage = "Enter a valid URL";
            return Page();
        }

        if (parsedFeedUrl.Scheme != Uri.UriSchemeHttp && parsedFeedUrl.Scheme != Uri.UriSchemeHttps)
        {
            ErrorMessage = "URL must use http or https";
            return Page();
        }

        var discoveryResult = await _feedDiscoveryService.DiscoverFeedUrlAsync(feedUrl, cancellationToken);
        if (!discoveryResult.IsSuccess)
        {
            ErrorMessage = discoveryResult.ErrorMessage;
            return Page();
        }

        var resolvedFeedUrl = discoveryResult.FeedUrl!;
        ResolvedFeedUrl = resolvedFeedUrl;
        FilterUrl = _filterUrlBuilder.Build(Request, resolvedFeedUrl);

        return Page();
    }
}
