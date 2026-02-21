using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class FeedConverterModel : PageModel
{
    private readonly FilterUrlBuilder _filterUrlBuilder;

    [BindProperty]
    public string FeedUrl { get; set; } = "";

    public string? FilterUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public FeedConverterModel(FilterUrlBuilder filterUrlBuilder)
    {
        _filterUrlBuilder = filterUrlBuilder;
    }

    public IActionResult OnPost()
    {
        var feedUrl = FeedUrl.Trim();
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var parsedFeedUrl))
        {
            ErrorMessage = "Enter a valid feed URL";
            return Page();
        }

        if (parsedFeedUrl.Scheme != Uri.UriSchemeHttp && parsedFeedUrl.Scheme != Uri.UriSchemeHttps)
        {
            ErrorMessage = "Feed URL must use http or https";
            return Page();
        }

        FilterUrl = _filterUrlBuilder.Build(Request, feedUrl);

        return Page();
    }
}
