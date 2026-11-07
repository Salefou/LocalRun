namespace LocalRun.Core.Modules;

using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>What <see cref="FeedVerifier"/> found for one library.</summary>
/// <param name="Status"><c>ok</c>, <c>missing</c> (no package in the feed) or <c>stale</c>.</param>
/// <param name="Reason">For a stale one: <c>csproj-ahead</c> (its csproj version is higher than the newest package's) or
/// <c>edited</c> (a file of its folder changed after that package was written).</param>
/// <param name="FeedVersion">The newest version in the feed, or null when missing.</param>
/// <param name="PackedAt">When that package was written (UTC).</param>
/// <param name="EditedFile">For <c>edited</c>: the newest file of the project's folder, relative to it, and when it changed (UTC).</param>
public sealed record PackageCheck(string Project, string PackageId, string Status, string? Reason, string CsprojVersion,
    string? FeedVersion, DateTime? PackedAt, string? EditedFile, DateTime? EditedAt)
{
    [JsonIgnore]
    public bool Ok => Status == FeedVerifier.Ok;
}

/// <summary>
/// Checks that the local feed holds an up-to-date package of every library of a config, so that what consumes them as
/// packages gets the code on disk: a package is missing, or stale when its csproj version is ahead of the feed's newest
/// (a version bump pulled since the last pack) or when a file of the project's folder changed after that package was written.
/// Executables are left out: <c>up</c> builds them from source.
/// </summary>
public static class FeedVerifier
{
    private static readonly ILogger logger = Log.For(typeof(FeedVerifier).FullName!);

    public const string Ok = "ok";
    public const string Missing = "missing";
    public const string Stale = "stale";

    /// <summary>Build outputs, which change when the project is built or packed (dot-folders are skipped too).</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj" };

    /// <returns>A check per library of the config, in config order. Logs a warning for each one not ok.</returns>
    public static List<PackageCheck> Verify(LocalRunConfig config)
    {
        var libraries = config.Projects.Where(proj => proj.Type != "executable").ToList();
        var projectFolders = config.Projects
            .Select(proj => Path.GetDirectoryName(Path.Combine(config.RootPath, proj.Path))!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool feedExists = Directory.Exists(config.LocalNugetPath);

        var checks = libraries.Select(proj => Check(proj, config.RootPath, config.LocalNugetPath, feedExists, projectFolders)).ToList();
        foreach (var check in checks.Where(check => !check.Ok))
        {
            if (check.Status == Missing)
            {
                logger.LogWarning(DiagnosticEvents.PackageMissing, "{Project}: no package in the local feed", check.Project);
            }
            else if (check.Reason == "csproj-ahead")
            {
                logger.LogWarning(DiagnosticEvents.PackageStale, "{Project}: stale, the csproj version {CsprojVersion} is ahead of {FeedVersion} in the feed",
                    check.Project, check.CsprojVersion, check.FeedVersion);
            }
            else
            {
                logger.LogWarning(DiagnosticEvents.PackageStale, "{Project}: stale, {File} changed at {EditedAt}, after {FeedVersion} was packed at {PackedAt}",
                    check.Project, check.EditedFile, Local(check.EditedAt!.Value), check.FeedVersion, Local(check.PackedAt!.Value));
            }
        }

        if (libraries.Count == 0)
            logger.LogWarning(DiagnosticEvents.EmptySelection, "No library in the config: nothing to verify");
        else if (!feedExists)
            logger.LogInformation("The local feed {Feed} doesn't exist: nothing was packed into it yet", config.LocalNugetPath);
        int ok = checks.Count(check => check.Ok);
        if (libraries.Count != 0)
        {
            // The failure itself is reported by the caller
            logger.LogInformation(ok == checks.Count ? LogEvents.Done : new EventId(), "{Ok} of {Libraries} up to date in {Feed}",
                ok, Text.Count(checks.Count, "library", "libraries"), config.LocalNugetPath);
        }
        return checks;
    }

    private static PackageCheck Check(ProjectNode proj, string rootPath, string feed, bool feedExists, HashSet<string> projectFolders)
    {
        string csprojVersion = proj.Version.AsVersionString();
        var newest = feedExists
            ? PackageIterations.Packages(feed, proj.PackageIdOrName).OrderByDescending(package => package.Version).FirstOrDefault()
            : default;
        if (newest.Version == null)
            return new(proj.Name, proj.PackageIdOrName, Missing, null, csprojVersion, null, null, null, null);

        string feedVersion = newest.Version.ToNormalizedString();
        DateTime packedAt = File.Exists(newest.Path) ? File.GetLastWriteTimeUtc(newest.Path) : Directory.GetLastWriteTimeUtc(newest.Path);
        // The number alone, as pack compares them: a 1.5.0 csproj is not ahead of 1.5.0-alpha.9
        if (NuGetVersion.TryParse(csprojVersion, out var csproj) && csproj.Version > newest.Version.Version)
            return new(proj.Name, proj.PackageIdOrName, Stale, "csproj-ahead", csprojVersion, feedVersion, packedAt, null, null);

        string projectFolder = Path.GetDirectoryName(Path.Combine(rootPath, proj.Path))!;
        if (NewestFile(projectFolder, projectFolders) is { } edited && edited.Time > packedAt)
        {
            return new(proj.Name, proj.PackageIdOrName, Stale, "edited", csprojVersion, feedVersion, packedAt,
                Path.GetRelativePath(projectFolder, edited.Path), edited.Time);
        }
        return new(proj.Name, proj.PackageIdOrName, Ok, null, csprojVersion, feedVersion, packedAt, null, null);
    }

    /// <summary>
    /// The most recently written file of a project's folder, without build outputs (<c>bin</c>, <c>obj</c>), dot-folders, and
    /// the folders of other projects nested in it.
    /// </summary>
    internal static (string Path, DateTime Time)? NewestFile(string projectFolder, IReadOnlySet<string> projectFolders)
    {
        (string Path, DateTime Time)? newest = null;
        if (!Directory.Exists(projectFolder))
            return newest;
        var folders = new Stack<string>([projectFolder]);
        while (folders.Count != 0)
        {
            string folder = folders.Pop();
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                DateTime time = File.GetLastWriteTimeUtc(file);
                if (newest == null || time > newest.Value.Time)
                    newest = (file, time);
            }
            foreach (var subfolder in Directory.EnumerateDirectories(folder))
            {
                string name = Path.GetFileName(subfolder);
                if (!SkippedFolders.Contains(name) && !name.StartsWith('.') && !projectFolders.Contains(subfolder))
                    folders.Push(subfolder);
            }
        }
        return newest;
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
