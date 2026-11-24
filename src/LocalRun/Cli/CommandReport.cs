namespace LocalRun.Cli;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
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

    /// <param name="indented">False for a command that streams events first (<c>up</c>): every line of its stdout is a document.</param>
    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, indented ? JsonOptions : new JsonSerializerOptions(JsonOptions) { WriteIndented = false });
}

/// <summary>
/// Writes events on stdout as they happen, one JSON object per line (NDJSON), before the command's final document. Safe
/// from any thread; once closed, late events are dropped, so the document stays the last line.
/// </summary>
internal sealed class EventStream(TextWriter writer)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private bool closed;

    public void Write<T>(T @event)
    {
        string line = JsonSerializer.Serialize(@event, JsonOptions);
        lock (writer)
        {
            if (closed) return;
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    public void Close()
    {
        lock (writer)
            closed = true;
    }
}

/// <param name="Code">A short stable string to branch on (<see cref="ErrorCodes"/>); the message is for people.</param>
/// <param name="Hint">The way past the failure (a command to run, an option to add), or null (<see cref="ErrorHint"/>).</param>
internal sealed record ReportError(string Code, string Message, string? Hint = null);

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

    /// <summary><c>verify</c> (or <c>build --verify</c>): a library's package is missing from the local feed, or stale (exit code 6).</summary>
    public const string VerifyFailed = "verify-failed";

    /// <summary><c>doctor</c>: a check failed (exit code 1, as nothing a script would do differently from another failure).</summary>
    public const string DoctorFailed = "doctor-failed";

    /// <summary><c>up</c>: another <c>up</c> runs with this config: <c>down</c> stops it (exit code 1).</summary>
    public const string UpRunning = "up-running";

    /// <summary>Any other failure (exit code 1).</summary>
    public const string CommandFailed = "command-failed";

    /// <summary>Every code with its exit code and meaning, as <c>help --json</c> lists them.</summary>
    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        new(Usage, LocalRunCli.ExitUsage, "Invalid command line: an unknown command or option, a missing or malformed value."),
        new(ConfigNotFound, LocalRunCli.ExitInput, "A config file to read is missing: run the command that writes it first."),
        new(ConfigInvalid, LocalRunCli.ExitInput, "A config file to read is empty, not valid JSON, or written by a newer lrun."),
        new(RootNotFound, LocalRunCli.ExitInput, "The directory to scan doesn't exist."),
        new(SettingsInvalid, LocalRunCli.ExitInput, "The settings file can't be read or holds an invalid value."),
        new(DotnetFailed, LocalRunCli.ExitDotnetFailed, "A dotnet command failed: fix the code or the project, retrying won't help."),
        new(Strict, LocalRunCli.ExitStrict, "--strict: the command completed, but its result left something out or may be wrong."),
        new(VerifyFailed, LocalRunCli.ExitVerifyFailed, "verify: a library's package is missing from the local feed, or stale."),
        new(DoctorFailed, LocalRunCli.ExitFailure, "doctor: a setup check failed."),
        new(UpRunning, LocalRunCli.ExitFailure, "up: another up runs with this config; lrun down stops it."),
        new(CommandFailed, LocalRunCli.ExitFailure, "Any other failure."),
    ];

    public static (string Code, int ExitCode) Of(Exception exception) => exception switch
    {
        VerifyFailedException => (VerifyFailed, LocalRunCli.ExitVerifyFailed),
        DoctorFailedException => (DoctorFailed, LocalRunCli.ExitFailure),
        UpRunningException => (UpRunning, LocalRunCli.ExitFailure),
        UsageException => (Usage, LocalRunCli.ExitUsage),
        ConfigNotFoundException => (ConfigNotFound, LocalRunCli.ExitInput),
        ConfigInvalidException => (ConfigInvalid, LocalRunCli.ExitInput),
        RootNotFoundException => (RootNotFound, LocalRunCli.ExitInput),
        SettingsInvalidException => (SettingsInvalid, LocalRunCli.ExitInput),
        DotnetFailedException => (DotnetFailed, LocalRunCli.ExitDotnetFailed),
        _ => (CommandFailed, LocalRunCli.ExitFailure),
    };
}

/// <param name="ExitCode">The process exit code that goes with it: several codes share one.</param>
internal sealed record ErrorCode(string Code, int ExitCode, string Meaning);

internal sealed class ConfigNotFoundException(string message) : FileNotFoundException(message);

internal sealed class ConfigInvalidException(string message) : Exception(message);

internal sealed class RootNotFoundException(string message) : DirectoryNotFoundException(message);

internal sealed class VerifyFailedException(string message) : Exception(message);

internal sealed class DoctorFailedException(string message) : Exception(message);

internal sealed class UpRunningException(string message) : Exception(message);

/// <summary>The result of <c>doctor</c>.</summary>
/// <param name="Checks">Every finding, ok ones included, in the order they are printed: settings, dotnet, root, local
/// feed, sources, then each repository.</param>
internal sealed record DoctorResult(string Root, string LocalNuget, int Ok, int Warn, int Fail, IReadOnlyList<DoctorCheck> Checks)
{
    public static DoctorResult Of(string root, string localNuget, IReadOnlyList<DoctorCheck> checks) =>
        new(root, localNuget, checks.Count(check => check.Status == Doctor.Ok), checks.Count(check => check.Status == Doctor.Warn),
            checks.Count(check => check.Status == Doctor.Fail), checks);

    /// <summary>Throws when a check failed, after the result is set.</summary>
    public void ThrowIfFailed()
    {
        var failed = Checks.Where(check => check.Status == Doctor.Fail).Select(check => $"{check.Check} {check.Subject}").ToList();
        if (failed.Count != 0)
            throw new DoctorFailedException($"{Text.Count(failed.Count, "check")} failed: {Text.Abbreviate(failed)}.");
    }
}

/// <param name="Help">The help text, as <c>lrun help</c> prints it; the other fields hold the same as data.</param>
/// <param name="AgentInstructions">What <c>help --agent</c> prints: a section for an agent's instructions, stamped with this release.</param>
internal sealed record HelpResult(string Help, IReadOnlyList<OptionInfo> GlobalOptions, IReadOnlyList<CommandInfo> Commands,
    IReadOnlyList<ExitCodeInfo> ExitCodes, IReadOnlyList<ErrorCode> ErrorCodes, IReadOnlyList<DiagnosticCode> DiagnosticCodes,
    string AgentInstructions);

/// <param name="Name">One of <see cref="Schemas.Names"/>.</param>
internal sealed record SchemaResult(string Name, JsonElement Schema);

internal sealed record VersionResult(string Version);

/// <param name="Filters">The filters given, as typed (<c>--deps-of WebApp, --with-services</c>), or null.</param>
/// <param name="Plot">The page written by <c>--plot</c>, or null.</param>
/// <param name="Verify">With <c>--verify</c>, the check of the config just written, as <c>verify</c> reports it; else null.</param>
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
    string? Plot,
    VerifyResult? Verify = null);

/// <summary>The result of <c>verify</c>.</summary>
/// <param name="Feed">The local feed checked, from the config.</param>
/// <param name="Packages">A check per library of the config, in config order.</param>
internal sealed record VerifyResult(string Config, string Feed, int Libraries, int Ok, int Missing, int Stale, IReadOnlyList<PackageCheck> Packages)
{
    public static VerifyResult Of(string config, string feed, IReadOnlyList<PackageCheck> checks) =>
        new(config, feed, checks.Count, checks.Count(check => check.Status == FeedVerifier.Ok),
            checks.Count(check => check.Status == FeedVerifier.Missing), checks.Count(check => check.Status == FeedVerifier.Stale), checks);

    /// <summary>Throws when a package is missing or stale, after the result is set.</summary>
    public void ThrowIfFailed()
    {
        var failed = Packages.Where(check => !check.Ok).Select(check => check.Project).ToList();
        if (failed.Count != 0)
        {
            throw new VerifyFailedException($"{failed.Count} of {Text.Count(Libraries, "library", "libraries")} missing or stale in the local feed: "
                + $"{Text.Abbreviate(failed)}. Run '{LocalRunCli.CommandName} pack' to repack the config.");
        }
    }
}

internal sealed record ProjectCounts(int Total, int Libraries, int Executables);

/// <param name="Filters">The filters given, as typed (<c>--users-of Core, --with-services</c>), or null.</param>
/// <param name="Projects">What <c>build</c> would keep, in dependency order (depth, then name).</param>
/// <param name="LeftOut">As for <c>build</c>.</param>
internal sealed record ListResult(
    string Root,
    string? Filters,
    int Scanned,
    ProjectCounts Counts,
    IReadOnlyList<ListedProject> Projects,
    IReadOnlyList<LeftOutProjects> LeftOut,
    IReadOnlyList<string>? GitChanges)
{
    public static ListResult Of(string root, string? filters, ProjectSelection selection)
    {
        int executables = selection.Projects.Count(proj => proj.Type == "executable");
        return new(root, filters, selection.Summary.Scanned,
            new ProjectCounts(selection.Projects.Count, selection.Projects.Count - executables, executables),
            [.. selection.Projects.Select(proj => ListedProject.Of(proj, selection.Depths[proj.Name]))],
            selection.Summary.LeftOut, selection.Summary.GitChanges);
    }
}

/// <param name="Depth">From 0, as in <c>pack</c>'s result.</param>
/// <param name="Path">The csproj, relative to the root.</param>
/// <param name="Solution">Full path of the first solution referencing it, or null.</param>
/// <param name="Dependencies">As in the config: projects and packages it references, kept or not.</param>
/// <param name="LatestVersion">The highest version <c>build</c> found on the NuGet sources (<c>status</c>); null in <c>list</c>, which never queries them.</param>
/// <param name="Test">A test project, kept with <c>--tests</c>.</param>
/// <param name="Packable">Whether it produces a package, so <c>pack</c> plans it (<see cref="ProjectNode.ProducesPackage"/>).</param>
internal sealed record ListedProject(string Name, string PackageId, string Type, int Depth, string Version, string Path,
    string? Solution, IReadOnlyList<string> Dependencies, string? LatestVersion, bool Test, bool Packable)
{
    public static ListedProject Of(ProjectNode proj, int depth) =>
        new(proj.Name, proj.PackageIdOrName, proj.Type, depth, proj.Version.AsVersionString(), proj.Path, proj.SolutionPath,
            proj.Dependencies, proj.LatestVersion?.AsVersionString(), proj.Test, proj.ProducesPackage);
}

/// <summary>The result of <c>status</c>: what a config holds and the plan made from it, read from the files alone.</summary>
/// <param name="Written">When the config file was last written, a hand edit included.</param>
/// <param name="Build">How <c>build</c> made it, or null for a config written before 0.6.0.</param>
/// <param name="Projects">In dependency order (depth, then name), with the latest versions <c>build</c> found.</param>
/// <param name="Plan">The repack plan, or null when there is none.</param>
/// <param name="Running">The <c>up</c> running with this config, as it records itself, or null.</param>
internal sealed record StatusResult(
    string Config,
    DateTimeOffset Written,
    string Root,
    string LocalNuget,
    BuildRecord? Build,
    ProjectCounts Counts,
    IReadOnlyList<ListedProject> Projects,
    StatusPlan? Plan,
    UpState? Running)
{
    public static StatusResult Of(string configPath, DateTimeOffset written, LocalRunConfig config,
        (string Path, DateTimeOffset Written, RepackConfig Plan)? plan, UpState? running = null)
    {
        var depths = DependencyGraph.FromConfig(config).ComputeNameToDepth();
        var projects = config.Projects
            .Select(proj => ListedProject.Of(proj, depths.GetValueOrDefault(proj.Name)))
            .OrderBy(proj => proj.Depth).ThenBy(proj => proj.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int executables = projects.Count(proj => proj.Type == "executable");
        return new(configPath, written, config.RootPath, config.LocalNugetPath, config.Build,
            new ProjectCounts(projects.Count, projects.Count - executables, executables), projects,
            plan is { } found ? StatusPlan.Of(found.Path, found.Written, found.Written < written, found.Plan) : null, running);
    }
}

/// <param name="OlderThanConfig">Written before the config: planned from an earlier one (warning <c>plan-older-than-config</c>).</param>
/// <param name="Clean">Whether go cleans the projects before restoring them (<c>pack --clean</c>).</param>
/// <param name="Packages">In the order go packs them: by depth, then by name.</param>
internal sealed record StatusPlan(string Path, DateTimeOffset Written, bool OlderThanConfig, bool Clean, IReadOnlyList<PlannedPackage> Packages)
{
    public static StatusPlan Of(string path, DateTimeOffset written, bool olderThanConfig, RepackConfig plan) =>
        new(path, written, olderThanConfig, plan.Clean, [.. RepackExecutor.Depths(plan).SelectMany(depth => depth)
            .Select(proj => new PlannedPackage(proj.Name, proj.PackageIdOrName, proj.Depth, proj.FromVersion.AsVersionString(),
                proj.ToVersion.AsVersionString()))]);
}

/// <param name="Depth">From 0, as in <c>pack</c>'s result.</param>
internal sealed record PlannedPackage(string Project, string PackageId, int Depth, string From, string To);

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
                proj.ToVersion.AsVersionString(), outcome.Status, outcome.DurationMs, outcome.Failure, outcome.SkippedFor);
        })]);
}

/// <param name="Status"><see cref="PackStatus"/>: <c>planned</c>, <c>packed</c>, <c>not-packed</c>, <c>failed</c> or <c>skipped</c> (it depends on a failed project).</param>
/// <param name="DurationMs">Time in its <c>dotnet</c> commands, or null when none ran.</param>
/// <param name="Failure">For a failed project: the failing step, its exit code and its error lines.</param>
/// <param name="SkippedFor">For a skipped project: the failed projects it depends on, directly or not.</param>
internal sealed record PackageReport(string Project, string PackageId, int Depth, string From, string To,
    string Status, long? DurationMs, DotnetFailure? Failure, IReadOnlyList<string>? SkippedFor);

/// <param name="Valid">Whether the commands that read the file can use it (true when there is none).</param>
/// <param name="Problem">Why not, when <paramref name="Valid"/> is false (warned <c>settings-invalid</c>).</param>
internal sealed record SettingsResult(string Path, bool Created, bool Valid, string? Problem);

/// <param name="Logs">The folder of the services' log files.</param>
/// <param name="Services">The executables of the config, dependencies first, without the excluded ones.</param>
internal sealed record UpResult(string Logs, IReadOnlyList<ServiceReport> Services);

/// <summary>The result of <c>down</c>.</summary>
/// <param name="State">The state file looked at, next to the config.</param>
/// <param name="Up">The <c>up</c> stopped, as it recorded itself; null when none was running.</param>
/// <param name="Stopped">Whether an <c>up</c> was stopped.</param>
internal sealed record DownResult(string State, UpState? Up, bool Stopped);

/// <param name="Status"><see cref="ServiceStatus"/>: <c>not-started</c>, <c>exited</c>, <c>failed</c>, <c>stopped</c> or <c>killed</c>.</param>
/// <param name="ExitCode">Null when it never started, or was killed and didn't exit in time.</param>
/// <param name="Failure">When its build failed (it is then <c>not-started</c>, as all are): the step, its exit code and its errors.</param>
internal sealed record ServiceReport(string Project, string Status, int? ExitCode, string? Log, DotnetFailure? Failure);
