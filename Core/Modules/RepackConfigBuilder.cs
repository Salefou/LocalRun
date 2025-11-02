namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using System.Threading.Tasks;

internal class RepackConfigBuilder
{
    private static readonly ILoggerShishe<RepackConfigBuilder> logger = LoggerFactory.Create<RepackConfigBuilder>();

    private readonly NugetVersionChecker nugetVersionChecker;

    public RepackConfigBuilder()
    {
        this.nugetVersionChecker = new NugetVersionChecker();
    }

    public async Task<RepackConfig> BuildRepackConfig(LocalRunConfig config, RepackOptions repackOptions)
    {
        logger.Info("[⚙ ] Building repack config in dependency order...");
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
                    version = ProjectVersion.Parse(nugetVersion?.Version.ToString());
                }
                else
                {
                    version = proj.Version;
                }

                var bumpedVersion = repackOptions.BumpOptions?.Bump(version) ?? version with { };
                logger.Info($"[📦] Writing repack config of {proj.Name} ({proj.Version}) to ({bumpedVersion})...");

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
        };
    }
}
