using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class RulesEditorModel : PageModel
{
    private static readonly HashSet<string> AllowedMatchKinds = ["title", "content", "all"];
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RulesEditorModel> _logger;
    private readonly SummarizationOptions _summarizationOptions;

    [BindProperty]
    public string RulesJson { get; set; } = "{}";

    public string InitialRulesJson { get; private set; } = "{}";

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public string SummaryPromptPlaceholder => _summarizationOptions.DefaultSummaryPrompt;

    public RulesEditorModel(
        IWebHostEnvironment environment,
        ILogger<RulesEditorModel> logger,
        SummarizationOptions summarizationOptions
    )
    {
        _environment = environment;
        _logger = logger;
        _summarizationOptions = summarizationOptions;
    }

    public void OnGet()
    {
        RefreshInitialRulesFromDisk();
    }

    public IActionResult OnPost()
    {
        EditorRulesConfig? submittedRules;
        try
        {
            submittedRules = JsonSerializer.Deserialize<EditorRulesConfig>(RulesJson, JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidPayload();
        }

        if (submittedRules is null)
            return InvalidPayload();

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
            _logger.LogError(ex, "Could not save rules");
            ErrorMessage = "Could not save rules";
            InitialRulesJson = SerializeEditorRules(submittedRules);
            return Page();
        }

        SuccessMessage =
            $"Saved {rulesToSave.GlobalFilters.Count} global filter(s) and {rulesToSave.Feeds.Count} feed rule(s)";
        InitialRulesJson = SerializeRulesForEditor(rulesToSave);
        return Page();
    }

    private IActionResult InvalidPayload()
    {
        ErrorMessage = "Rules payload is invalid";
        RefreshInitialRulesFromDisk();
        return Page();
    }

    private void RefreshInitialRulesFromDisk()
    {
        InitialRulesJson = SerializeRulesForEditor(LoadRules());
    }

    private RulesConfig LoadRules()
    {
        try
        {
            return Rules.Load(GetRulesPath());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load rules");
            ErrorMessage = "Could not load rules";
            return new RulesConfig();
        }
    }

    private string? TryBuildRules(EditorRulesConfig submittedRules, out RulesConfig builtRules)
    {
        builtRules = new RulesConfig();
        var seenFeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < submittedRules.GlobalFilters.Count; index++)
        {
            var builtFilter = TryBuildFilterRule(
                submittedRules.GlobalFilters[index],
                $"Global filter {index + 1}",
                allowEmpty: true,
                out var validationError
            );
            if (validationError is not null)
                return validationError;

            if (builtFilter is not null)
                builtRules.GlobalFilters.Add(builtFilter);
        }

        for (var index = 0; index < submittedRules.Feeds.Count; index++)
        {
            var submittedFeed = submittedRules.Feeds[index];
            var name = submittedFeed.Name.Trim();
            var feed = submittedFeed.Feed.Trim();
            var summaryPrompt = submittedFeed.SummaryEnabled ? submittedFeed.SummaryPrompt.Trim() : "";

            var builtFilters = new List<FilterRule>();
            for (var filterIndex = 0; filterIndex < submittedFeed.Filters.Count; filterIndex++)
            {
                var builtFilter = TryBuildFilterRule(
                    submittedFeed.Filters[filterIndex],
                    $"Feed rule {index + 1}, filter {filterIndex + 1}",
                    allowEmpty: true,
                    out var validationError
                );
                if (validationError is not null)
                    return validationError;

                if (builtFilter is not null)
                    builtFilters.Add(builtFilter);
            }

            var hasAnyValues =
                !string.IsNullOrWhiteSpace(name)
                || !string.IsNullOrWhiteSpace(feed)
                || submittedFeed.SummaryEnabled
                || !string.IsNullOrWhiteSpace(summaryPrompt)
                || builtFilters.Count > 0;
            if (!hasAnyValues)
                continue;

            if (string.IsNullOrWhiteSpace(name))
                return $"Feed rule {index + 1} is missing a name";

            if (string.IsNullOrWhiteSpace(feed))
                return $"Feed rule {index + 1} is missing a feed";

            if (!seenFeeds.Add(feed))
                return $"Feed rule {index + 1} duplicates feed '{feed}'";

            if (builtFilters.Count == 0 && !submittedFeed.SummaryEnabled)
                return $"Feed rule {index + 1} must have at least one filter or summary enabled";

            builtRules.Feeds.Add(
                new FeedRule
                {
                    Name = name,
                    Feed = feed,
                    Filters = builtFilters,
                    Summary = submittedFeed.SummaryEnabled
                        ? new SummaryRule { Prompt = string.IsNullOrWhiteSpace(summaryPrompt) ? null : summaryPrompt }
                        : null
                }
            );
        }

        return null;
    }

    private FilterRule? TryBuildFilterRule(
        EditorFilterRule submittedRule,
        string context,
        bool allowEmpty,
        out string? validationError
    )
    {
        validationError = null;

        var name = submittedRule.Name.Trim();
        var match = submittedRule.Match.Trim().ToLowerInvariant();
        var regex = submittedRule.Regex.Trim();
        var caseSensitive = submittedRule.CaseSensitive;
        var sample = submittedRule.Sample.Trim();

        var hasNoValues = string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(regex);
        if (allowEmpty && hasNoValues)
            return null;

        if (string.IsNullOrWhiteSpace(name))
        {
            validationError = $"{context} is missing a rule name";
            return null;
        }

        if (string.IsNullOrWhiteSpace(regex))
        {
            validationError = $"{context} is missing a regex";
            return null;
        }

        if (!AllowedMatchKinds.Contains(match))
        {
            validationError = $"{context} has an invalid match target";
            return null;
        }

        return new FilterRule
        {
            Name = name,
            Match = match,
            Regex = regex,
            CaseSensitive = caseSensitive,
            Sample = string.IsNullOrWhiteSpace(sample) ? null : sample
        };
    }

    private string GetRulesPath()
    {
        return Path.Combine(_environment.ContentRootPath, "rules.default.yaml");
    }

    private static EditorRulesConfig MapToEditorRules(RulesConfig rules)
    {
        return new EditorRulesConfig
        {
            GlobalFilters = rules.GlobalFilters.Select(MapToEditorFilterRule).ToList(),
            Feeds = rules.Feeds.Select(MapToEditorFeedRule).ToList()
        };
    }

    private static EditorFeedRule MapToEditorFeedRule(FeedRule rule)
    {
        return new EditorFeedRule
        {
            Name = rule.Name,
            Feed = rule.Feed,
            SummaryEnabled = rule.Summary is not null,
            SummaryPrompt = rule.Summary?.Prompt ?? "",
            Filters = rule.Filters.Select(MapToEditorFilterRule).ToList()
        };
    }

    private static EditorFilterRule MapToEditorFilterRule(FilterRule rule)
    {
        return new EditorFilterRule
        {
            Name = rule.Name,
            Match = rule.Match,
            Regex = rule.Regex,
            CaseSensitive = rule.CaseSensitive,
            Sample = rule.Sample ?? ""
        };
    }

    private static string SerializeRulesForEditor(RulesConfig rules)
    {
        return SerializeEditorRules(MapToEditorRules(rules));
    }

    private static string SerializeEditorRules(EditorRulesConfig rules)
    {
        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    public class EditorRulesConfig
    {
        public List<EditorFilterRule> GlobalFilters { get; set; } = [];

        public List<EditorFeedRule> Feeds { get; set; } = [];
    }

    public class EditorFeedRule
    {
        public string Name { get; set; } = "";

        public string Feed { get; set; } = "";

        public bool SummaryEnabled { get; set; }

        public string SummaryPrompt { get; set; } = "";

        public List<EditorFilterRule> Filters { get; set; } = [];
    }

    public class EditorFilterRule
    {
        public string Name { get; set; } = "";

        public string Match { get; set; } = "title";

        public string Regex { get; set; } = "";

        public bool CaseSensitive { get; set; }

        public string Sample { get; set; } = "";
    }
}
