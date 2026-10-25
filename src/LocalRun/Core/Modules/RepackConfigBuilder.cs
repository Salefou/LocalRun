namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using System.Threading.Tasks;

internal class RepackConfigBuilder
{
    private static readonly ILogger<RepackConfigBuilder> logger = Log.For<RepackConfigBuilder>();

    /// <summary>The global packages folder a restore from the given project folder uses (NuGet.Config, NUGET_PACKAGES).</summary>
    private readonly Func<string, string> globalPackagesFolderOf;

    public RepackConfigBuilder()
        : this(projectFolder => SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(projectFolder)))
    {
    }

    internal RepackConfigBuilder(Func<string, string> globalPackagesFolderOf)
    {
        this.globalPackagesFolderOf = globalPackagesFolderOf;
    }

    public Task<RepackConfig> BuildRepackConfig(LocalRunConfig config, RepackOptions repackOptions)
    {
        logger.LogInformation("[⚙ ] Building repack config in dependency order...");
        var dependencyGraph = DependencyGraph.FromConfig(config);

        // The remote version recorded by 'build --remote' wins: it is what the csproj version may lag behind
        var fromVersions = config.Projects.ToDictionary(proj => proj.Name, proj => proj.RemoteVersion ?? proj.Version, StringComparer.OrdinalIgnoreCase);
        var toVersions = fromVersions.ToDictionary(
            entry => entry.Key,
            entry => repackOptions.BumpOptions?.Bump(entry.Value) ?? entry.Value with { },
            StringComparer.OrdinalIgnoreCase);

        // Only a label set by the suffix is known to end the version (a kept label may carry +metadata)
        if (repackOptions.BumpOptions?.Suffix != null && toVersions.Count != 0)
        {
            int iteration = PackageIterations.Next(toVersions.Select(entry => (entry.Key, entry.Value)), PackageFolders(config));
            logger.LogInformation("[🔢] Iteration {Iteration} ({Reason})", iteration,
                iteration == 1 ? "first pack of these versions" : $"up to .{iteration - 1} found in the local feed or the NuGet cache");
            foreach (var name in toVersions.Keys.ToList())
                toVersions[name] = toVersions[name] with { PatchAndSuffix = $"{toVersions[name].PatchAndSuffix}.{iteration}" };
        }

        List<RepackTree> repackTrees = [];
        Dictionary<string, int> nameToDepth = dependencyGraph.ComputeNameToDepth();
        foreach (var projTree in dependencyGraph.ConnectedComponents)
        {
            List<ProjectRepack> projectRepacks = [];

            foreach (var proj in projTree)
            {
                var version = fromVersions[proj.Name];
                var bumpedVersion = toVersions[proj.Name];
                logger.LogInformation("[📦] Writing repack config of {Project} ({FromVersion}) to ({ToVersion})...", proj.Name, version, bumpedVersion);

                string pathToCsproj = Path.Combine(config.RootPath, proj.Path);
                int depth = nameToDepth[proj.Name];
                projectRepacks.Add(new()
                {
                    Name = proj.Name,
                    PathToCsproj = pathToCsproj,
                    FromVersion = version,
                    ToVersion = bumpedVersion,
                    Depth = depth,
                });
            }
            repackTrees.Add(new()
            {
                Projects = [.. projectRepacks
                    .OrderBy(proj => proj.Depth)
                    .ThenBy(proj => proj.PathToCsproj)]
            });
        }
        return Task.FromResult(new RepackConfig()
        {
            LocalNugetPath = config.LocalNugetPath,
            RepackTrees = repackTrees,
            Clean = repackOptions.CleanBeforeRestore
        });
    }

    /// <summary>Where earlier iterations can be: the local feed, and the global packages folders the consumers restored them into.</summary>
    private List<string> PackageFolders(LocalRunConfig config)
    {
        List<string> folders = [config.LocalNugetPath];
        var projectFolders = config.Projects
            .Select(proj => Path.GetDirectoryName(Path.Combine(config.RootPath, proj.Path))!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var projectFolder in projectFolders)
        {
            try
            {
                folders.Add(globalPackagesFolderOf(projectFolder));
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not find the NuGet global packages folder for {Folder}, earlier iterations cached there are not counted: {Error}", projectFolder, ex.Message);
            }
        }
        return [.. folders.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
