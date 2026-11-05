namespace LocalRun.Cli;

using LocalRun.Core.Modules;
using LocalRun.Logger;
using LocalRun.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>What a command produced as it ran: its result and the files it wrote, reported in JSON mode.</summary>
internal sealed class CommandOutput
{
    public object? Result { get; set; }

    public List<string> Written { get; } = [];
}

/// <summary>
/// The document <c>--json</c> writes on stdout, the only thing written there, whether the command succeeds or not.
/// Renaming or removing a field bumps <see cref="CurrentSchemaVersion"/>; adding one doesn't.
/// </summary>
/// <param name="Command">The command as given, lowercased; null without one.</param>
/// <param name="Status"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Result">The command's own result, or null (failed before having one, or a command without one yet).</param>
/// <param name="Diagnostics">The warnings and errors logged, in order (stderr has them too).</param>
/// <param name="Written">Full paths of the files written.</param>
internal sealed record CommandReport(
    int SchemaVersion,
    string? Command,
    string Status,
    int ExitCode,
    long DurationMs,
    object? Result,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> Written,
    ReportError? Error)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Read by programs, never embedded in HTML: keep '+' (in versions), quotes and non-ASCII names readable
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <param name="Code">A short stable string to branch on (<see cref="ErrorCodes"/>); the message is for people.</param>
internal sealed record ReportError(string Code, string Message);

internal static class ErrorCodes
{
    /// <summary>Invalid command line (exit code 2).</summary>
    public const string Usage = "usage";

    /// <summary>A config file to read is missing: run the command that writes it first.</summary>
    public const string ConfigNotFound = "config-not-found";

    /// <summary>The directory <c>build</c> was asked to scan doesn't exist.</summary>
    public const string RootNotFound = "root-not-found";

    /// <summary>Any other failure, until it gets a code of its own.</summary>
    public const string CommandFailed = "command-failed";

    public static string Of(Exception exception) => exception switch
    {
        UsageException => Usage,
        ConfigNotFoundException => ConfigNotFound,
        RootNotFoundException => RootNotFound,
        _ => CommandFailed,
    };
}

internal sealed class ConfigNotFoundException(string message) : FileNotFoundException(message);

internal sealed class RootNotFoundException(string message) : DirectoryNotFoundException(message);

internal sealed record HelpResult(string Help);

internal sealed record VersionResult(string Version);

/// <param name="Filters">The filters given, as typed (<c>--deps-of WebApp, --with-services</c>), or null.</param>
/// <param name="Plot">The page written by <c>--plot</c>, or null.</param>
internal sealed record BuildResult(
    string Config,
    string Root,
    string LocalNuget,
    string? Filters,
    int Scanned,
    ProjectCounts Projects,
    IReadOnlyList<string> Services,
    IReadOnlyList<LeftOutProjects> LeftOut,
    IReadOnlyList<string>? GitChanges,
    string? Plot);

internal sealed record ProjectCounts(int Total, int Libraries, int Executables);

internal sealed record PlotResult(string Plot);

/// <param name="Packed">Whether the plan was packed (false with <c>--dry-run</c>).</param>
/// <param name="Packages">The plan, in packing order: depth, then name.</param>
internal sealed record PackResult(string Plan, bool Packed, IReadOnlyList<PlannedPackage> Packages);

internal sealed record PlannedPackage(string Project, string PackageId, int Depth, string From, string To);

internal sealed record SettingsResult(string Path, bool Created);
