using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class RulesEditorModel : PageModel
{
    private static readonly HashSet<string> AllowedMatchKinds = ["title", "content", "all"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RulesEditorModel> _logger;

    [BindProperty]
    public string RulesJson { get; set; } = "[]";

    public string InitialRulesJson { get; private set; } = "[]";

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public RulesEditorModel(IWebHostEnvironment environment, ILogger<RulesEditorModel> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public void OnGet()
    {
        var rules = LoadRules();
        InitialRulesJson = SerializeEditorRules(rules.Select(MapToEditorRule).ToList());
    }

    public IActionResult OnPost()
    {
        List<EditorRule>? submittedRules;
        try
        {
            submittedRules = JsonSerializer.Deserialize<List<EditorRule>>(RulesJson, JsonOptions);
        }
        catch (JsonException)
        {
            ErrorMessage = "Rules payload is invalid";
            var existingRules = LoadRules();
            InitialRulesJson = SerializeEditorRules(existingRules.Select(MapToEditorRule).ToList());
            return Page();
        }

        if (submittedRules is null)
        {
            ErrorMessage = "Rules payload is invalid";
            var existingRules = LoadRules();
            InitialRulesJson = SerializeEditorRules(existingRules.Select(MapToEditorRule).ToList());
            return Page();
        }

        var validationError = TryBuildRules(submittedRules, out var rulesToSave);
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            InitialRulesJson = SerializeEditorRules(submittedRules);
            return Page();
        }

        try
        {
            Rules.Save(GetRulesPath(), rulesToSave);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save filtering rules");
            ErrorMessage = "Could not save rules";
            InitialRulesJson = SerializeEditorRules(submittedRules);
            return Page();
        }

        SuccessMessage = $"Saved {rulesToSave.Count} rule(s)";
        InitialRulesJson = SerializeEditorRules(rulesToSave.Select(MapToEditorRule).ToList());
        return Page();
    }

    private List<Rule> LoadRules()
    {
        try
        {
            return Rules.Load(GetRulesPath());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load filtering rules");
            ErrorMessage = "Could not load rules";
            return [];
        }
    }

    private string? TryBuildRules(List<EditorRule> submittedRules, out List<Rule> builtRules)
    {
        builtRules = [];

        for (var index = 0; index < submittedRules.Count; index++)
        {
            var submittedRule = submittedRules[index];
            var name = submittedRule.Name.Trim();
            var host = submittedRule.Host.Trim();
            var match = submittedRule.Match.Trim().ToLowerInvariant();
            var regex = submittedRule.Regex.Trim();

            var hasNoValues =
                string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(regex);
            if (hasNoValues)
                continue;

            if (string.IsNullOrWhiteSpace(name))
                return $"Rule {index + 1} is missing a rule name";

            if (string.IsNullOrWhiteSpace(regex))
                return $"Rule {index + 1} is missing a regex";

            if (!AllowedMatchKinds.Contains(match))
                return $"Rule {index + 1} has an invalid match target";

            builtRules.Add(new Rule
            {
                Name = name,
                Host = string.IsNullOrWhiteSpace(host) ? null : host,
                Match = match,
                Regex = regex
            });
        }

        return null;
    }

    private string GetRulesPath()
    {
        return Path.Combine(_environment.ContentRootPath, "rules.default.yaml");
    }

    private static EditorRule MapToEditorRule(Rule rule)
    {
        return new EditorRule
        {
            Name = rule.Name,
            Host = rule.Host ?? "",
            Match = rule.Match,
            Regex = rule.Regex
        };
    }

    private static string SerializeEditorRules(List<EditorRule> rules)
    {
        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    public class EditorRule
    {
        public string Name { get; set; } = "";

        public string Host { get; set; } = "";

        public string Match { get; set; } = "title";

        public string Regex { get; set; } = "";
    }
}
