public class ArticleSummarizer
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<ArticleSummarizer> _logger;
    private readonly SummarizationOptions _summarizationOptions;

    public ArticleSummarizer(
        ILLMClient llmClient,
        ILogger<ArticleSummarizer> logger,
        SummarizationOptions summarizationOptions
    )
    {
        _llmClient = llmClient;
        _logger = logger;
        _summarizationOptions = summarizationOptions;
    }

    public bool IsConfigured => _llmClient.IsConfigured;

    public int MinimumContentLength => _summarizationOptions.MinimumContentLength;

    public string DefaultPrompt => _summarizationOptions.DefaultSummaryPrompt;

    public async Task<string?> SummarizeAsync(
        string title,
        string contentText,
        string prompt,
        CancellationToken cancellationToken
    )
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Skipping article summary because no LLM client is configured");
            return null;
        }

        var summary = await _llmClient.GenerateAsync(
            GetSystemPrompt(),
            CreateUserPrompt(title, contentText, prompt),
            cancellationToken
        );

        if (summary is null)
            _logger.LogWarning("Summarization returned no result for '{Title}'", title);

        return summary;
    }

    private string GetSystemPrompt()
    {
        return
            "You summarize feed articles for display at the top of the original article body. Return only an HTML fragment. Do not use markdown. Do not repeat the title as a heading.";
    }

    private string CreateUserPrompt(string title, string contentText, string prompt)
    {
        return
            $"""
            Summary instructions:
            {prompt}

            Article title:
            {title}

            Article content:
            {contentText}
            """;
    }
}
