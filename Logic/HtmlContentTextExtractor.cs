using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

public static partial class HtmlContentTextExtractor
{
    public static string Extract(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var parser = new HtmlParser();
        var document = parser.ParseDocument(content);
        var text = document.Body?.TextContent ?? document.DocumentElement?.TextContent ?? "";
        return NormalizeWhitespace(text);
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
