public sealed record Failure(
    string Id,
    string FeedUrl,
    string Type,
    string Message,
    string Details,
    DateTimeOffset FirstErrorUtc,
    DateTimeOffset LastErrorUtc,
    int TotalErrors,
    int ConsecutiveErrors,
    int? HttpStatus = null,
    string? HttpReason = null,
    string? FinalUrl = null,
    IReadOnlyList<string>? Redirects = null
);
