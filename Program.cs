var builder = WebApplication.CreateBuilder(args);

// Register core services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddLogging();

// Register our own services (for the app's logic)
builder.Services.AddSingleton<Filter>();

// Customize logging
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

// Log startup and number of rules in default ruleset
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var rules = Rules.Load("ruleset.default.yaml");
logger.LogInformation($"✅ Application started!");
logger.LogInformation($"ℹ️  Default ruleset contains {rules.Count} rules.");

app.Run();
