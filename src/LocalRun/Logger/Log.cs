namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

/// <summary>
/// Logging entry point. The minimum level defaults to Information and can be overridden
/// with the LOCALRUN_LOG_LEVEL environment variable (Trace, Debug, Information, Warning, Error, Critical, None).
/// </summary>
public static class Log
{
    private const string MinimumLevelVariable = "LOCALRUN_LOG_LEVEL";

    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder => builder
        .SetMinimumLevel(ReadMinimumLevel())
        .AddConsole(options => options.FormatterName = LocalRunConsoleFormatter.FormatterName)
        .AddConsoleFormatter<LocalRunConsoleFormatter, ConsoleFormatterOptions>());

    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();

    /// <summary>For static classes, which cannot be used as type arguments.</summary>
    public static ILogger For(string categoryName) => Factory.CreateLogger(categoryName);

    /// <summary>
    /// The console provider writes from a background queue: dispose the factory on exit to flush it.
    /// </summary>
    public static void Shutdown() => Factory.Dispose();

    private static LogLevel ReadMinimumLevel()
    {
        return Enum.TryParse(Environment.GetEnvironmentVariable(MinimumLevelVariable), ignoreCase: true, out LogLevel level)
            ? level
            : LogLevel.Information;
    }
}
