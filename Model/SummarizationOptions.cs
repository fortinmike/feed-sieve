public class SummarizationOptions
{
    public string DefaultSummaryPrompt { get; set; } = "Summarize this text in one or two short paragraphs.";

    public int MinimumContentLength { get; set; } = 1500;
}
