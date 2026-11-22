namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Threading.Tasks;

/// <summary>What became of one project of a plan in a run.</summary>
public sealed class PackOutcome
{
    /// <summary>
    /// <c>planned</c> before anything runs (a dry run), else <c>packed</c>, <c>not-packed</c> (the pack succeeded but produced
    /// no package), <c>failed</c>, or <c>skipped</c> (not reached).
    /// </summary>
    public string Status { get; internal set; } = PackStatus.Planned;

    /// <summary>Time spent in this project's <c>dotnet</c> commands, or null before any ran.</summary>
    public long? DurationMs { get; internal set; }

    public DotnetFailure? Failure { get; internal set; }

    /// <summary>The version of the package produced, when it isn't the planned one (written back into the plan).</summary>
    public string? PackedVersion { get; internal set; }
}

public static class PackStatus
{
    public const string Planned = "planned";
    public const string Packed = "packed";
    public const string NotPacked = "not-packed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public class RepackExecutor
{
    private static readonly ILogger<RepackExecutor> logger = Log.For<RepackExecutor>();

    /// <summary>Names printed on a depth line before "and N more".</summary>
    private const int ShownPerDepth = 8;

    private readonly ConcurrentDictionary<string, PackOutcome> outcomes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each project of the last run, by name; complete after a failure too (the rest is <c>skipped</c>).</summary>
    public IReadOnlyDictionary<string, PackOutcome> Outcomes => outcomes;

    /// <summary>Prints the depths a run would go through, as <see cref="RepackAndRebuildAsync"/> does, without running them.</summary>
    public void DescribePlan(RepackConfig repackConfig)
    {
        var depths = Depths(repackConfig);
        for (int i = 0; i < depths.Count; i++)
            LogDepth(i, depths);
    }

    public async Task RepackAndRebuildAsync(RepackConfig repackConfig)
    {
        // Restore uses the feed as a source before the first pack creates it: NuGet rejects a missing local source
        Directory.CreateDirectory(repackConfig.LocalNugetPath);
        string versionsPropsPath = Path.Combine(Path.GetTempPath(), $"localrun-versions-{Guid.NewGuid():N}.props");
        await File.WriteAllTextAsync(versionsPropsPath, BuildVersionsProps(repackConfig));
        string packFolder = Path.Combine(Path.GetTempPath(), $"localrun-pack-{Guid.NewGuid():N}");
        Stopwatch stopwatch = Stopwatch.StartNew();
        var depths = Depths(repackConfig);
        outcomes.Clear();
        foreach (var proj in depths.SelectMany(depth => depth))
            outcomes[proj.Name] = new PackOutcome { Status = PackStatus.Skipped };
        try
        {
            for (int i = 0; i < depths.Count; i++)
            {
                LogDepth(i, depths);
                if (repackConfig.Clean)
                {
                    await CleanAllAsync(depths[i]);
                }
                await RestoreAllAsync(depths[i], repackConfig.LocalNugetPath);
                await PackAllAsync(depths[i], repackConfig.LocalNugetPath, versionsPropsPath, packFolder);
            }
        }
        finally
        {
            File.Delete(versionsPropsPath);
            if (Directory.Exists(packFolder))
                Directory.Delete(packFolder, recursive: true);
        }
        int notPacked = outcomes.Values.Count(outcome => outcome.Status == PackStatus.NotPacked);
        logger.LogInformation(LogEvents.Done, "Packed {Projects} in {Duration}{NotPacked}",
            Text.Count(depths.Sum(depth => depth.Count) - notPacked, "project"), Text.Duration(stopwatch.Elapsed),
            notPacked == 0 ? string.Empty : $" ({notPacked} produced no package)");
    }

    /// <summary>
    /// The projects of every tree, one list per depth. A depth starts once the previous one is packed in all trees: a tree can
    /// wait on a slower one, but each depth gets one line saying what is about to run.
    /// </summary>
    internal static List<List<ProjectRepack>> Depths(RepackConfig repackConfig) =>
        [.. repackConfig.RepackTrees.SelectMany(tree => tree.Projects)
            .GroupBy(proj => proj.Depth)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(proj => proj.Name, StringComparer.OrdinalIgnoreCase).ToList())];

    private static void LogDepth(int index, List<List<ProjectRepack>> depths) =>
        logger.LogInformation(LogEvents.Start, "Depth {Depth}/{Depths}: {Projects}",
            index + 1, depths.Count, Text.Abbreviate(depths[index].Select(proj => proj.Name), ShownPerDepth));

    /// <summary>
    /// MSBuild props giving every project of the plan its planned PackageVersion, keyed by project path.
    /// Passing -p:PackageVersion instead would be a global property: it flows into project references too,
    /// so a package would declare its own version as the version of every project it references.
    /// Imported through CustomBeforeMicrosoftCommonProps, which every project, referenced ones included, imports.
    /// MinVer sets the version in a target, after these props: its own override makes it take the planned one too.
    /// </summary>
    internal static string BuildVersionsProps(RepackConfig repackConfig)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project>");
        foreach (var proj in repackConfig.RepackTrees.SelectMany(tree => tree.Projects))
        {
            // MSBuild compares strings case-insensitively, so path casing doesn't matter
            string path = SecurityElement.Escape(Path.GetFullPath(proj.PathToCsproj));
            string version = SecurityElement.Escape(proj.ToVersion.AsVersionString());
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(MSBuildProjectFullPath)' == '{path}'\">");
            sb.AppendLine($"    <PackageVersion>{version}</PackageVersion>");
            sb.AppendLine($"    <MinVerVersionOverride>{version}</MinVerVersionOverride>");
            sb.AppendLine("  </PropertyGroup>");
        }
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    // NoSummary: MSBuild would list every warning a second time at the end of each command (up's builds use it too)
    internal const string NoSummary = " -clp:NoSummary";

    /// <summary>
    /// Packs each project into a folder of its own, then moves what it produced into the feed: the package itself says
    /// which version was packed, whatever set it (a tool setting the version in a target wins over the props file).
    /// </summary>
    private async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, string versionsPropsPath, string packFolder)
    {
        await Task.WhenAll(projects.Select(async proj =>
        {
            string output = Path.Combine(packFolder, Guid.NewGuid().ToString("N"));
            await RunDotnetAsync("pack", proj, $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{output}\" -p:CustomBeforeMicrosoftCommonProps=\"{versionsPropsPath}\"{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}");
            var outcome = outcomes[proj.Name];
            var packed = MoveToFeed(output, nugetRepoPath);
            if (packed == null)
            {
                outcome.Status = PackStatus.NotPacked;
                logger.LogWarning(DiagnosticEvents.NotPacked,
                    "{Project} produced no package: it is not packable (IsPackable false where lrun can't read it?). Set <IsPackable>false</IsPackable> in its csproj to leave it out of the plan",
                    proj.Name);
                return;
            }
            outcome.Status = PackStatus.Packed;
            if (PackedVersionIfOther(proj, packed) is { } other)
            {
                outcome.PackedVersion = other;
                logger.LogWarning(DiagnosticEvents.PackedVersionDiffers,
                    "{Project} was packed as {Packed}, not the planned {Planned}: its build sets the version itself. The plan now records {Packed}",
                    proj.Name, other, proj.ToVersion.AsVersionString(), other);
            }
        }));
    }

    /// <summary>Moves every file of a pack's output folder into the feed (packages, symbol packages).</summary>
    /// <returns>The version of the package moved, or null when the pack produced none.</returns>
    internal static NuGetVersion? MoveToFeed(string output, string feed)
    {
        if (!Directory.Exists(output))
            return null;
        NuGetVersion? version = null;
        foreach (var file in Directory.EnumerateFiles(output))
        {
            if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new PackageArchiveReader(file);
                version = reader.GetIdentity().Version;
            }
            File.Move(file, Path.Combine(feed, Path.GetFileName(file)), overwrite: true);
        }
        return version;
    }

    /// <summary>The version packed, normalized, when it isn't the planned one (build metadata aside).</summary>
    internal static string? PackedVersionIfOther(ProjectRepack proj, NuGetVersion packed) =>
        NuGetVersion.TryParse(proj.ToVersion.AsVersionString(), out var planned) && planned.Equals(packed)
            ? null
            : packed.ToNormalizedString();

    /// <summary>
    /// Writes what the run found into the plan: the version packed, for those packed under another one, and
    /// <see cref="ProjectRepack.NotPacked"/> (set for those that produced no package, cleared for those that did).
    /// </summary>
    /// <returns>Whether the plan changed.</returns>
    public bool RecordPackedVersions(RepackConfig repackConfig)
    {
        bool changed = false;
        foreach (var proj in repackConfig.RepackTrees.SelectMany(tree => tree.Projects))
        {
            var outcome = outcomes.GetValueOrDefault(proj.Name);
            if (outcome?.PackedVersion is { } packed)
            {
                proj.ToVersion = ProjectVersion.Parse(packed);
                changed = true;
            }
            bool notPacked = outcome?.Status == PackStatus.NotPacked;
            if (outcome?.Status is PackStatus.Packed or PackStatus.NotPacked && proj.NotPacked != notPacked)
            {
                proj.NotPacked = notPacked;
                changed = true;
            }
        }
        return changed;
    }

    private async Task CleanAllAsync(List<ProjectRepack> projects)
    {
        await Task.WhenAll(projects.Select(proj =>
            RunDotnetAsync("clean", proj, $"clean \"{proj.PathToCsproj}\" --verbosity minimal{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}")));
    }

    private async Task RestoreAllAsync(List<ProjectRepack> projects, string nugetRepoPath)
    {
        // The local feed is added to the sources from NuGet.config (--source would replace them all,
        // breaking restore of any third-party package that isn't in the global packages folder yet).
        // --force: a no-op restore would keep floating *-* references on the previous iteration
        await Task.WhenAll(projects.Select(proj =>
            RunDotnetAsync("restore", proj, $"restore \"{proj.PathToCsproj}\" --force -p:RestoreAdditionalProjectSources=\"{nugetRepoPath}\" --verbosity minimal{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}")));
    }

    /// <summary>
    /// Runs one `dotnet` command, printing its warnings and errors as they come, verbatim and all of them (a multi-target
    /// project repeats a warning per framework, which its owners want to see). The rest of its output is only printed if it
    /// fails without having printed an error, so a failure is never silent. Its duration and failure go into the
    /// project's <see cref="Outcomes"/>.
    /// </summary>
    private async Task RunDotnetAsync(string step, ProjectRepack proj, string args)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        logger.LogDebug("dotnet {Arguments}", args);

        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        void OnLine(string? line)
        {
            if (line == null) return;
            output.Enqueue(line);
            switch (DotnetOutput.DiagnosticLevel(line))
            {
                case LogLevel.Error:
                    errors.Enqueue(line);
                    logger.LogError(DiagnosticEvents.DotnetError, "{Line}", line);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(DiagnosticEvents.DotnetWarning, "{Line}", line);
                    break;
            }
        }

        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        // A project's commands run one after the other: no concurrent update of its outcome
        var outcome = outcomes[proj.Name];
        outcome.DurationMs = (outcome.DurationMs ?? 0) + stopwatch.ElapsedMilliseconds;
        if (proc.ExitCode != 0)
        {
            if (errors.IsEmpty)
            {
                foreach (var line in output)
                    logger.LogWarning(DiagnosticEvents.DotnetOutput, "{Line}", line);
            }
            outcome.Status = PackStatus.Failed;
            outcome.Failure = DotnetFailure.Of(step, proc.ExitCode, [.. output]);
            throw new DotnetFailedException($"dotnet {step} failed on {proj.Name} with exit code {proc.ExitCode} ({proj.PathToCsproj})");
        }
    }
}
