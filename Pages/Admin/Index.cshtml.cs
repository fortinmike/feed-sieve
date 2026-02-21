using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace feed_sieve.Pages.Admin;

public class IndexModel : PageModel
{
    private const string AdminUsername = "admin";
    private readonly IConfiguration _configuration;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IConfiguration configuration, ILogger<IndexModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        if (!IsAuthorized())
        {
            Response.Headers.WWWAuthenticate = "Basic realm=\"feed-sieve-admin\", charset=\"UTF-8\"";
            return Unauthorized();
        }

        return Page();
    }

    private bool IsAuthorized()
    {
        var header = Request.Headers["Authorization"].ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        var encodedCredentials = header["Basic ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(encodedCredentials))
            return false;

        try
        {
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var separator = decodedCredentials.IndexOf(':');
            if (separator <= 0)
                return false;

            var username = decodedCredentials[..separator];
            var password = decodedCredentials[(separator + 1)..];
            var expectedPassword = _configuration["Secret"] ?? "";
            return username == AdminUsername && password == expectedPassword;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid Basic auth credentials format");
            return false;
        }
    }
}
