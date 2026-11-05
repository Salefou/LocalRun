namespace LocalRun.Cli;

using LocalRun.Core.Model;
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

/// <summary>The error codes of the JSON output. Several codes share an exit code: the number is coarse, the name precise.</summary>
internal static class ErrorCodes
{
    /// <summary>Invalid command line (exit code 2).</summary>
    public const string Usage = "usage";

    /// <summary>A config file to read is missing: run the command that writes it first (exit code 3).</summary>
    public const string ConfigNotFound = "config-not-found";

    /// <summary>A config file to read is empty or not valid JSON (exit code 3).</summary>
    public const string ConfigInvalid = "config-invalid";

    /// <summary>The directory <c>build</c> was asked to scan doesn't exist (exit code 3).</summary>
    public const string RootNotFound = "root-not-found";

    /// <summary>The settings file can't be read or holds an invalid value (exit code 3).</summary>
    public const string SettingsInvalid = "settings-invalid";

    /// <summary>A <c>dotnet</c> command failed (exit code 4).</summary>
    public const string DotnetFailed = "dotnet-failed";

    /// <summary><c>--strict</c>: the command completed, but its result left something out or may be wrong (exit code 5).</summary>
    public const string Strict = "strict";

    /// <summary>Any other failure (exit code 1).</summary>
    public const string CommandFailed = "command-failed";

    public static (string Code, int ExitCode) Of(Exception exception) => exception switch
    {
        UsageException => (Usage, LocalRunCli.ExitUsage),
        ConfigNotFoundException => (ConfigNotFound, LocalRunCli.ExitInput),
        ConfigInvalidException => (ConfigInvalid, LocalRunCli.ExitInput),
        RootNotFoundException => (RootNotFound, LocalRunCli.ExitInput),
        SettingsInvalidException => (SettingsInvalid, LocalRunCli.ExitInput),
        DotnetFailedException => (DotnetFailed, LocalRunCli.ExitDotnetFailed),
        _ => (CommandFailed, LocalRunCli.ExitFailure),
    };
}

internal sealed class ConfigNotFoundException(string message) : FileNotFoundException(message);

internal sealed class ConfigInvalidException(string message) : Exception(message);

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

/// <summary>The result of <c>pack</c> and of <c>go</c>.</summary>
/// <param name="Packed">Whether the whole plan was packed (false with <c>--dry-run</c>, or after a failure).</param>
/// <param name="Packages">The plan, in packing order (depth, then name), with what became of each project.</param>
internal sealed record PackResult(string Plan, bool Packed, IReadOnlyList<PackageReport> Packages)
{
    public static PackResult Of(string plan, bool packed, RepackConfig repackConfig, IReadOnlyDictionary<string, PackOutcome>? outcomes) =>
        new(plan, packed, [.. RepackExecutor.Depths(repackConfig).SelectMany(depth => depth).Select(proj =>
        {
            var outcome = outcomes?.GetValueOrDefault(proj.Name) ?? new PackOutcome();
            return new PackageReport(proj.Name, proj.PackageIdOrName, proj.Depth, proj.FromVersion.AsVersionString(),
                proj.ToVersion.AsVersionString(), outcome.Status, outcome.DurationMs, outcome.Failure);
        })]);
}

/// <param name="Status"><see cref="PackStatus"/>: <c>planned</c>, <c>packed</c>, <c>failed</c> or <c>skipped</c>.</param>
/// <param name="DurationMs">Time in its <c>dotnet</c> commands, or null when none ran.</param>
/// <param name="Failure">For a failed project: the failing step, its exit code and its error lines.</param>
internal sealed record PackageReport(string Project, string PackageId, int Depth, string From, string To,
    string Status, long? DurationMs, PackFailure? Failure);

internal sealed record SettingsResult(string Path, bool Created);
