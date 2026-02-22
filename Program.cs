using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

// Add an additional config layer with our secrets
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Register core services
// Fail fast before feed readers (for example NetNewsWire at ~15s with 1 connection per host) time out and stall their queue
builder.Services.AddHttpClient(
    "upstream-feed",
    client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("feed-sieve (RSS Reader)");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetNewsWire (RSS Reader; https://netnewswire.com/)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/feed+json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml", 0.8));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.7));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
    }
).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // We follow redirects manually in UpstreamFeedClient so HTTPS->HTTP feed redirects can still work
    AllowAutoRedirect = false
});
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = MessageOnlyConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<MessageOnlyConsoleFormatter, ConsoleFormatterOptions>();

// Register our own services (for the app's logic)
builder.Services.AddSingleton(new Cache(new DirectoryInfo("./storage/cache")));
builder.Services.AddSingleton(new FailureStore(new DirectoryInfo("./storage/failures")));
builder.Services.AddSingleton<UpstreamFeedClient>();
builder.Services.AddScoped<Processor>();
builder.Services.AddScoped<IFilter, RegexFilter>();
builder.Services.AddSingleton<FilterUrlBuilder>();

var app = builder.Build();
var secret = builder.Configuration["Secret"] ?? "";
if (string.IsNullOrWhiteSpace(secret))
    throw new InvalidOperationException("Secret must be set in configuration");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMiddleware<BasicAuthMiddleware>(
    new BasicAuthOptions
    {
        Username = "admin",
        Password = secret,
        ProtectedPathPrefixes = ["/admin"]
    }
);

app.MapControllers();
app.MapRazorPages();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation($"Application started!");
logger.LogInformation($"Default rules contain {Rules.Load("rules.default.yaml").Count} entries.");
logger.LogInformation("Secret-Based Authentication: Enabled");

app.Run();
