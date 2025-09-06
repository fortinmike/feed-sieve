var builder = WebApplication.CreateBuilder(args);

// Add an additional config layer with our secrets
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Register core services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddLogging();

// Register our own services (for the app's logic)
builder.Services.AddSingleton<Processor>();
builder.Services.AddSingleton(new Cache(new DirectoryInfo("cache")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation($"Application started!");
logger.LogInformation($"Default rules contain {Rules.Load("rules.default.yaml").Count} entries.");
logger.LogInformation(
    $"Secret-Based Authentication: {(builder.Configuration["Secret"] == "" ? "Disabled" : "Enabled")}"
);

app.Run();
