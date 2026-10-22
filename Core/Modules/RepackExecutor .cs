namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

public class RepackExecutor
{
    private static readonly ILogger<RepackExecutor> logger = Log.For<RepackExecutor>();

    public async Task RepackAndRebuildAsync(RepackConfig repackConfig)
    {
        logger.LogInformation("[⚙ ] Starting repack in dependency order...");
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
        logger.LogInformation("[🚀] Repack complete. Took {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    private async Task PackAllAsync(List<ProjectRepack> projects, string nugetRepoPath, int depth)
    {
        var levelPackingTasks = projects.Select(async proj =>
        {
            logger.LogInformation("[📦] [Depth {Depth}] Packing {Project} ({FromVersion}) to ({ToVersion})...", depth, proj.Name, proj.FromVersion, proj.ToVersion);
            await RunProcessAsync("dotnet", $"pack \"{proj.PathToCsproj}\" --no-restore -o \"{nugetRepoPath}\" -p:PackageVersion=\"{proj.ToVersion.AsVersionString()}\"");
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