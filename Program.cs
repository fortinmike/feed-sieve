using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

// Override with env vars if provided
Dictionary<string, string?> BuildEnvConfigOverrides()
{
    var overrides = new Dictionary<string, string?>();

    void AddOverride(string configKey, string envVarName)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(value))
            overrides[configKey] = value;
    }

    AddOverride("Auth:Username", "AUTH_USERNAME");
    AddOverride("Auth:Password", "AUTH_PASSWORD");
    AddOverride("Secret", "SECRET");
    AddOverride("OpenAIAPI:BaseUrl", "OPENAI_API_BASE_URL");
    AddOverride("OpenAIAPI:ApiKey", "OPENAI_API_KEY");
    AddOverride("OpenAIAPI:Model", "OPENAI_API_MODEL");
    AddOverride("Summarization:DefaultSummaryPrompt", "SUMMARIZATION_DEFAULT_SUMMARY_PROMPT");
    AddOverride("Summarization:MinimumContentLength", "SUMMARIZATION_MINIMUM_CONTENT_LENGTH");

    return overrides;
}
var envConfigOverrides = BuildEnvConfigOverrides();
builder.Configuration.AddInMemoryCollection(envConfigOverrides);

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
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html", 0.6));
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
builder
    .Services.AddHttpClient(
        "html-metadata",
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Feed Sieve (RSS Reader)");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetNewsWire (RSS Reader; https://netnewswire.com/)");
        }
    )
    .ConfigurePrimaryHttpMessageHandler(
        () =>
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            }
    );
builder.Services.AddHttpClient("openai-api", client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = MessageOnlyConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<MessageOnlyConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var isCacheEnabled = builder.Configuration.GetRequiredSection("Cache").Get<bool>();
var consecutiveErrorsToPublishFailure = builder
    .Configuration.GetRequiredSection("ConsecutiveErrorsToPublishFailure")
    .Get<int>();

// Register our own services (for the app's logic)
builder.Services.AddSingleton<ICache>(
    isCacheEnabled ? new Cache(new DirectoryInfo("./storage/cache")) : new NullCache()
);
builder.Services.AddSingleton(new SummaryCache(new DirectoryInfo("./storage/summaries")));
builder.Services.AddSingleton(
    new FailureStore(new DirectoryInfo("./storage/failures"), consecutiveErrorsToPublishFailure)
);
builder.Services.AddSingleton(builder.Configuration.GetSection("OpenAIAPI").Get<OpenAIAPIOptions>() ?? new OpenAIAPIOptions());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Summarization").Get<SummarizationOptions>() ?? new SummarizationOptions()
);
builder.Services.AddSingleton<UpstreamFeedClient>();
builder.Services.AddSingleton<FeedDiscoveryService>();
builder.Services.AddSingleton<ILLMClient, OpenAIAPIClient>();
builder.Services.AddSingleton<ArticleSummarizer>();
builder.Services.AddScoped<Processor>();
builder.Services.AddScoped<IFilter, RegexFilter>();
builder.Services.AddSingleton<FilterUrlBuilder>();

var authSection = builder.Configuration.GetRequiredSection("Auth");
var isAdminAuthEnabledValue = authSection.GetRequiredSection("Enabled").Get<bool>();

var baseSettings = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

var defaultSecret = baseSettings.GetRequiredSection("Secret").Value!;
var secret = builder.Configuration.GetRequiredSection("Secret").Value;
if (secret == defaultSecret)
    throw new InvalidOperationException("Secret must be set via FEED_SIEVE_SECRET or overridden in appsettings.");

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

if (isAdminAuthEnabledValue)
{
    var defaultAuthSection = baseSettings.GetRequiredSection("Auth");

    var defaultAuthUsername = defaultAuthSection.GetRequiredSection("Username").Value!;
    var authUsername = authSection.GetSection("Username").Value;

    var defaultAuthPassword = defaultAuthSection.GetRequiredSection("Password").Value!;
    var authPassword = authSection.GetSection("Password").Value;

    if (
        string.IsNullOrWhiteSpace(authUsername)
        || authUsername == defaultAuthUsername
        || string.IsNullOrWhiteSpace(authPassword)
        || authPassword == defaultAuthPassword
    )
    {
        throw new InvalidOperationException(
            "Username and password must be set via FEED_SIEVE_AUTH_USERNAME/FEED_SIEVE_AUTH_PASSWORD or overridden in appsettings when auth is enabled."
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
var defaultRules = Rules.Load("rules.default.yaml");
logger.LogInformation($"Application started!");
logger.LogInformation(
    "Default rules contain {GlobalFilterCount} global filters and {FeedCount} feed configs",
    defaultRules.GlobalFilters.Count,
    defaultRules.Feeds.Count
);
logger.LogInformation("API Secret Authentication: Enabled");
logger.LogInformation("Admin Authentication: {State}", isAdminAuthEnabledValue ? "Enabled" : "Disabled");
logger.LogInformation("Cache: {State}", isCacheEnabled ? "Enabled" : "Disabled");

app.Run();
