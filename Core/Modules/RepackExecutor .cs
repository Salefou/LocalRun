namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

public class RepackExecutor
{
    private static readonly ILoggerShishe<RepackExecutor> logger = LoggerFactory.Create<RepackExecutor>();

    public async Task RepackAndRebuildAsync(RepackConfig repackConfig)
    {
        logger.Info("[⚙ ] Starting repack in dependency order...");
        Stopwatch stopwatch = Stopwatch.StartNew();
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
                await PackAllAsync(projects, repackConfig.LocalNugetPath, depth);
            }
        });
        await Task.WhenAll(independentTreePackingTasks);
        stopwatch.Stop();
        logger.Info($"[🚀] Repack complete. Took {stopwatch.ElapsedMilliseconds}ms");
    }

    private async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelPackingTasks = projects.Select(async proj =>
        {
            logger.Info($"[📦] [Depth {depth}] Packing {proj.Name} ({proj.FromVersion}) to ({proj.ToVersion})...");
            await RunProcessAsync("dotnet", $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{nugetRepoPath}\" -p:PackageVersion=\"{proj.ToVersion.AsVersionString()}\"");
        });
        await Task.WhenAll(levelPackingTasks);
    }

    private async Task CleanAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelCleanAndRestoreTasks = projects.Select(async proj =>
        {
            logger.Info($"[🔁] [Depth {depth}] Cleaning {proj.Name}...");
            await RunProcessAsync("dotnet", $"clean \"{proj.PathToCsproj}\" --verbosity minimal");
        });
        await Task.WhenAll(levelCleanAndRestoreTasks);
    }

    private async Task RestoreAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelCleanAndRestoreTasks = projects.Select(async proj =>
        {
            logger.Info($"[🔁] [Depth {depth}] Restoring {proj.Name}...");
            // TODO source = everything in nuget manager
            await RunProcessAsync("dotnet", $"restore \"{proj.PathToCsproj}\" --source \"{nugetRepoPath}\" --verbosity minimal");
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
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) logger.Info(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.Info(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
    }
}