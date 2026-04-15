public interface ILLMClient
{
    bool IsConfigured { get; }

    Task<string?> SummarizeAsync(string title, string contentText, string prompt, CancellationToken cancellationToken);
}
