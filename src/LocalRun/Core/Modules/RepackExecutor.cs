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
    /// no package), <c>failed</c>, or <c>skipped</c> (it depends on a failed project: <see cref="SkippedFor"/>).
    /// </summary>
    public string Status { get; internal set; } = PackStatus.Planned;

    /// <summary>Time spent in this project's <c>dotnet</c> commands, or null before any ran.</summary>
    public long? DurationMs { get; internal set; }

    public DotnetFailure? Failure { get; internal set; }

    /// <summary>The version of the package produced, when it isn't the planned one (written back into the plan).</summary>
    public string? PackedVersion { get; internal set; }

    /// <summary>For a skipped project, the failed projects it depends on (directly or not), sorted by name.</summary>
    public IReadOnlyList<string>? SkippedFor { get; internal set; }
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

    /// <summary>
    /// The way past failed projects, attached to the run's failure (<see cref="ErrorHint"/>; the CLI writes it: the core
    /// doesn't name commands). Null for no suggestion.
    /// </summary>
    private readonly Func<IReadOnlyCollection<string>, string>? excludeHint;

    public RepackExecutor(Func<IReadOnlyCollection<string>, string>? excludeHint = null)
    {
        this.excludeHint = excludeHint;
    }

    /// <summary>Each project of the last run, by name; complete after a failure too (its users are <c>skipped</c>).</summary>
    public IReadOnlyDictionary<string, PackOutcome> Outcomes => outcomes;

    /// <summary>Prints the depths a run would go through, as <see cref="RepackAndRebuildAsync"/> does, without running them.</summary>
    public void DescribePlan(RepackConfig repackConfig)
    {
        var depths = Depths(repackConfig);
        for (int i = 0; i < depths.Count; i++)
            LogDepth(i, depths);
    }

    /// <summary>
    /// Packs the plan depth by depth. A project failing doesn't stop the run: the others go on, and whatever depends on it
    /// is skipped (its package would declare the planned version of one that never reached the feed). The run then fails
    /// with every failed project, and the hint to leave them out.
    /// </summary>
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
        // For each project failed or skipped so far, the failed projects behind it
        var failedBehind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (int i = 0; i < depths.Count; i++)
            {
                var runnable = new List<ProjectRepack>();
                foreach (var proj in depths[i])
                {
                    if (SkippedFor(proj, failedBehind) is { } failed)
                    {
                        outcomes[proj.Name].SkippedFor = failed;
                        failedBehind[proj.Name] = failed;
                    }
                    else
                    {
                        runnable.Add(proj);
                    }
                }
                if (runnable.Count == 0)
                    continue;
                LogDepth(i, depths.Count, runnable, depths[i].Count - runnable.Count);
                if (repackConfig.Clean)
                    runnable = await CleanAllAsync(runnable);
                runnable = await RestoreAllAsync(runnable, repackConfig.LocalNugetPath);
                await PackAllAsync(runnable, repackConfig.LocalNugetPath, versionsPropsPath, packFolder);
                foreach (var proj in depths[i].Where(proj => outcomes[proj.Name].Status == PackStatus.Failed))
                    failedBehind[proj.Name] = [proj.Name];
            }
        }
        finally
        {
            File.Delete(versionsPropsPath);
            if (Directory.Exists(packFolder))
                Directory.Delete(packFolder, recursive: true);
        }
        var all = depths.SelectMany(depth => depth).ToList();
        var failedProjects = all.Where(proj => outcomes[proj.Name].Status == PackStatus.Failed).Select(proj => proj.Name).ToList();
        if (failedProjects.Count != 0)
            throw ErrorHint.With(new DotnetFailedException(FailureMessage(all, outcomes)), HintFor(failedProjects, all.Count, excludeHint));
        int notPacked = outcomes.Values.Count(outcome => outcome.Status == PackStatus.NotPacked);
        logger.LogInformation(LogEvents.Done, "Packed {Projects} in {Duration}{NotPacked}",
            Text.Count(all.Count - notPacked, "project"), Text.Duration(stopwatch.Elapsed),
            notPacked == 0 ? string.Empty : $" ({notPacked} produced no package)");
    }

    /// <summary>
    /// The failed projects a project depends on, through <see cref="ProjectRepack.DependsOn"/>, or null when none failed.
    /// A plan written before it (no <c>DependsOn</c>) can't tell: every project after a failing depth is skipped.
    /// </summary>
    /// <param name="failedBehind">For each project failed or skipped in the earlier depths, the failed projects behind it.</param>
    internal static IReadOnlyList<string>? SkippedFor(ProjectRepack proj, IReadOnlyDictionary<string, IReadOnlyList<string>> failedBehind)
    {
        var dependencies = proj.DependsOn ?? (IEnumerable<string>)failedBehind.Keys;
        var failed = dependencies
            .SelectMany(name => failedBehind.GetValueOrDefault(name) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return failed.Count == 0 ? null : failed;
    }

    /// <summary>The way past the failed projects, or null when excluding them would leave nothing to pack.</summary>
    internal static string? HintFor(IReadOnlyCollection<string> failed, int planned, Func<IReadOnlyCollection<string>, string>? excludeHint) =>
        failed.Count < planned ? excludeHint?.Invoke(failed) : null;

    /// <summary>The failure of a run: the failed projects with the step that failed, then those skipped for them.</summary>
    internal static string FailureMessage(IReadOnlyList<ProjectRepack> plan, IReadOnlyDictionary<string, PackOutcome> outcomes)
    {
        var failed = plan.Where(proj => outcomes[proj.Name].Status == PackStatus.Failed).Select(proj => proj.Name).ToList();
        var skipped = plan.Where(proj => outcomes[proj.Name].SkippedFor != null).Select(proj => proj.Name).ToList();
        string Step(string name) => outcomes[name].Failure?.Step is { } step ? $"{name} ({step})" : name;
        var message = new StringBuilder($"dotnet failed on {Text.Abbreviate(failed.Select(Step))}");
        if (skipped.Count != 0)
            message.Append($"; skipped, as {(skipped.Count == 1 ? "it depends" : "they depend")} on {(failed.Count == 1 ? "it" : "them")}: {Text.Abbreviate(skipped)}");
        message.Append('.');
        return message.ToString();
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
        LogDepth(index, depths.Count, depths[index], skipped: 0);

    private static void LogDepth(int index, int count, List<ProjectRepack> projects, int skipped) =>
        logger.LogInformation(LogEvents.Start, "Depth {Depth}/{Depths}: {Projects}{Skipped}",
            index + 1, count, Text.Abbreviate(projects.Select(proj => proj.Name), ShownPerDepth),
            skipped == 0 ? string.Empty : $" ({skipped} skipped)");

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
            if (!await RunDotnetAsync("pack", proj, $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{output}\" -p:CustomBeforeMicrosoftCommonProps=\"{versionsPropsPath}\"{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}"))
                return;
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

    /// <returns>The projects cleaned; the others failed.</returns>
    private Task<List<ProjectRepack>> CleanAllAsync(List<ProjectRepack> projects) =>
        RunAllAsync(projects, proj =>
            RunDotnetAsync("clean", proj, $"clean \"{proj.PathToCsproj}\" --verbosity minimal{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}"));

    /// <returns>The projects restored; the others failed.</returns>
    private Task<List<ProjectRepack>> RestoreAllAsync(List<ProjectRepack> projects, string nugetRepoPath) =>
        // The local feed is added to the sources from NuGet.config (--source would replace them all,
        // breaking restore of any third-party package that isn't in the global packages folder yet).
        // --force: a no-op restore would keep floating *-* references on the previous iteration
        RunAllAsync(projects, proj =>
            RunDotnetAsync("restore", proj, $"restore \"{proj.PathToCsproj}\" --force -p:RestoreAdditionalProjectSources=\"{nugetRepoPath}\" --verbosity minimal{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}"));

    /// <summary>Runs a step on every project at once.</summary>
    /// <returns>The projects it succeeded on, in their order.</returns>
    private static async Task<List<ProjectRepack>> RunAllAsync(List<ProjectRepack> projects, Func<ProjectRepack, Task<bool>> step)
    {
        var succeeded = await Task.WhenAll(projects.Select(step));
        return [.. projects.Where((_, i) => succeeded[i])];
    }

    /// <summary>
    /// Runs one `dotnet` command, printing its warnings and errors as they come, verbatim and all of them (a multi-target
    /// project repeats a warning per framework, which its owners want to see). The rest of its output is only printed if it
    /// fails without having printed an error, so a failure is never silent. Its duration and failure go into the
    /// project's <see cref="Outcomes"/>.
    /// </summary>
    /// <returns>Whether it succeeded; when not, the project is <c>failed</c>.</returns>
    private async Task<bool> RunDotnetAsync(string step, ProjectRepack proj, string args)
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
            return false;
        }
        return true;
    }
}
