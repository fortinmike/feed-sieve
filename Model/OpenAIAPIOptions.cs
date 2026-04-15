public class OpenAIAPIOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public string ApiKey { get; set; } = "";

    public string? Model { get; set; }
}
