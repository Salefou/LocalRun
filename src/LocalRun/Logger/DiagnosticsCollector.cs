namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

/// <summary>A warning or an error logged during a command, as the JSON output reports it.</summary>
/// <param name="Code">What it is about, for a caller to branch on (<see cref="DiagnosticEvents"/>); <c>unspecified</c> without one.</param>
/// <param name="Level"><c>warning</c> or <c>error</c>.</param>
public sealed record Diagnostic(string Code, string Level, string Message);

/// <summary>
/// Keeps every warning and error logged, in order: the JSON output carries them as data besides the logs on stderr, and
/// <c>--strict</c> checks their codes.
/// </summary>
internal sealed class DiagnosticsCollector : ILoggerProvider
{
    public const string UnspecifiedCode = "unspecified";

    private readonly ConcurrentQueue<Diagnostic> diagnostics = new();

    public IReadOnlyList<Diagnostic> Snapshot() => [.. diagnostics];

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(DiagnosticsCollector collector) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            // Continuation lines are indented under the line they belong to: as data, they stand alone
            string message = formatter(state, exception).Trim();
            if (message.Length == 0) return;
            collector.diagnostics.Enqueue(new(DiagnosticEvents.CodeOf(eventId), logLevel == LogLevel.Warning ? "warning" : "error", message));
        }
    }
}
