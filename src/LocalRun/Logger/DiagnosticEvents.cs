namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

/// <summary>
/// The codes of LocalRun's warnings and errors, given as the event id of the log call (ids from 100, apart from the marks
/// of <see cref="LogEvents"/>). The JSON output reports the name as the diagnostic's <c>code</c>, for a caller to branch on:
/// a name is a contract, renaming one is a breaking change. <see cref="Strict"/> lists those <c>--strict</c> fails on.
/// </summary>
public static class DiagnosticEvents
{
    // Left out or possibly wrong: --strict fails on these

    /// <summary>Several projects share a name: all left out of the config.</summary>
    public static readonly EventId NameCollision = new(100, "name-collision");

    /// <summary>A dependency cycle left out of the config.</summary>
    public static readonly EventId CycleRemoved = new(101, "cycle-removed");

    /// <summary>Projects kept in the config without their dependency on a collision or a cycle.</summary>
    public static readonly EventId KeptWithoutDependency = new(102, "kept-without-dependency");

    /// <summary>MSBuild could not evaluate a project: its version and package id may be wrong.</summary>
    public static readonly EventId EvaluationFailed = new(103, "evaluation-failed");

    /// <summary>A package id needs MSBuild, which was skipped or failed: the project name is used.</summary>
    public static readonly EventId UnevaluatedPackageId = new(104, "unevaluated-package-id");

    /// <summary>A version needs MSBuild, which was skipped or failed: 1.0.0 is used.</summary>
    public static readonly EventId UnevaluatedVersion = new(105, "unevaluated-version");

    /// <summary>A NuGet source could not be queried: the latest versions may be missing some.</summary>
    public static readonly EventId SourceQueryFailed = new(106, "source-query-failed");

    /// <summary>A csproj sets PackageVersion, which overrides the planned version.</summary>
    public static readonly EventId PackageVersionOverride = new(107, "package-version-override");

    /// <summary>A global packages folder is unknown: an iteration number may be reused.</summary>
    public static readonly EventId PackagesFolderUnknown = new(108, "packages-folder-unknown");

    // Expected or by design: reported, never failing

    /// <summary>Nothing selected: no project kept, nothing to pack, no executable to run. Exit code 0.</summary>
    public static readonly EventId EmptySelection = new(120, "empty-selection");

    /// <summary>A project no solution references: its commands get no <c>$(SolutionDir)</c>.</summary>
    public static readonly EventId NoSolution = new(121, "no-solution");

    /// <summary>The csproj version is ahead of the latest on the sources: pack starts from it.</summary>
    public static readonly EventId CsprojAheadOfLatest = new(122, "csproj-ahead-of-latest");

    /// <summary><c>--git-changes</c>: projects outside any git repository, counted as changed.</summary>
    public static readonly EventId NotInGit = new(123, "not-in-git");

    /// <summary><c>--git-changes</c>: no origin branch to compare with (fetch it), the whole repo counted as changed.</summary>
    public static readonly EventId NoGitBase = new(124, "no-git-base");

    /// <summary><c>--git-changes</c>: HEAD shares no history with the base, the whole repo counted as changed.</summary>
    public static readonly EventId NoCommonHistory = new(125, "no-common-history");

    /// <summary><c>--git-changes</c>: a repository could not be read, the whole repo counted as changed.</summary>
    public static readonly EventId RepoUnreadable = new(126, "repo-unreadable");

    /// <summary>A warning line of a <c>dotnet</c> command, verbatim.</summary>
    public static readonly EventId DotnetWarning = new(127, "dotnet-warning");

    /// <summary>An error line of a <c>dotnet</c> command, verbatim.</summary>
    public static readonly EventId DotnetError = new(128, "dotnet-error");

    /// <summary>The output of a failed <c>dotnet</c> command that printed no error line.</summary>
    public static readonly EventId DotnetOutput = new(129, "dotnet-output");

    /// <summary>A file or page could not be opened.</summary>
    public static readonly EventId OpenFailed = new(130, "open-failed");

    /// <summary><c>up</c>: the Windows job object is unavailable, processes may outlive LocalRun if it is killed.</summary>
    public static readonly EventId JobObjectFailed = new(131, "job-object-failed");

    /// <summary><c>up</c>: a process did not stop in time and was killed.</summary>
    public static readonly EventId ProcessKilled = new(132, "process-killed");

    /// <summary><c>up</c>: a warning or error line of a service.</summary>
    public static readonly EventId ServiceOutput = new(133, "service-output");

    // Verification failures: verify fails on them itself (exit code 6), --strict adds nothing

    /// <summary><c>verify</c>: a library of the config has no package in the local feed.</summary>
    public static readonly EventId PackageMissing = new(140, "package-missing");

    /// <summary><c>verify</c>: a library's newest package in the local feed is older than its csproj version or its files.</summary>
    public static readonly EventId PackageStale = new(141, "package-stale");

    // Setup findings of doctor: the result holds each one's check name; doctor fails on a failure itself (exit code 1)

    /// <summary><c>doctor</c>: a check that works, but whose result may surprise (an old fetch, no origin branch).</summary>
    public static readonly EventId DoctorWarning = new(150, "doctor-warning");

    /// <summary><c>doctor</c>: a check that will make a command fail (no SDK, a source unreachable, the feed not writable).</summary>
    public static readonly EventId DoctorFailure = new(151, "doctor-failure");

    /// <summary>The codes <c>--strict</c> turns into a failure (exit code 5): the result left something out, or may be wrong.</summary>
    public static readonly IReadOnlySet<string> Strict = new HashSet<string>(StringComparer.Ordinal)
    {
        NameCollision.Name!, CycleRemoved.Name!, KeptWithoutDependency.Name!, EvaluationFailed.Name!, UnevaluatedPackageId.Name!,
        UnevaluatedVersion.Name!, SourceQueryFailed.Name!, PackageVersionOverride.Name!, PackagesFolderUnknown.Name!,
    };

    /// <summary>The code of a log call's event id; <c>unspecified</c> for one without a code.</summary>
    public static string CodeOf(EventId eventId) =>
        eventId.Id >= NameCollision.Id && eventId.Name is { } name ? name : DiagnosticsCollector.UnspecifiedCode;
}
