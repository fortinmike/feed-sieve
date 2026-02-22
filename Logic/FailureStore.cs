using System.Text;
using System.Text.Json;
using System.Xml;

public sealed class FailureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DirectoryInfo _directory;

    public FailureStore(DirectoryInfo directory)
    {
        _directory = directory;

        if (!_directory.Exists)
            _directory.Create();
    }

    public void RecordFailure(UpstreamFailureInfo failureInfo)
    {
        var dir = GetFeedDirectory(failureInfo.FeedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteInfo(dir, failureInfo);
    }

    public void RecordFailure(UpstreamFailureInfo failureInfo, Exception exception)
    {
        var dir = GetFeedDirectory(failureInfo.FeedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteInfo(dir, failureInfo);
        File.WriteAllText(Path.Combine(dir, "exception.log"), exception.ToString());
    }

    public void RecordParseFailure(string feedUrl, string feedXml, XmlException exception)
    {
        var failureInfo = new UpstreamFailureInfo(
            FeedUrl: feedUrl,
            FailureType: "XmlParse",
            Message: $"Invalid XML at line {exception.LineNumber}, position {exception.LinePosition}"
        );

        var dir = GetFeedDirectory(feedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteInfo(dir, failureInfo);
        File.WriteAllText(Path.Combine(dir, "feed.xml"), feedXml);
        File.WriteAllText(Path.Combine(dir, "exception.log"), exception.ToString());
    }

    public void ClearFailure(string feedUrl)
    {
        var statePath = Path.Combine(GetFeedDirectory(feedUrl), "state.json");
        if (File.Exists(statePath))
            File.Delete(statePath);
    }

    public IReadOnlyList<Failure> GetCurrentFailures()
    {
        if (!_directory.Exists)
            return [];

        var failures = new List<Failure>();
        foreach (var dir in Directory.EnumerateDirectories(_directory.FullName))
        {
            var statePath = Path.Combine(dir, "state.json");
            if (!File.Exists(statePath))
                continue;

            try
            {
                var json = File.ReadAllText(statePath);
                var state = JsonSerializer.Deserialize<Failure>(json);
                if (state != null)
                    failures.Add(state);
            }
            catch
            {
                // Ignore malformed state files and continue emitting the feed
            }
        }

        return failures;
    }

    public string? TryGetFeedXmlPreview(Failure failure)
    {
        var feedXmlPath = Path.Combine(GetFeedDirectory(failure.FeedUrl), "feed.xml");
        if (!File.Exists(feedXmlPath))
            return null;

        return CreateFeedXmlPreview(File.ReadAllText(feedXmlPath));
    }

    private string GetFeedDirectory(string feedUrl)
    {
        return Path.Combine(_directory.FullName, feedUrl.ToSafeFileName());
    }

    private static void WriteState(string dir, UpstreamFailureInfo failureInfo)
    {
        var now = DateTimeOffset.UtcNow;
        var statePath = Path.Combine(dir, "state.json");
        var existingState = ReadState(statePath);
        var state = new Failure(
            FeedUrl: failureInfo.FeedUrl,
            SafeFeedId: failureInfo.FeedUrl.ToSafeFileName(),
            FailureType: failureInfo.FailureType,
            UserMessage: CreateUserMessage(failureInfo),
            Message: failureInfo.Message,
            FirstFailureUtc: existingState?.FirstFailureUtc ?? now,
            LastFailureUtc: now,
            FailureCount: (existingState?.FailureCount ?? 0) + 1,
            HttpStatusCode: failureInfo.HttpStatusCode is { } statusCode ? (int)statusCode : null,
            HttpReasonPhrase: failureInfo.HttpReasonPhrase,
            FinalUrl: failureInfo.FinalUrl,
            Redirects: failureInfo.Redirects
        );

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(statePath, json);
    }

    private static Failure? ReadState(string statePath)
    {
        if (!File.Exists(statePath))
            return null;

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<Failure>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateFeedXmlPreview(string feedXml)
    {
        using var reader = new StringReader(feedXml);
        var previewLines = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            previewLines.Add(line);
        }

        return string.Join(Environment.NewLine, previewLines);
    }

    private static void WriteInfo(string dir, UpstreamFailureInfo failureInfo)
    {
        var info = new StringBuilder();
        info.AppendLine($"Feed URL: {failureInfo.FeedUrl}");
        info.AppendLine($"Failure: {failureInfo.FailureType}");
        info.AppendLine($"Message: {failureInfo.Message}");

        if (failureInfo.FinalUrl != null)
            info.AppendLine($"Final URL: {failureInfo.FinalUrl}");

        if (failureInfo.HttpStatusCode is { } statusCode)
            info.AppendLine($"HTTP Status: {(int)statusCode} {failureInfo.HttpReasonPhrase ?? statusCode.ToString()}");

        if (failureInfo.Redirects is { Count: > 0 })
        {
            info.AppendLine();
            info.AppendLine("Redirects:");
            foreach (var redirect in failureInfo.Redirects)
                info.AppendLine(redirect);
        }

        if (failureInfo.ResponseHeaders is { Count: > 0 })
        {
            info.AppendLine();
            info.AppendLine("Response headers:");
            foreach (var header in failureInfo.ResponseHeaders)
                info.AppendLine(header);
        }

        if (!string.IsNullOrEmpty(failureInfo.ResponseBodyPreview))
        {
            info.AppendLine();
            info.AppendLine("Response body preview:");
            info.AppendLine(failureInfo.ResponseBodyPreview);
        }

        File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString());
    }

    private static string CreateUserMessage(UpstreamFailureInfo failureInfo)
    {
        if (failureInfo.FailureType == "XmlParse")
            return "The upstream feed returned invalid XML";

        if (failureInfo.HttpStatusCode is { } statusCode)
        {
            return statusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "The upstream server denied access to the feed",
                System.Net.HttpStatusCode.NotFound => "The upstream feed URL was not found",
                System.Net.HttpStatusCode.Unauthorized => "The upstream feed requires authentication",
                _ => $"The upstream server returned HTTP {(int)statusCode}"
            };
        }

        if (
            failureInfo.FailureType.Contains(nameof(OperationCanceledException), StringComparison.Ordinal)
            || failureInfo.FailureType.Contains(nameof(TaskCanceledException), StringComparison.Ordinal)
        )
            return "The upstream feed request timed out";

        return "The upstream feed could not be fetched";
    }
}
