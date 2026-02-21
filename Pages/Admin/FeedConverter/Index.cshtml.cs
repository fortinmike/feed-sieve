using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class FeedConverterModel : PageModel
{
    private readonly IConfiguration _configuration;

    [BindProperty]
    public string FeedUrl { get; set; } = "";

    public string? FilterUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public FeedConverterModel(IConfiguration configuration)
    {
        _configuration = configuration;
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

        var secret = _configuration["Secret"] ?? "";
        var endpoint = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/filter";
        var encodedFeedUrl = Uri.EscapeDataString(feedUrl);
        var encodedSecret = Uri.EscapeDataString(secret);
        FilterUrl = $"{endpoint}?url={encodedFeedUrl}&secret={encodedSecret}";

        return Page();
    }
}
