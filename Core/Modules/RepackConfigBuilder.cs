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
        List<ProjectRepack> projectRepacks = [];

        var dependencyGraph = DependencyGraph.FromConfig(config);
        foreach (var proj in dependencyGraph.TopologicalOrder())
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
            logger.Info($"[📦] Packing {proj.Name} ({proj.Version}) to ({bumpedVersion})...");

            string pathToCsproj = Path.Combine(config.RootPath, proj.Path);
            projectRepacks.Add(new() { PathToCsproj = pathToCsproj, FromVersion = version, ToVersion = bumpedVersion });
        }
        return new RepackConfig()
        {
            LocalNugetPath = config.LocalNugetPath,
            Projects = projectRepacks,
        };
    }
}
