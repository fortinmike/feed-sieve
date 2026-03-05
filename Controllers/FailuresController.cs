using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class FailuresController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly FailureStore _failureStore;

    public FailuresController(IConfiguration configuration, FailureStore failureStore)
    {
        _configuration = configuration;
        _failureStore = failureStore;
    }

    [HttpGet("failures")]
    public IActionResult Failures([FromQuery] string? secret)
    {
        var correctSecret = _configuration.GetRequiredSection("Secret").Value!;
        if (secret != correctSecret)
            return Unauthorized();

        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}";
        var feedUrl = $"{requestBaseUrl}/failures";
        var failures = _failureStore.GetCurrentFailures().OrderByDescending(failure => failure.LastErrorUtc).ToList();

        var feed = new SyndicationFeed(
            "Feed Sieve Failures",
            "Feeds currently failing in this Feed Sieve instance",
            new Uri(feedUrl),
            "feed-sieve:failures",
            failures.FirstOrDefault()?.LastErrorUtc ?? DateTimeOffset.UtcNow
        );

        feed.Items = failures.Select(CreateItem).ToList();

        using var stream = new MemoryStream();
        using (
            var writer = XmlWriter.Create(
                stream,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }
            )
        )
        {
            new Rss20FeedFormatter(feed).WriteTo(writer);
        }

        return File(stream.ToArray(), "application/rss+xml; charset=utf-8");
    }

    private SyndicationItem CreateItem(Failure failure)
    {
        var title = failure.HttpStatus is { } statusCode
            ? $"HTTP {statusCode}: {failure.FeedUrl}"
            : $"{failure.Type}: {failure.FeedUrl}";

        var item = new SyndicationItem
        {
            Title = SyndicationContent.CreatePlaintextContent(title),
            Id = failure.Id,
            PublishDate = failure.FirstErrorUtc,
            LastUpdatedTime = failure.FirstErrorUtc,
            Summary = SyndicationContent.CreateHtmlContent(CreateSummaryHtml(failure))
        };

        if (Uri.TryCreate(failure.FeedUrl, UriKind.Absolute, out var feedUrl))
            item.Links.Add(SyndicationLink.CreateAlternateLink(feedUrl));

        return item;
    }

    private string CreateSummaryHtml(Failure failure)
    {
        var html = new StringBuilder();

        AppendParagraph(html, CreateLinkOrCode(failure.FeedUrl));
        if (
            !string.IsNullOrWhiteSpace(failure.FinalUrl)
            && !string.Equals(failure.FinalUrl, failure.FeedUrl, StringComparison.OrdinalIgnoreCase)
        )
        {
            AppendParagraph(html, CreateLinkOrCode("-> " + failure.FinalUrl));
        }

        AppendParagraph(html, $"<strong>{Encode("Message")}</strong><br>{Encode(failure.Message)}");

        AppendParagraph(html, $"<strong>{Encode("Details")}</strong><br>{Encode(failure.Details)}");
        AppendParagraph(
            html,
            $"<strong>{Encode("First Error")}</strong><br><code>{Encode(failure.FirstErrorUtc.ToString("O"))}</code>"
        );
        AppendParagraph(
            html,
            $"<strong>{Encode("Last Error")}</strong><br><code>{Encode(failure.LastErrorUtc.ToString("O"))}</code>"
        );
        AppendParagraph(html, $"<strong>{Encode("Consecutive Errors")}</strong><br>{failure.ConsecutiveErrors}");
        AppendParagraph(html, $"<strong>{Encode("Total Errors")}</strong><br>{failure.TotalErrors}");

        if (failure.Redirects is { Count: > 0 })
        {
            html.Append("<p><strong>Redirects</strong></p>");
            html.Append("<ul>");
            foreach (var redirect in failure.Redirects)
            {
                html.Append("<li>");
                html.Append(WebUtility.HtmlEncode(redirect));
                html.Append("</li>");
            }
            html.Append("</ul>");
        }

        if (_failureStore.TryGetResponsePreview(failure) is string responsePreview)
        {
            html.Append("<p><strong>Response Preview (first 10 lines)</strong></p>");
            html.Append("<pre><code>");
            html.Append(Encode(responsePreview));
            html.Append("</code></pre>");
        }

        return html.ToString();
    }

    private static void AppendParagraph(StringBuilder html, string innerHtml)
    {
        html.Append("<p>");
        html.Append(innerHtml);
        html.Append("</p>");
    }

    private static string CreateLinkOrCode(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"<code>{Encode(url)}</code>";

        var encodedUrl = Encode(uri.ToString());
        return $"<a href=\"{encodedUrl}\">{encodedUrl}</a>";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
