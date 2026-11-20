namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>A diagnostic code as <c>help --json</c> lists it.</summary>
/// <param name="Strict">Whether <c>--strict</c> fails on it.</param>
public sealed record DiagnosticCode(string Code, bool Strict, string Meaning);

/// <summary>
/// The codes of LocalRun's warnings and errors, given as the event id of the log call (ids from 100, apart from the marks
/// of <see cref="LogEvents"/>). The JSON output reports the name as the diagnostic's <c>code</c>, for a caller to branch on:
/// a name is a contract, renaming one is a breaking change. Each is declared with its meaning, which <c>help --json</c>
/// lists; <see cref="Strict"/> holds those <c>--strict</c> fails on.
/// </summary>
public static class DiagnosticEvents
{
    // Declared first: the fields below register into it as they initialize, in textual order
    private static readonly List<DiagnosticCode> all = [];

    // Left out or possibly wrong: --strict fails on these

    public static readonly EventId NameCollision = Code(100, "name-collision", strict: true,
        "Several projects share a name: all left out of the config.");

    public static readonly EventId CycleRemoved = Code(101, "cycle-removed", strict: true,
        "A dependency cycle left out of the config.");

    public static readonly EventId KeptWithoutDependency = Code(102, "kept-without-dependency", strict: true,
        "Projects kept in the config without their dependency on a collision or a cycle: packing them may fail.");

    public static readonly EventId EvaluationFailed = Code(103, "evaluation-failed", strict: true,
        "MSBuild could not evaluate a project: its version and package id may be wrong.");

    public static readonly EventId UnevaluatedPackageId = Code(104, "unevaluated-package-id", strict: true,
        "A package id needs MSBuild, which was skipped (--csproj) or failed: the project name is used.");

    public static readonly EventId UnevaluatedVersion = Code(105, "unevaluated-version", strict: true,
        "A version needs MSBuild, which was skipped (--csproj) or failed: 1.0.0 is used.");

    public static readonly EventId SourceQueryFailed = Code(106, "source-query-failed", strict: true,
        "A NuGet source could not be queried: the latest versions may be missing some.");

    public static readonly EventId PackageVersionOverride = Code(107, "package-version-override", strict: true,
        "A csproj sets PackageVersion, which overrides the planned version.");

    public static readonly EventId PackagesFolderUnknown = Code(108, "packages-folder-unknown", strict: true,
        "A global packages folder is unknown: an iteration number may be reused.");

    public static readonly EventId UnknownConfigField = Code(109, "unknown-config-field", strict: true,
        "A config file holds fields this lrun doesn't know (a typo, or a field of another version): they are ignored.");

    // Expected or by design: reported, never failing

    public static readonly EventId EmptySelection = Code(120, "empty-selection", strict: false,
        "Nothing selected: no project kept, nothing to pack, no executable to run. The command succeeds.");

    public static readonly EventId NoSolution = Code(121, "no-solution", strict: false,
        "A project no solution references: its commands get no $(SolutionDir).");

    public static readonly EventId CsprojAheadOfLatest = Code(122, "csproj-ahead-of-latest", strict: false,
        "The csproj version is ahead of the latest on the sources: pack starts from it.");

    public static readonly EventId NotInGit = Code(123, "not-in-git", strict: false,
        "--git-changes: projects outside any git repository, counted as changed.");

    public static readonly EventId NoGitBase = Code(124, "no-git-base", strict: false,
        "--git-changes: no origin branch to compare with (run git fetch), the whole repository counted as changed.");

    public static readonly EventId NoCommonHistory = Code(125, "no-common-history", strict: false,
        "--git-changes: HEAD shares no history with the base, the whole repository counted as changed.");

    public static readonly EventId RepoUnreadable = Code(126, "repo-unreadable", strict: false,
        "--git-changes: a repository could not be read, the whole repository counted as changed.");

    public static readonly EventId DotnetWarning = Code(127, "dotnet-warning", strict: false,
        "A warning line of a dotnet command, verbatim.");

    public static readonly EventId DotnetError = Code(128, "dotnet-error", strict: false,
        "An error line of a dotnet command, verbatim.");

    public static readonly EventId DotnetOutput = Code(129, "dotnet-output", strict: false,
        "The output of a failed dotnet command that printed no error line.");

    public static readonly EventId OpenFailed = Code(130, "open-failed", strict: false,
        "A file or page could not be opened.");

    public static readonly EventId JobObjectFailed = Code(131, "job-object-failed", strict: false,
        "up: the Windows job object is unavailable, processes may outlive lrun if it is killed.");

    public static readonly EventId ProcessKilled = Code(132, "process-killed", strict: false,
        "up: a process did not stop in time and was killed.");

    public static readonly EventId ServiceOutput = Code(133, "service-output", strict: false,
        "up: a warning or error line of a service.");

    // Verification failures: verify fails on them itself (exit code 6), --strict adds nothing

    public static readonly EventId PackageMissing = Code(140, "package-missing", strict: false,
        "verify: a library of the config has no package in the local feed (verify fails with exit code 6).");

    public static readonly EventId PackageStale = Code(141, "package-stale", strict: false,
        "verify: a library's newest package in the local feed is older than its csproj version or its files (exit code 6).");

    // Setup findings of doctor: the result holds each one's check name; doctor fails on a failure itself (exit code 1)

    public static readonly EventId DoctorWarning = Code(150, "doctor-warning", strict: false,
        "doctor: a check that works, but whose result may surprise (an old fetch, no origin branch).");

    public static readonly EventId DoctorFailure = Code(151, "doctor-failure", strict: false,
        "doctor: a check that will make a command fail (no SDK, a source unreachable, the feed not writable).");

    public static readonly EventId PlannedBelowBase = Code(160, "planned-below-base", strict: false,
        "pack: a planned version sorts below its prerelease base on the same version, its label being lower " +
        "(1.0.1-q.2 -> 1.0.1-local.1): a restore of the latest won't pick it. --next plans above it.");

    /// <summary>Every code, in id order, with its meaning; <see cref="DiagnosticsCollector.UnspecifiedCode"/> last.</summary>
    public static IReadOnlyList<DiagnosticCode> All { get; } =
        [.. all, new(DiagnosticsCollector.UnspecifiedCode, false, "A warning without a code yet.")];

    /// <summary>The codes <c>--strict</c> turns into a failure (exit code 5): the result left something out, or may be wrong.</summary>
    public static readonly IReadOnlySet<string> Strict =
        new HashSet<string>(all.Where(code => code.Strict).Select(code => code.Code), StringComparer.Ordinal);

    /// <summary>The code of a log call's event id; <c>unspecified</c> for one without a code.</summary>
    public static string CodeOf(EventId eventId) =>
        eventId.Id >= NameCollision.Id && eventId.Name is { } name ? name : DiagnosticsCollector.UnspecifiedCode;

    private static EventId Code(int id, string name, bool strict, string meaning)
    {
        all.Add(new(name, strict, meaning));
        return new(id, name);
    }
}
