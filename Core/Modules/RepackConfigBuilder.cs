namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

internal class RepackConfigBuilder
{
    private static readonly ILogger<RepackConfigBuilder> logger = Log.For<RepackConfigBuilder>();

    private readonly NugetVersionChecker nugetVersionChecker;

    public RepackConfigBuilder()
    {
        this.nugetVersionChecker = new NugetVersionChecker();
    }

    public async Task<RepackConfig> BuildRepackConfig(LocalRunConfig config, RepackOptions repackOptions)
    {
        logger.LogInformation("[⚙ ] Building repack config in dependency order...");
        var dependencyGraph = DependencyGraph.FromConfig(config);

        List<RepackTree> repackTrees = [];
        Dictionary<string, int> nameToDepth = dependencyGraph.ComputeNameToDepth();
        foreach (var projTree in dependencyGraph.ConnectedComponents)
        {
            List<ProjectRepack> projectRepacks = [];

            foreach (var proj in projTree)
            {
                if (proj.SkipRepack) continue;
                ProjectVersion version;
                if (repackOptions.UseRemoteAsVersionReference)
                {
                    //var nugetVersion = await nugetVersionChecker.GetLatestVersionAsync(proj.Name);
                    var nugetVersion = await nugetVersionChecker.GetLatestVersionAcrossSourcesAsync(proj.Name);
                    // Normalized keeps the prerelease label (1.4.2-beta); .Version would give 1.4.2.0
                    version = ProjectVersion.Parse(nugetVersion?.ToNormalizedString());
                }
                else
                {
                    version = proj.Version;
                }

                var bumpedVersion = repackOptions.BumpOptions?.Bump(version) ?? version with { };
                logger.LogInformation("[📦] Writing repack config of {Project} ({FromVersion}) to ({ToVersion})...", proj.Name, proj.Version, bumpedVersion);

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
        return new RepackConfig()
        {
            LocalNugetPath = config.LocalNugetPath,
            RepackTrees = repackTrees,
            Clean = repackOptions.CleanBeforeRestore
        };
    }
}
