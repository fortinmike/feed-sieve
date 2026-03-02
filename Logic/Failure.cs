public sealed record Failure(
    string FeedUrl,
    string SafeFeedId,
    string FailureType,
    string UserMessage,
    string Message,
    DateTimeOffset FirstFailureUtc,
    DateTimeOffset LastFailureUtc,
    int FailureCount,
    string Id,
    int? HttpStatusCode = null,
    string? HttpReasonPhrase = null,
    string? FinalUrl = null,
    IReadOnlyList<string>? Redirects = null
);
