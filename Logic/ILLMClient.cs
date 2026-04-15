public interface ILLMClient
{
    bool IsConfigured { get; }

    Task<string?> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
