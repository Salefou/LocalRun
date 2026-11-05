namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
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
    /// <summary><c>planned</c> before anything runs (a dry run), else <c>packed</c>, <c>failed</c>, or <c>skipped</c> (not reached).</summary>
    public string Status { get; internal set; } = PackStatus.Planned;

    /// <summary>Time spent in this project's <c>dotnet</c> commands, or null before any ran.</summary>
    public long? DurationMs { get; internal set; }

    public PackFailure? Failure { get; internal set; }
}

public static class PackStatus
{
    public const string Planned = "planned";
    public const string Packed = "packed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <param name="Step">The failing <c>dotnet</c> command: <c>clean</c>, <c>restore</c> or <c>pack</c>.</param>
/// <param name="Output">Its error lines, or its last lines when it printed none.</param>
public sealed record PackFailure(string Step, int ExitCode, IReadOnlyList<string> Output);

public class RepackExecutor
{
    private static readonly ILogger<RepackExecutor> logger = Log.For<RepackExecutor>();

    /// <summary>Names printed on a depth line before "and N more".</summary>
    private const int ShownPerDepth = 8;

    /// <summary>Lines of a failing command kept in its <see cref="PackFailure"/>.</summary>
    private const int FailureLines = 20;

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
                await PackAllAsync(depths[i], repackConfig.LocalNugetPath, versionsPropsPath);
            }
        }
        finally
        {
            File.Delete(versionsPropsPath);
        }
        logger.LogInformation(LogEvents.Done, "Packed {Projects} in {Duration}",
            Text.Count(depths.Sum(depth => depth.Count), "project"), Text.Duration(stopwatch.Elapsed));
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
            sb.AppendLine("  </PropertyGroup>");
        }
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    // NoSummary: MSBuild would list every warning a second time at the end of each command
    private const string NoSummary = " -clp:NoSummary";

    private async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, string versionsPropsPath)
    {
        await Task.WhenAll(projects.Select(async proj =>
        {
            await RunDotnetAsync("pack", proj, $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{nugetRepoPath}\" -p:CustomBeforeMicrosoftCommonProps=\"{versionsPropsPath}\"{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}");
            outcomes[proj.Name].Status = PackStatus.Packed;
        }));
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
            outcome.Failure = new PackFailure(step, proc.ExitCode, [.. (errors.IsEmpty ? output : errors).TakeLast(FailureLines)]);
            throw new DotnetFailedException($"dotnet {step} failed on {proj.Name} with exit code {proc.ExitCode} ({proj.PathToCsproj})");
        }
    }
}
