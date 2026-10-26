namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System;
using System.IO;

/// <summary>
/// CLI-style console output: Information is printed as-is, other levels get a colored prefix,
/// and the category name is left out. Absolute paths become clickable links. Colors and links are dropped when output
/// is redirected (e.g. CI logs).
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
        if (useColors && logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            message = LinkPaths(message, values);
        }

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

    /// <summary>
    /// Turns the absolute paths among the message's values into OSC 8 hyperlinks, which terminals (Windows Terminal,
    /// VS Code) open on Ctrl+click while showing the path unchanged. A path contained in a longer one is left as is.
    /// </summary>
    internal static string LinkPaths(string message, IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        var paths = values
            .Select(value => value.Value as string)
            .Where(value => !string.IsNullOrEmpty(value) && Path.IsPathFullyQualified(value) && message.Contains(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var path in paths.Where(path => !paths.Any(other => other.Length > path.Length && other.Contains(path))))
        {
            message = message.Replace(path, $"\x1B]8;;{new Uri(path).AbsoluteUri}\x1B\\{path}\x1B]8;;\x1B\\");
        }
        return message;
    }
}
