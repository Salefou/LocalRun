namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

/// <summary>
/// Logging entry point. The minimum level is <see cref="MinimumLevel"/>, set by <c>Main</c> from <c>--log-level</c> or the
/// settings (<see cref="LevelNames"/>).
/// Warnings and errors are also collected (<see cref="Diagnostics"/>). In JSON mode (<see cref="OutputMode"/>), every log goes to stderr.
/// </summary>
public static class Log
{
    public const string LevelOption = "--log-level";

    /// <summary>The level names <see cref="LevelOption"/> and the <c>logLevel</c> setting accept, in any case.</summary>
    public static readonly string[] LevelNames = Enum.GetNames<LogLevel>();

    private static readonly DiagnosticsCollector collector = new();

    /// <summary>Read once, when the first logger is created: set it before anything logs.</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    // Created on first use, not by the type initializer, so that Main has set the output mode and level by then
    private static readonly Lazy<ILoggerFactory> Factory = new(CreateFactory);

    public static ILogger<T> For<T>() => Factory.Value.CreateLogger<T>();

    /// <summary>For static classes, which cannot be used as type arguments.</summary>
    public static ILogger For(string categoryName) => Factory.Value.CreateLogger(categoryName);

    /// <summary>The warnings and errors logged so far.</summary>
    public static IReadOnlyList<Diagnostic> Diagnostics => collector.Snapshot();

    /// <summary>
    /// The console provider writes from a background queue: dispose the factory on exit to flush it.
    /// </summary>
    public static void Shutdown()
    {
        if (Factory.IsValueCreated)
            Factory.Value.Dispose();
    }

    private static ILoggerFactory CreateFactory() => LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(MinimumLevel)
            .AddConsole(options =>
            {
                options.FormatterName = LocalRunConsoleFormatter.FormatterName;
                // stdout holds the JSON document alone
                if (OutputMode.Json)
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
            })
            .AddConsoleFormatter<LocalRunConsoleFormatter, ConsoleFormatterOptions>();
        builder.AddProvider(collector);
    });

    /// <summary>The level of a name of <see cref="LevelNames"/>, in any case; null for anything else (numbers included).</summary>
    public static LogLevel? ParseLevel(string? name) =>
        LevelNames.FirstOrDefault(level => level.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match
            ? Enum.Parse<LogLevel>(match)
            : null;

    /// <summary>
    /// The level a command line asks for (<see cref="LevelOption"/>, anywhere), else the settings', else Information.
    /// A bad value is ignored here: the command reports it (usage error, invalid settings) once logging works.
    /// </summary>
    public static LogLevel ChooseLevel(string[] args, Func<string?> settingsLevel)
    {
        int index = Array.IndexOf(args, LevelOption);
        if (index >= 0)
            return ParseLevel(index + 1 < args.Length ? args[index + 1] : null) ?? LogLevel.Information;
        try
        {
            return ParseLevel(settingsLevel()) ?? LogLevel.Information;
        }
        catch (Exception)
        {
            return LogLevel.Information;
        }
    }
}
