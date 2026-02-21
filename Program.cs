var builder = WebApplication.CreateBuilder(args);

// Add an additional config layer with our secrets
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Register core services
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddLogging();

// Register our own services (for the app's logic)
builder.Services.AddSingleton(new Cache(new DirectoryInfo("cache")));
builder.Services.AddScoped<Processor>();
builder.Services.AddScoped<IFilter, RegexFilter>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMiddleware<BasicAuthMiddleware>(
    new BasicAuthOptions
    {
        Username = "admin",
        Password = builder.Configuration["Secret"] ?? "",
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
