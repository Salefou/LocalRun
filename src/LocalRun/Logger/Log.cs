namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

/// <summary>
/// Logging entry point. The minimum level defaults to Information and can be overridden
/// with the LOCALRUN_LOG_LEVEL environment variable (Trace, Debug, Information, Warning, Error, Critical, None).
/// Warnings and errors are also collected (<see cref="Diagnostics"/>). In JSON mode (<see cref="OutputMode"/>), every log goes to stderr.
/// </summary>
public static class Log
{
    private const string MinimumLevelVariable = "LOCALRUN_LOG_LEVEL";

    private static readonly DiagnosticsCollector collector = new();

    // Created on first use, not by the type initializer, so that Main has set the output mode by then
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
            .SetMinimumLevel(ReadMinimumLevel())
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

    private static LogLevel ReadMinimumLevel()
    {
        return Enum.TryParse(Environment.GetEnvironmentVariable(MinimumLevelVariable), ignoreCase: true, out LogLevel level)
            ? level
            : LogLevel.Information;
    }
}
