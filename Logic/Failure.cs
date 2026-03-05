public sealed record Failure(
    FailureState State,
    string? Headers,
    FailureResponseBodyData? ResponseBodyData,
    string? ExceptionLog
);

public enum FailureResponseBodyKind
{
    Full,
    Preview
}

public sealed record FailureResponseBodyData(FailureResponseBodyKind Kind, string Content);
