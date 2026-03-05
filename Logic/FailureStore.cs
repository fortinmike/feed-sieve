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

    #region Specific Recording Methods

    public void RecordFailure(UpstreamFailureInfo failureInfo, Exception? exception = null)
    {
        var directory = GetFeedDirectory(failureInfo.FeedUrl);
        var existingState = LoadState(GetStatePath(directory));
        var state = CreateFailureState(existingState, failureInfo);
        var failure = new Failure(
            State: state,
            Headers: FormatHeaders(failureInfo.ResponseHeaders),
            ResponseBodyData: new FailureResponseBodyData(
                FailureResponseBodyKind.Full,
                failureInfo.ResponseBody ?? string.Empty
            ),
            ExceptionLog: exception?.ToString()
        );
        SaveFailure(directory, failure);
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

        RecordFailure(failureInfo, exception);
    }

    public void RecordSuccess(string feedUrl)
    {
        SetFailureState(feedUrl, failure => failure with { ConsecutiveErrors = 0 });
    }

    private static FailureState CreateFailureState(FailureState? existingState, UpstreamFailureInfo failureInfo)
    {
        var now = DateTimeOffset.UtcNow;
        var hasConsecutiveErrors = existingState is { ConsecutiveErrors: > 0 };
        var firstErrorUtc = hasConsecutiveErrors ? existingState!.FirstErrorUtc : now;
        var id = hasConsecutiveErrors ? existingState!.Id : Guid.NewGuid().ToString("N");
        return new FailureState(
            FeedUrl: failureInfo.FeedUrl,
            Type: failureInfo.FailureType,
            Details: failureInfo.Message,
            FirstErrorUtc: firstErrorUtc,
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
    }

    private static string FormatHeaders(IReadOnlyList<string>? responseHeaders)
    {
        return responseHeaders == null ? string.Empty : string.Join(Environment.NewLine, responseHeaders);
    }

    #endregion

    #region State Accessors

    public FailureState? GetFailureState(string feedUrl)
    {
        return LoadState(GetStatePath(GetFeedDirectory(feedUrl)));
    }

    public void SetFailureState(string feedUrl, Func<FailureState, FailureState> update)
    {
        var directory = GetFeedDirectory(feedUrl);
        var statePath = GetStatePath(directory);
        var existingState = LoadState(statePath);
        if (existingState == null)
            return;

        var updatedState = update(existingState);
        if (updatedState == existingState)
            return;

        SaveState(statePath, updatedState);
    }

    public IReadOnlyList<Failure> GetFailures()
    {
        if (!_directory.Exists)
            return [];

        var failures = new List<Failure>();
        foreach (var dir in Directory.EnumerateDirectories(_directory.FullName))
        {
            var failure = LoadFailure(dir);
            if (failure != null && failure.State.ConsecutiveErrors > _consecutiveErrorsForFailureFeed)
                failures.Add(failure);
        }

        return failures;
    }

    #endregion

    #region Persistence

    private Failure? LoadFailure(string directory)
    {
        var state = LoadState(GetStatePath(directory));
        if (state == null)
            return null;

        return new Failure(
            State: state,
            Headers: LoadTextOrNull(Path.Combine(directory, "headers.txt")),
            ResponseBodyData: LoadResponseBodyPreview(Path.Combine(directory, "response.xml")),
            ExceptionLog: LoadTextOrNull(Path.Combine(directory, "exception.log"))
        );
    }

    private void SaveFailure(string directory, Failure failure)
    {
        Directory.CreateDirectory(directory);
        SaveState(GetStatePath(directory), failure.State);
        SaveText(Path.Combine(directory, "headers.txt"), failure.Headers);
        SaveResponseBody(Path.Combine(directory, "response.xml"), failure.ResponseBodyData);
        SaveText(Path.Combine(directory, "exception.log"), failure.ExceptionLog);
    }

    private static FailureState? LoadState(string statePath)
    {
        if (!File.Exists(statePath))
            return null;

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<FailureState>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? LoadTextOrNull(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static FailureResponseBodyData? LoadResponseBodyPreview(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var reader = File.OpenText(path);
            var preview = ReadPreview(reader);
            if (preview == null)
                return null;

            return new FailureResponseBodyData(FailureResponseBodyKind.Preview, preview);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadPreview(TextReader reader, int maxLines = 10)
    {
        var previewLines = new List<string>();

        for (var i = 0; i < maxLines; i++)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            previewLines.Add(line);
        }

        return previewLines.Count == 0 ? null : string.Join(Environment.NewLine, previewLines);
    }

    private static void SaveState(string statePath, FailureState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(statePath, json);
    }

    private static void SaveText(string path, string? content)
    {
        if (content == null)
            return;

        File.WriteAllText(path, content);
    }

    private static void SaveResponseBody(string path, FailureResponseBodyData? responseBodyData)
    {
        if (responseBodyData == null || responseBodyData.Kind != FailureResponseBodyKind.Full)
            return;

        File.WriteAllText(path, responseBodyData.Content);
    }

    private string GetFeedDirectory(string feedUrl)
    {
        return Path.Combine(_directory.FullName, feedUrl.ToSafeFileName());
    }

    private static string GetStatePath(string directory)
    {
        return Path.Combine(directory, "state.json");
    }

    #endregion
}
