namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;

public class ConfigBuilder
{
    private static readonly ILogger<ConfigBuilder> logger = Log.For<ConfigBuilder>();

    public LocalRunConfig BuildConfig(string rootPath, BuildConfigOptions options, string localNugetRepoPath)
    {
        logger.LogInformation("[⚙ ] Building dependency graph from {RootPath}...", rootPath);

        string[] csprojs = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
        var graph = DependencyGraph.FromSource(rootPath, csprojs, options.IncludeExternal, options.IncludeTests);
        if (graph.LeftOutTests.Count != 0)
        {
            logger.LogInformation("[🧪] Left out {Count} test project(s) ('--tests' keeps them): {Projects}",
                graph.LeftOutTests.Count, string.Join(", ", graph.LeftOutTests));
        }
        foreach (var collision in graph.Collisions)
        {
            logger.LogError("Several projects are named {Name}, all left out of the config: {Paths}",
                collision.Name, string.Join(", ", collision.Paths));
            if (collision.Dependents.Count != 0)
            {
                logger.LogWarning("  Kept without their dependency on it (packing or running them may fail): {Dependents}",
                    string.Join(", ", collision.Dependents));
            }
        }

        ValidateFilterNames(graph, options);
        List<string>? usersOf = options.RetainGitChanges
            ? [.. (options.RetainDependsOn ?? []).Union(FindGitChanges(rootPath, csprojs, graph), StringComparer.OrdinalIgnoreCase)]
            : options.RetainDependsOn;

        var addedForServices = graph.Retain(usersOf, options.RetainIsDependencyOf, options.Union, options.WithServices);
        if (options.WithServices)
        {
            logger.LogInformation("[⚙ ] --with-services added: {Projects}",
                addedForServices.Count == 0 ? "nothing (no other service uses the kept projects)" : string.Join(", ", addedForServices));
        }
        if (usersOf is { Count: > 0 } && options.RetainIsDependencyOf != null && !options.Union && !graph.AllProjects.Any())
        {
            logger.LogWarning("No project is both a user of {UsersOf} and needed by {DepsOf}: the latter doesn't depend on the former. Use --union, or one flag alone.",
                string.Join(", ", usersOf), string.Join(", ", options.RetainIsDependencyOf));
        }

        // After the filters, so they still see the real graph; a cycle cannot be ordered, so it is left out
        foreach (var cycle in graph.RemoveCycles())
        {
            logger.LogWarning("Dependency cycle left out of the config: {Projects}", string.Join(", ", cycle.Projects));
            if (cycle.Dependents.Count != 0)
            {
                logger.LogWarning("  Kept without their dependency on it (packing or running them may fail): {Dependents}",
                    string.Join(", ", cycle.Dependents));
            }
        }

        var config = new LocalRunConfig
        {
            RootPath = rootPath,
            LocalNugetPath = localNugetRepoPath,
            Projects = [.. graph.TopologicalOrder()]
        };
        return config;
    }

    /// <summary>
    /// Records each project's highest version across the NuGet sources that apply to its folder, as a restore from
    /// there would see them: the local feed included when a NuGet.Config lists it, so your own builds count.
    /// Skipped with <c>--csproj</c>.
    /// </summary>
    public async Task FetchLatestVersionsAsync(LocalRunConfig config)
    {
        logger.LogInformation("[🌐] Fetching the latest versions on your NuGet sources and in {LocalNugetPath} ('--csproj' skips this)...", config.LocalNugetPath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var nugetVersionChecker = new NugetVersionChecker();
        // go restores with the local feed as an additional source, whatever the NuGet.Config files say
        string? localFeed = Directory.Exists(config.LocalNugetPath) ? config.LocalNugetPath : null;
        // Several projects at once (each querying its sources at once); logged afterwards, in config order
        var latestPerProject = new NuGetVersion?[config.Projects.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, config.Projects.Count), new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentLookups },
            async (i, _) =>
            {
                var proj = config.Projects[i];
                string projectFolder = Path.GetDirectoryName(Path.Combine(config.RootPath, proj.Path))!;
                latestPerProject[i] = await nugetVersionChecker.GetLatestVersionAcrossSourcesAsync(proj.PackageIdOrName, projectFolder, localFeed);
            });

        for (int i = 0; i < config.Projects.Count; i++)
        {
            var proj = config.Projects[i];
            string project = proj.PackageId == null ? proj.Name : $"{proj.Name} ({proj.PackageId})";
            if (latestPerProject[i] is not { } latest)
            {
                logger.LogInformation("  {Project}: not found, pack will start from the csproj version {Version}", project, proj.Version);
                continue;
            }
            // Normalized keeps the prerelease label (1.4.2-beta); .Version would give 1.4.2.0
            proj.LatestVersion = ProjectVersion.Parse(latest.ToNormalizedString());
            logger.LogInformation("  {Project}: {LatestVersion} (csproj {Version})", project, proj.LatestVersion, proj.Version);
        }
        logger.LogDebug("Fetched the latest versions of {Count} projects in {ElapsedMs}ms", config.Projects.Count, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Projects looked up at once by <see cref="FetchLatestVersionsAsync"/>: enough to hide the latency of remote feeds.</summary>
    private const int MaxConcurrentLookups = 8;

    /// <summary>The projects of the graph with pending git changes, which <c>--git-changes</c> adds to the <c>--users-of</c> names.</summary>
    private static List<string> FindGitChanges(string rootPath, string[] csprojs, DependencyGraph graph)
    {
        // Every scanned project, collided ones included, so their files map to them rather than to the whole repo
        var changed = GitChangeDetector.FindChangedProjects(csprojs.Select(csproj => (Path.GetFileNameWithoutExtension(csproj), csproj)))
            .Where(name => graph.TryGetFromName(name, out _))
            .ToList();
        if (changed.Count == 0)
            logger.LogWarning("--git-changes: no pending changes under {RootPath}, nothing to keep", rootPath);
        else
            logger.LogInformation("[🔀] --git-changes: {Projects} changed, kept with all their users", string.Join(", ", changed));
        return changed;
    }

    /// <summary>A misspelt name would silently filter everything out: reject it, checked against the full graph.</summary>
    internal static void ValidateFilterNames(DependencyGraph graph, BuildConfigOptions options)
    {
        foreach (var (flag, names) in new[] { ("--users-of", options.RetainDependsOn), ("--deps-of", options.RetainIsDependencyOf) })
        {
            if (names == null) continue;
            var collided = graph.FindCollided(names);
            if (collided.Count != 0)
                throw new UsageException($"Ambiguous project(s) for '{flag}': {string.Join(", ", collided)} (several projects share the name, see the error above).");
            var unknown = graph.FindUnknown(names);
            if (unknown.Count != 0)
            {
                bool tests = unknown.Any(name => graph.LeftOutTests.Contains(name, StringComparer.OrdinalIgnoreCase));
                throw new UsageException($"Unknown project(s) for '{flag}': {string.Join(", ", unknown)}."
                    + (tests ? " Test projects are left out unless you give '--tests'." : string.Empty));
            }
        }
    }
}
