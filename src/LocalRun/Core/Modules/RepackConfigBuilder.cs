namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Versioning;
using System.Threading.Tasks;
using System.Xml.Linq;

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
        var dependencyGraph = DependencyGraph.FromConfig(config);

        var fromVersions = config.Projects.ToDictionary(proj => proj.Name, proj => BaseVersion(proj).Version, StringComparer.OrdinalIgnoreCase);
        var ahead = config.Projects.Where(proj => BaseVersion(proj).CsprojAhead).Select(proj => proj.Name).ToList();
        if (ahead.Count != 0)
        {
            logger.LogWarning(DiagnosticEvents.CsprojAheadOfLatest, "The csproj version is higher than the latest on your sources for {Count} project(s): pack starts from the csproj version and ignores the bump flag for them, only the suffix applies. {Projects}",
                ahead.Count, Text.Abbreviate(ahead, shown: 5));
        }

        // A csproj ahead of the latest already is the version in development: a bump would skip it (11.0.0-preview, --patch → 11.0.1)
        var withoutBump = repackOptions.BumpOptions == null ? null : new BumpOptions { BumpType = BumpType.None, Suffix = repackOptions.BumpOptions.Suffix };
        var aheadNames = ahead.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toVersions = fromVersions.ToDictionary(
            entry => entry.Key,
            entry => (aheadNames.Contains(entry.Key) ? withoutBump : repackOptions.BumpOptions)?.Bump(entry.Value) ?? entry.Value with { },
            StringComparer.OrdinalIgnoreCase);

        // Only a label set by the suffix is known to end the version (a kept label may carry +metadata)
        if (repackOptions.BumpOptions?.Suffix != null && toVersions.Count != 0)
        {
            var packageIds = config.Projects.ToDictionary(proj => proj.Name, proj => proj.PackageIdOrName, StringComparer.OrdinalIgnoreCase);
            int iteration = PackageIterations.Next(toVersions.Select(entry => (packageIds[entry.Key], entry.Value, fromVersions[entry.Key])), PackageFolders(config));
            logger.LogDebug("Iteration {Iteration} ({Reason})", iteration,
                iteration == 1 ? "first pack of these versions" : $"up to .{iteration - 1} found on the sources, in the local feed or in the NuGet cache");
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
                string pathToCsproj = Path.Combine(config.RootPath, proj.Path);
                if (OverridesPackageVersion(pathToCsproj))
                {
                    logger.LogWarning(DiagnosticEvents.PackageVersionOverride, "{Project} sets <PackageVersion> in its csproj, which overrides the planned version: go will pack it under the csproj's",
                        proj.Name);
                }
                int depth = nameToDepth[proj.Name];
                projectRepacks.Add(new()
                {
                    Name = proj.Name,
                    PackageId = proj.PackageId,
                    PathToCsproj = pathToCsproj,
                    SolutionPath = proj.SolutionPath,
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
        LogVersions([.. config.Projects.Select(proj => (proj.Name, fromVersions[proj.Name], toVersions[proj.Name]))]);
        return Task.FromResult(new RepackConfig()
        {
            LocalNugetPath = config.LocalNugetPath,
            RepackTrees = repackTrees,
            Clean = repackOptions.CleanBeforeRestore
        });
    }

    /// <summary>The plan's versions: the change most projects share, once, then a line per project that differs.</summary>
    private static void LogVersions(IReadOnlyList<(string Name, ProjectVersion From, ProjectVersion To)> plan)
    {
        if (plan.Count == 0)
        {
            logger.LogWarning(DiagnosticEvents.EmptySelection, "Nothing to pack: the config holds no project.");
            return;
        }
        var (common, commonCount, others) = VersionChanges.Summarize(plan, Terminal.Arrow);
        if (common != null)
        {
            logger.LogInformation("Versions: {Change}, {Projects}{Others}", common, Text.Count(commonCount, "project"),
                others.Count == 0 ? string.Empty : $"; {others.Count} other(s):");
        }
        foreach (var (name, change) in others)
            logger.LogInformation(common == null ? "{Project}: {Change}" : "  {Project}: {Change}", name, change);
    }

    /// <summary>
    /// What <c>pack</c> starts from: the higher of the latest version on the sources recorded by <c>build</c> and the csproj
    /// version. The latest wins over a csproj that lags behind releases and your builds; the csproj wins when it is ahead, being the
    /// version in development (<c>11.0.0-preview</c> while <c>10.7.0</c> is released). "Higher" is the number alone: <c>1.5.0</c>
    /// against <c>1.5.0-alpha.9</c> is the same release, whose alpha iterations go on from the latest.
    /// </summary>
    /// <returns>The base, and whether it is the csproj version ahead of a latest (the caller warns).</returns>
    internal static (ProjectVersion Version, bool CsprojAhead) BaseVersion(ProjectNode project)
    {
        if (project.LatestVersion is not { } latest)
            return (project.Version, false);
        bool csprojAhead = NuGetVersion.TryParse(project.Version.AsVersionString(), out var csproj)
            && NuGetVersion.TryParse(latest.AsVersionString(), out var found)
            && csproj.Version > found.Version;
        return csprojAhead ? (project.Version, true) : (latest, false);
    }

    /// <summary>
    /// Whether the csproj sets <c>PackageVersion</c> itself: its body is evaluated after the props giving the planned
    /// version, so it wins. A default kept for an empty <c>PackageVersion</c> (condition on it) doesn't.
    /// </summary>
    internal static bool OverridesPackageVersion(string pathToCsproj)
    {
        if (!File.Exists(pathToCsproj))
            return false;
        try
        {
            var doc = XDocument.Load(pathToCsproj);
            var ns = doc.Root!.Name.Namespace;
            return doc.Root.Elements(ns + "PropertyGroup").Elements(ns + "PackageVersion")
                .Any(elem => elem.Attribute("Condition")?.Value.Contains("$(PackageVersion)", StringComparison.OrdinalIgnoreCase) != true);
        }
        catch (Exception ex)
        {
            logger.LogDebug("Could not read {Csproj} to check for <PackageVersion>: {Error}", pathToCsproj, ex.Message);
            return false;
        }
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
                logger.LogWarning(DiagnosticEvents.PackagesFolderUnknown, "Could not find the NuGet global packages folder for {Folder}, earlier iterations cached there are not counted: {Error}", projectFolder, ex.Message);
            }
        }
        return [.. folders.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
