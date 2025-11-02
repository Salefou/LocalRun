namespace LocalRun.Core.Modules;
using LocalRun.Core.Model;
using LocalRun.Logger;
using System.Diagnostics;
using System.Threading.Tasks;

public class RepackExecutor
{
    private static readonly ILoggerShishe<RepackExecutor> logger = LoggerFactory.Create<RepackExecutor>();

    public async Task RepackAndRebuildAsync(RepackConfig repackConfig)
    {
        logger.Info("[⚙ ] Starting repack in dependency order...");
        IEnumerable<Task> independentTreePackingTasks = repackConfig.RepackTrees.Select(async projTree =>
        {
            var depthToProjects = projTree.Projects.GroupBy(proj => proj.Depth)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var depthAndProjects in depthToProjects.OrderBy(p => p.Key))
            {
                int currentDepth = depthAndProjects.Key;
                IEnumerable<Task> levelPackingTasks = depthAndProjects.Value.Select(async proj =>
                {
                    logger.Info($"[📦] Packing {proj.Name} ({proj.FromVersion}) to ({proj.ToVersion})...");
                    await RunProcessAsync("dotnet", $"pack \"{proj.PathToCsproj}\" -o \"{repackConfig.LocalNugetPath}\" -p:PackageVersion=\"{proj.ToVersion.AsVersionString()}\"");
                });
                await Task.WhenAll(levelPackingTasks);
            }
        });
        await Task.WhenAll(independentTreePackingTasks);

        logger.Info("[🔁] Cleaning and restoring dependent projects...");
        foreach (var proj in repackConfig.RepackTrees.SelectMany(tree => tree.Projects))
        {
            string pathToCsproj = proj.PathToCsproj;
            await RunProcessAsync("dotnet", $"clean \"{pathToCsproj}\" --verbosity minimal");
            await RunProcessAsync("dotnet", $"restore \"{pathToCsproj}\" --source \"{repackConfig.LocalNugetPath}\" --verbosity minimal");
        }

        logger.Info("[🚀] Repack complete.");
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