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

public class RepackExecutor
{
    private static readonly ILogger<RepackExecutor> logger = Log.For<RepackExecutor>();

    /// <summary>Names printed on a depth line before "and N more".</summary>
    private const int ShownPerDepth = 8;

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

    private static async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, string versionsPropsPath)
    {
        await Task.WhenAll(projects.Select(proj =>
            RunDotnetAsync("pack", proj, $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{nugetRepoPath}\" -p:CustomBeforeMicrosoftCommonProps=\"{versionsPropsPath}\"{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}")));
    }

    private static async Task CleanAllAsync(List<ProjectRepack> projects)
    {
        await Task.WhenAll(projects.Select(proj =>
            RunDotnetAsync("clean", proj, $"clean \"{proj.PathToCsproj}\" --verbosity minimal{NoSummary}{SolutionProperties.Arguments(proj.SolutionPath)}")));
    }

    private static async Task RestoreAllAsync(List<ProjectRepack> projects, string nugetRepoPath)
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
    /// fails without having printed an error, so a failure is never silent.
    /// </summary>
    private static async Task RunDotnetAsync(string step, ProjectRepack proj, string args)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        logger.LogDebug("dotnet {Arguments}", args);

        var output = new ConcurrentQueue<string>();
        bool printedError = false;
        void OnLine(string? line)
        {
            if (line == null) return;
            output.Enqueue(line);
            switch (DotnetOutput.DiagnosticLevel(line))
            {
                case LogLevel.Error:
                    printedError = true;
                    logger.LogError("{Line}", line);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning("{Line}", line);
                    break;
            }
        }

        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            if (!printedError)
            {
                foreach (var line in output)
                    logger.LogWarning("{Line}", line);
            }
            throw new InvalidOperationException($"dotnet {step} failed on {proj.Name} with exit code {proc.ExitCode} ({proj.PathToCsproj})");
        }
    }
}
