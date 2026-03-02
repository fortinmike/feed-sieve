using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

// Add an additional config layer with our secrets
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Register core services
builder
    .Services.AddHttpClient(
        "upstream-feed",
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20); // Times out after most feed readers, as a last resort
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Feed Sieve (RSS Reader)");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetNewsWire (RSS Reader; https://netnewswire.com/)");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/feed+json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml", 0.8));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.7));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        }
    )
    .ConfigurePrimaryHttpMessageHandler(
        () =>
            new HttpClientHandler
            {
                // We follow redirects manually in UpstreamFeedClient so HTTPS->HTTP feed redirects can still work
                AllowAutoRedirect = false
            }
    );
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = MessageOnlyConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<MessageOnlyConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var isCacheEnabled = builder.Configuration.GetValue<bool?>("Cache") ?? true;

// Register our own services (for the app's logic)
builder.Services.AddSingleton<ICache>(
    isCacheEnabled ? new Cache(new DirectoryInfo("./storage/cache")) : new NullCache()
);
builder.Services.AddSingleton(new FailureStore(new DirectoryInfo("./storage/failures")));
builder.Services.AddSingleton<UpstreamFeedClient>();
builder.Services.AddScoped<Processor>();
builder.Services.AddScoped<IFilter, RegexFilter>();
builder.Services.AddSingleton<FilterUrlBuilder>();

var isAdminAuthEnabled = builder.Configuration.GetValue<bool?>("Auth:Enabled");
if (isAdminAuthEnabled is null)
    throw new InvalidOperationException("Auth:Enabled must be set in app settings (currently null)");

var baseSettings = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

var defaultSecret = baseSettings["Secret"] ?? "";
var secret = builder.Configuration["Secret"];
if (string.IsNullOrWhiteSpace(secret) || secret == defaultSecret)
    throw new InvalidOperationException("Secret must be overridden in app settings");

var isAdminAuthEnabledValue = isAdminAuthEnabled.Value;

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

if (isAdminAuthEnabledValue)
{
    var defaultAuthUsername = baseSettings["Auth:Username"] ?? "";
    var authUsername = builder.Configuration["Auth:Username"];

    var defaultAuthPassword = baseSettings["Auth:Password"] ?? "";
    var authPassword = builder.Configuration["Auth:Password"];

    if (
        string.IsNullOrWhiteSpace(authUsername)
        || authUsername == defaultAuthUsername
        || string.IsNullOrWhiteSpace(authPassword)
        || authPassword == defaultAuthPassword
    )
    {
        throw new InvalidOperationException(
            "Auth:Username and Auth:Password must be overridden in app settings when auth is enabled"
        );
    }

    app.UseMiddleware<BasicAuthMiddleware>(
        new BasicAuthOptions
        {
            Username = authUsername,
            Password = authPassword,
            ProtectedPathPrefixes = ["/admin"]
        }
    );
}

app.MapControllers();
app.MapRazorPages();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation($"Application started!");
logger.LogInformation($"Default rules contain {Rules.Load("rules.default.yaml").Count} entries.");
logger.LogInformation("API Secret Authentication: Enabled");
logger.LogInformation("Admin Authentication: {State}", isAdminAuthEnabledValue ? "Enabled" : "Disabled");
logger.LogInformation("Cache: {State}", isCacheEnabled ? "Enabled" : "Disabled");

app.Run();
