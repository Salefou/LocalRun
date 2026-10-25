namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Threading.Tasks;

public class RepackExecutor
{
    private static readonly ILogger<RepackExecutor> logger = Log.For<RepackExecutor>();

    public async Task RepackAndRebuildAsync(RepackConfig repackConfig)
    {
        logger.LogInformation("[⚙ ] Starting repack in dependency order...");
        // Restore uses the feed as a source before the first pack creates it: NuGet rejects a missing local source
        Directory.CreateDirectory(repackConfig.LocalNugetPath);
        string versionsPropsPath = Path.Combine(Path.GetTempPath(), $"localrun-versions-{Guid.NewGuid():N}.props");
        await File.WriteAllTextAsync(versionsPropsPath, BuildVersionsProps(repackConfig));
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            var independentTreePackingTasks = repackConfig.RepackTrees.Select(async projTree =>
            {
                var depthToProjects = projTree.Projects.GroupBy(proj => proj.Depth)
                    .ToDictionary(group => group.Key, group => group.ToList());

                foreach (var (depth, projects) in depthToProjects.OrderBy(p => p.Key))
                {
                    if (repackConfig.Clean)
                    {
                        await CleanAllAsync(projects, repackConfig.LocalNugetPath, depth);
                    }
                    await RestoreAllAsync(projects, repackConfig.LocalNugetPath, depth);
                    await PackAllAsync(projects, repackConfig.LocalNugetPath, versionsPropsPath, depth);
                }
            });
            await Task.WhenAll(independentTreePackingTasks);
        }
        finally
        {
            File.Delete(versionsPropsPath);
        }
        stopwatch.Stop();
        logger.LogInformation("[🚀] Repack complete. Took {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

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

    private async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, string versionsPropsPath, int depth)
    {
        var levelPackingTasks = projects.Select(async proj =>
        {
            logger.LogInformation("[📦] [Depth {Depth}] Packing {Project} ({FromVersion}) to ({ToVersion})...", depth, proj.Name, proj.FromVersion, proj.ToVersion);
            await RunProcessAsync("dotnet", $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{nugetRepoPath}\" -p:CustomBeforeMicrosoftCommonProps=\"{versionsPropsPath}\"");
        });
        await Task.WhenAll(levelPackingTasks);
    }

    private async Task CleanAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelCleanAndRestoreTasks = projects.Select(async proj =>
        {
            logger.LogInformation("[🔁] [Depth {Depth}] Cleaning {Project}...", depth, proj.Name);
            await RunProcessAsync("dotnet", $"clean \"{proj.PathToCsproj}\" --verbosity minimal");
        });
        await Task.WhenAll(levelCleanAndRestoreTasks);
    }

    private async Task RestoreAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelCleanAndRestoreTasks = projects.Select(async proj =>
        {
            logger.LogInformation("[🔁] [Depth {Depth}] Restoring {Project}...", depth, proj.Name);
            // The local feed is added to the sources from NuGet.config (--source would replace them all,
            // breaking restore of any third-party package that isn't in the global packages folder yet).
            // --force: a no-op restore would keep floating *-* references on the previous iteration
            await RunProcessAsync("dotnet", $"restore \"{proj.PathToCsproj}\" --force -p:RestoreAdditionalProjectSources=\"{nugetRepoPath}\" --verbosity minimal");
        });
        await Task.WhenAll(levelCleanAndRestoreTasks);
    }

    private static async Task RunProcessAsync(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) logger.LogInformation("{Line}", e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.LogWarning("{Line}", e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command `{fileName} {args}` failed with exit code {proc.ExitCode}");
        }
    }
}