namespace LocalRun.Core.Modules;

using LocalRun.Contracts;
using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

public class RepackExecutor : IRepacker
{
    private static readonly ILoggerShishe<RepackExecutor> logger = LoggerFactory.Create<RepackExecutor>();

    private readonly NugetVersionChecker nugetVersionChecker = new();

    public async Task RepackAndRebuildAsync(LocalRunConfig config, RepackOptions repackOptions)
    {
        logger.Info("[⚙ ] Starting repack in dependency order...");

        var dependencyGraph = DependencyGraph.FromConfig(config);
        foreach (var proj in dependencyGraph.TopologicalOrder())
        {
            if (proj.SkipRepack) continue;
            ProjectVersion version;
            if (repackOptions.UseRemoteAsVersionReference)
            {
                var nugetVersion = await nugetVersionChecker.GetLatestVersionAcrossSourcesAsync(proj.Name);
                //var nugetVersion = await nugetVersionChecker.GetLatestVersionAsync(proj.Name);
                version = ProjectVersion.Parse(nugetVersion?.Version.ToString());
            }
            else
            {
                version = proj.Version;
            }

            var bumpedVersion = repackOptions.BumpOptions?.Bump(version) ?? version with { };
            logger.Info($"[📦] Packing {proj.Name} ({proj.Version}) to ({bumpedVersion})...");

            string pathToCsproj = Path.Combine(config.RootPath, proj.Path);
            await RunProcessAsync("dotnet", $"pack \"{pathToCsproj}\" -o \"{config.LocalNugetPath}\" -p:PackageVersion=\"{bumpedVersion.ToVersionString()}\"");
            /*--no-build*/
        }

        logger.Info("[🔁] Cleaning and restoring dependent solutions...");
        var allSolutions = config.Projects
            .Where(project => project != null)
            .Select(project => project!.SolutionPath)
            .Where(solutionPath => solutionPath != null)
            .ToHashSet()!;

        var solutionsFound = allSolutions.Where(File.Exists).ToList();
        var solutionsNotFound = allSolutions.Except(solutionsFound);
        foreach (var solutionNotFound in solutionsNotFound)
        {
            logger.Error($": Did not find solution file at {solutionNotFound}");
        }

        foreach (var solutionPath in solutionsFound)
        {
            await RunProcessAsync("dotnet", $"clean \"{solutionPath}\" --verbosity minimal");
            await RunProcessAsync("dotnet", $"restore \"{solutionPath}\" --source \"{config.LocalNugetPath}\" --verbosity minimal");
        }

        logger.Info($"[🔁] Cleaned and restored the following solutions: \n\t{string.Join("\n\t", solutionsFound.Select(path => path == null ? "UNKNOWN" : Path.GetRelativePath(config.RootPath, path)))}");
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