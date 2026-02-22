using System.ServiceModel.Syndication;
using System.Net;
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
        var correctSecret = _configuration["Secret"] ?? "";
        if (correctSecret != "" && secret != correctSecret)
            return Unauthorized();

        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}";
        var feedUrl = $"{requestBaseUrl}/failures";
        var failures = _failureStore
            .GetCurrentFailures()
            .OrderByDescending(failure => failure.LastFailureUtc)
            .ToList();

        var feed = new SyndicationFeed(
            "feed-sieve broken feeds",
            "Feeds currently failing in this feed-sieve instance",
            new Uri(feedUrl),
            "feed-sieve:failures",
            failures.FirstOrDefault()?.LastFailureUtc ?? DateTimeOffset.UtcNow
        );

        feed.Items = failures.Select(CreateItem).ToList();

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(
            stream,
            new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }
        ))
        {
            new Rss20FeedFormatter(feed).WriteTo(writer);
        }

        return File(stream.ToArray(), "application/rss+xml; charset=utf-8");
    }

    private static SyndicationItem CreateItem(Failure failure)
    {
        var title = failure.HttpStatusCode is { } statusCode
            ? $"HTTP {statusCode}: {failure.FeedUrl}"
            : $"{failure.FailureType}: {failure.FeedUrl}";

        var item = new SyndicationItem
        {
            Title = SyndicationContent.CreatePlaintextContent(title),
            Id = $"feed-sieve:failure:{failure.FeedUrl.Hash()}",
            PublishDate = failure.LastFailureUtc,
            LastUpdatedTime = failure.LastFailureUtc,
            Summary = SyndicationContent.CreateHtmlContent(CreateSummaryHtml(failure))
        };

        if (Uri.TryCreate(failure.FeedUrl, UriKind.Absolute, out var feedUrl))
            item.Links.Add(SyndicationLink.CreateAlternateLink(feedUrl));

        return item;
    }

    private static string CreateSummaryHtml(Failure failure)
    {
        var html = new StringBuilder();

        AppendParagraph(html, $"<strong>{Encode("Summary")}</strong><br>{Encode(failure.UserMessage)}");
        AppendParagraph(html, $"<strong>{Encode("Feed URL")}</strong><br>{CreateLinkOrCode(failure.FeedUrl)}");

        if (failure.HttpStatusCode is { } statusCode)
        {
            var reason = failure.HttpReasonPhrase is { Length: > 0 } reasonPhrase ? $" {reasonPhrase}" : "";
            AppendParagraph(html, $"<strong>{Encode("HTTP Status")}</strong><br>{(int)statusCode}{Encode(reason)}");
        }

        AppendParagraph(html, $"<strong>{Encode("Reason")}</strong><br>{Encode(failure.Message)}");
        AppendParagraph(html, $"<strong>{Encode("First failure")}</strong><br><code>{Encode(failure.FirstFailureUtc.ToString("O"))}</code>");
        AppendParagraph(html, $"<strong>{Encode("Last failure")}</strong><br><code>{Encode(failure.LastFailureUtc.ToString("O"))}</code>");
        AppendParagraph(html, $"<strong>{Encode("Failure count")}</strong><br>{failure.FailureCount}");

        if (!string.IsNullOrWhiteSpace(failure.FinalUrl))
            AppendParagraph(html, $"<strong>{Encode("Final URL")}</strong><br>{CreateLinkOrCode(failure.FinalUrl)}");

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
