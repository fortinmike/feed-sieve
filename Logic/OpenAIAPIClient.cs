using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public class OpenAIAPIClient : ILLMClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAIAPIOptions _openAIAPIOptions;
    private readonly ILogger<OpenAIAPIClient> _logger;

    public OpenAIAPIClient(
        IHttpClientFactory httpClientFactory,
        OpenAIAPIOptions openAIAPIOptions,
        ILogger<OpenAIAPIClient> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _openAIAPIOptions = openAIAPIOptions;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_openAIAPIOptions.BaseUrl)
        && !string.IsNullOrWhiteSpace(_openAIAPIOptions.ApiKey);

    public async Task<string?> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Skipping LLM request because OpenAIAPI is not configured");
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient("openai-api");
        using var request = new HttpRequestMessage(HttpMethod.Post, GetChatCompletionsUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAIAPIOptions.ApiKey);
        request.Content = JsonContent.Create(CreateRequestBody(systemPrompt, userPrompt), options: SerializerOptions);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenAIAPI request failed with HTTP {StatusCode}: {ResponseBody}",
                    (int)response.StatusCode,
                    responseBody
                );
                return null;
            }

            return ExtractResponseText(responseBody);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "OpenAIAPI request timed out");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAIAPI request failed");
            return null;
        }
    }

    private Uri GetChatCompletionsUri()
    {
        var baseUrl = _openAIAPIOptions.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        return new Uri(new Uri(baseUrl, UriKind.Absolute), "chat/completions");
    }

    private object CreateRequestBody(string systemPrompt, string userPrompt)
    {
        return new
        {
            model = string.IsNullOrWhiteSpace(_openAIAPIOptions.Model) ? null : _openAIAPIOptions.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userPrompt
                }
            }
        };
    }

    private string? ExtractResponseText(string responseBody)
    {
        using var jsonDocument = JsonDocument.Parse(responseBody);
        if (!jsonDocument.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message))
            return null;

        if (!message.TryGetProperty("content", out var content))
            return null;

        return ReadContent(content);
    }

    private string? ReadContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return NormalizeContent(content.GetString());

        if (content.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (
                part.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "text"
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
            )
            {
                parts.Add(text.GetString() ?? "");
            }
        }

        return NormalizeContent(string.Concat(parts));
    }

    private string? NormalizeContent(string? content)
    {
        var trimmed = content?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
