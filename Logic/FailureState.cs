public sealed record FailureState(
    string Id,
    string FeedUrl,
    string Type,
    string Details,
    DateTimeOffset FirstErrorUtc,
    DateTimeOffset LastErrorUtc,
    int TotalErrors,
    int ConsecutiveErrors,
    int? HttpStatus = null,
    string? HttpReason = null,
    string? FinalUrl = null,
    DateTimeOffset? DoNotUpdateBeforeUtc = null,
    IReadOnlyList<string>? Redirects = null
);
