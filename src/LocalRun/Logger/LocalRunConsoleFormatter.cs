namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System;
using System.IO;

/// <summary>
/// CLI-style console output: Information is printed as-is, other levels get a colored prefix,
/// and the category name is left out. Colors are dropped when output is redirected (e.g. CI logs).
/// </summary>
public sealed class LocalRunConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "localrun";

    private const string ResetForeground = "\x1B[39m\x1B[22m";

    private readonly bool useColors;

    public LocalRunConsoleFormatter()
        : this(useColors: !Console.IsOutputRedirected)
    {
    }

    internal LocalRunConsoleFormatter(bool useColors)
        : base(FormatterName)
    {
        this.useColors = useColors;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception == null) return;

        var (prefix, color) = logEntry.LogLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => ("[DBG] ", "\x1B[1m\x1B[30m"),
            LogLevel.Warning => ("[WRN] ", "\x1B[33m"),
            LogLevel.Error or LogLevel.Critical => ("[ERR] ", "\x1B[31m"),
            _ => (string.Empty, string.Empty),
        };

        if (prefix.Length != 0)
        {
            textWriter.Write(useColors ? $"{color}{prefix}{ResetForeground}" : prefix);
        }
        textWriter.WriteLine(message);
        if (logEntry.Exception != null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }
    }
}
