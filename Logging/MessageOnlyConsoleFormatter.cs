using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

public sealed class MessageOnlyConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "MessageOnly";

    public MessageOnlyConsoleFormatter()
        : base(FormatterName) { }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter
    )
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (!string.IsNullOrWhiteSpace(message))
            textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception);
    }
}
