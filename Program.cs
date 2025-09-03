var builder = WebApplication.CreateBuilder(args);

// Register core services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddLogging();

// Register our own services (for the app's logic)
builder.Services.AddSingleton<Filter>();
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

// Log startup and number of rules in default rule set
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var rules = Rules.Load("rules.default.yaml");
logger.LogInformation($"Application started!");
logger.LogInformation($"Default rules contain {rules.Count} entries.");

app.Run();
