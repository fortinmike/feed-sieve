public class ArticleSummarizer
{
    private readonly ILLMClient _llmClient;
    private readonly SummarizationOptions _summarizationOptions;

    public ArticleSummarizer(ILLMClient llmClient, SummarizationOptions summarizationOptions)
    {
        _llmClient = llmClient;
        _summarizationOptions = summarizationOptions;
    }

    public bool IsConfigured => _llmClient.IsConfigured;

    public int MinimumContentLength => _summarizationOptions.MinimumContentLength;

    public string DefaultPrompt => _summarizationOptions.DefaultSummaryPrompt;

    public Task<string?> SummarizeAsync(
        string title,
        string contentText,
        string prompt,
        CancellationToken cancellationToken
    )
    {
        return _llmClient.SummarizeAsync(title, contentText, prompt, cancellationToken);
    }
}
