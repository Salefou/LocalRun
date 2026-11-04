namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System;
using System.IO;

/// <summary>
/// CLI-style console output, the category name left out. On a terminal, a line's color says its level (yellow warnings,
/// red errors, dim debug) and its mark (<see cref="LogEvents"/>) is a glyph. When output is redirected (CI, a pipe),
/// levels get a <c>[WRN]</c> / <c>[ERR]</c> / <c>[DBG]</c> prefix instead, marks an ASCII word, and progress lines are dropped.
/// </summary>
public sealed class LocalRunConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "localrun";

    private const string ResetForeground = "\x1B[39m\x1B[22m";

    private readonly bool onTerminal;
    private readonly bool useColors;

    public LocalRunConsoleFormatter()
        : this(onTerminal: Terminal.IsAttached, useColors: Terminal.ColorsEnabled)
    {
    }

    internal LocalRunConsoleFormatter(bool onTerminal, bool useColors)
        : base(FormatterName)
    {
        this.onTerminal = onTerminal;
        this.useColors = useColors;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (logEntry.EventId == LogEvents.Progress && !onTerminal) return;
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception == null) return;

        var (mark, markColor) = logEntry.EventId.Id switch
        {
            var id when id == LogEvents.Start.Id => (onTerminal ? "›" : ">", "\x1B[36m"),
            var id when id == LogEvents.Done.Id => (onTerminal ? "✓" : "ok", "\x1B[32m"),
            var id when id == LogEvents.Failed.Id => (onTerminal ? "×" : "FAILED", "\x1B[31m"),
            var id when id == LogEvents.Launched.Id => (onTerminal ? "▸" : "+", "\x1B[36m"),
            var id when id == LogEvents.Stopped.Id => (onTerminal ? "■" : "-", "\x1B[90m"),
            _ => (null, string.Empty),
        };
        var (prefix, lineColor) = logEntry.LogLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => ("[DBG] ", "\x1B[90m"),
            LogLevel.Warning => ("[WRN] ", "\x1B[33m"),
            LogLevel.Error or LogLevel.Critical => ("[ERR] ", "\x1B[31m"),
            _ => (string.Empty, string.Empty),
        };

        if (useColors)
        {
            if (mark != null) textWriter.Write($"{markColor}{mark}{ResetForeground} ");
            textWriter.WriteLine(lineColor.Length == 0 ? message : $"{lineColor}{message}{ResetForeground}");
        }
        else
        {
            // A mark already says how it went: FAILED needs no [ERR] in front of it
            textWriter.Write(mark != null ? $"{mark} " : prefix);
            textWriter.WriteLine(message);
        }
        if (logEntry.Exception != null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }
    }
}
