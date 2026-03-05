using System.Text.Json;
using System.Xml;

public sealed class FailureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DirectoryInfo _directory;
    private readonly int _consecutiveErrorsForFailureFeed;

    public FailureStore(DirectoryInfo directory, int consecutiveErrorsForFailureFeed)
    {
        _directory = directory;
        _consecutiveErrorsForFailureFeed = consecutiveErrorsForFailureFeed;

        if (!_directory.Exists)
            _directory.Create();
    }

    public void RecordFailure(UpstreamFailureInfo failureInfo)
    {
        var dir = GetFeedDirectory(failureInfo.FeedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteResponseArtifacts(dir, failureInfo);
    }

    public void RecordFailure(UpstreamFailureInfo failureInfo, Exception exception)
    {
        var dir = GetFeedDirectory(failureInfo.FeedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteResponseArtifacts(dir, failureInfo);
        File.WriteAllText(Path.Combine(dir, "exception.log"), exception.ToString());
    }

    public void RecordParseFailure(string feedUrl, string feedXml, XmlException exception)
    {
        var failureInfo = new UpstreamFailureInfo(
            FeedUrl: feedUrl,
            FailureType: "XmlParse",
            Message: $"Invalid XML at line {exception.LineNumber}, position {exception.LinePosition}",
            ResponseHeaders: [],
            ResponseBody: feedXml
        );

        var dir = GetFeedDirectory(feedUrl);
        Directory.CreateDirectory(dir);
        WriteState(dir, failureInfo);
        WriteResponseArtifacts(dir, failureInfo);
        File.WriteAllText(Path.Combine(dir, "exception.log"), exception.ToString());
    }

    public void RecordSuccess(string feedUrl)
    {
        var statePath = GetStatePath(feedUrl);
        var existingState = ReadState(statePath);
        if (existingState == null || existingState.ConsecutiveErrors == 0)
            return;

        var state = existingState with { ConsecutiveErrors = 0 };
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(statePath, json);
    }

    public Failure? GetFailure(string feedUrl)
    {
        var statePath = GetStatePath(feedUrl);
        return ReadState(statePath);
    }

    public void ClearDoNotUpdateBeforeUtc(string feedUrl)
    {
        var statePath = GetStatePath(feedUrl);
        var existingState = ReadState(statePath);
        if (existingState?.DoNotUpdateBeforeUtc == null)
            return;

        var updatedState = existingState with { DoNotUpdateBeforeUtc = null };
        var json = JsonSerializer.Serialize(updatedState, JsonOptions);
        File.WriteAllText(statePath, json);
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
                if (state != null && state.ConsecutiveErrors > _consecutiveErrorsForFailureFeed)
                    failures.Add(state);
            }
            catch
            {
                // Ignore malformed state files and continue emitting the feed
            }
        }

        return failures;
    }

    public string? TryGetResponsePreview(Failure failure)
    {
        var responsePath = Path.Combine(GetFeedDirectory(failure.FeedUrl), "response.xml");
        if (!File.Exists(responsePath))
            return null;

        return CreateResponsePreview(File.ReadAllText(responsePath));
    }

    private string GetFeedDirectory(string feedUrl)
    {
        return Path.Combine(_directory.FullName, feedUrl.ToSafeFileName());
    }

    private void WriteState(string dir, UpstreamFailureInfo failureInfo)
    {
        var now = DateTimeOffset.UtcNow;
        var statePath = Path.Combine(dir, "state.json");
        var existingState = ReadState(statePath);
        var hasConsecutiveErrors = existingState is { ConsecutiveErrors: > 0 };
        var id = hasConsecutiveErrors ? existingState!.Id : Guid.NewGuid().ToString("N");
        var state = new Failure(
            FeedUrl: failureInfo.FeedUrl,
            Type: failureInfo.FailureType,
            Details: failureInfo.Message,
            FirstErrorUtc: existingState?.FirstErrorUtc ?? now,
            LastErrorUtc: now,
            TotalErrors: (existingState?.TotalErrors ?? 0) + 1,
            ConsecutiveErrors: (existingState?.ConsecutiveErrors ?? 0) + 1,
            HttpStatus: failureInfo.HttpStatusCode is { } statusCode ? (int)statusCode : null,
            HttpReason: failureInfo.HttpReasonPhrase,
            FinalUrl: failureInfo.FinalUrl,
            Redirects: failureInfo.Redirects,
            DoNotUpdateBeforeUtc: failureInfo.DoNotUpdateBeforeUtc ?? existingState?.DoNotUpdateBeforeUtc,
            Id: id
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

    private string GetStatePath(string feedUrl)
    {
        return Path.Combine(GetFeedDirectory(feedUrl), "state.json");
    }

    private static string CreateResponsePreview(string responseText)
    {
        using var reader = new StringReader(responseText);
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

    private static void WriteResponseArtifacts(string dir, UpstreamFailureInfo failureInfo)
    {
        var headers = failureInfo.ResponseHeaders == null
            ? string.Empty
            : string.Join(Environment.NewLine, failureInfo.ResponseHeaders);

        File.WriteAllText(Path.Combine(dir, "headers.txt"), headers);
        File.WriteAllText(Path.Combine(dir, "response.xml"), failureInfo.ResponseBody ?? string.Empty);
    }
}
