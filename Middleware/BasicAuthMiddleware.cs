using System.Text;

public class BasicAuthMiddleware
{
    private const string ChallengeRealm = "feed-sieve-admin";
    private readonly RequestDelegate _next;
    private readonly ILogger<BasicAuthMiddleware> _logger;
    private readonly BasicAuthOptions _options;
    private readonly IReadOnlyList<PathString> _protectedPathPrefixes;

    public BasicAuthMiddleware(RequestDelegate next, ILogger<BasicAuthMiddleware> logger, BasicAuthOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
        _protectedPathPrefixes = options
            .ProtectedPathPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.StartsWith('/') ? prefix : $"/{prefix}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(prefix => new PathString(prefix))
            .ToList();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldProtectPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (IsAuthorized(context.Request.Headers.Authorization.ToString()))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{ChallengeRealm}\", charset=\"UTF-8\"";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private bool ShouldProtectPath(PathString requestPath)
    {
        foreach (var prefix in _protectedPathPrefixes)
            if (requestPath.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private bool IsAuthorized(string header)
    {
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
            return username == _options.Username && password == _options.Password;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid Basic auth credentials format");
            return false;
        }
    }
}
