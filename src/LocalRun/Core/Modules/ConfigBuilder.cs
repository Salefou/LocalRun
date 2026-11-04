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
        logger.LogInformation(LogEvents.Progress, "Scanning {RootPath}...", rootPath);

        string[] csprojs = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
        var graph = DependencyGraph.FromSource(rootPath, csprojs, options.IncludeExternal, options.IncludeTests,
            evaluate: options.CsprojVersionsOnly ? null : MsBuildEvaluator.Evaluate);
        var scanned = graph.AllProjects.ToList();
        int executables = scanned.Count(proj => proj.Type == "executable");
        logger.LogInformation("Scanned {RootPath}: {Projects} in {Solutions}, {Libraries} and {Executables}", rootPath,
            Text.Count(scanned.Count, "project"),
            Text.Count(scanned.Select(proj => proj.SolutionPath).Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "solution"),
            Text.Count(scanned.Count - executables, "library", "libraries"), Text.Count(executables, "executable"));
        if (graph.LeftOutTests.Count != 0)
        {
            logger.LogInformation("Left out {Projects} ('--tests' keeps them): {Names}",
                Text.Count(graph.LeftOutTests.Count, "test project"), Text.Abbreviate(graph.LeftOutTests));
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
            logger.LogInformation("--with-services added: {Projects}",
                addedForServices.Count == 0 ? "nothing (no other service uses the kept projects)" : Text.Abbreviate(addedForServices));
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
        if (Filters(options) is { } filters)
            logger.LogInformation("Kept {Kept} of {Scanned} ({Filters})", config.Projects.Count, scanned.Count, filters);
        return config;
    }

    /// <summary>The filters given to <c>build</c>, as typed (<c>--deps-of WebApp, --with-services</c>), or null without any.</summary>
    internal static string? Filters(BuildConfigOptions options)
    {
        List<string> filters = [];
        if (options.RetainDependsOn != null) filters.Add($"--users-of {string.Join(" ", options.RetainDependsOn)}");
        if (options.RetainGitChanges) filters.Add("--git-changes");
        if (options.RetainIsDependencyOf != null) filters.Add($"--deps-of {string.Join(" ", options.RetainIsDependencyOf)}");
        if (options.Union) filters.Add("--union");
        if (options.WithServices) filters.Add("--with-services");
        return filters.Count == 0 ? null : string.Join(", ", filters);
    }

    /// <summary>
    /// Records each project's highest version across the NuGet sources that apply to its folder, as a restore from
    /// there would see them: the local feed included when a NuGet.Config lists it, so your own builds count.
    /// Skipped with <c>--csproj</c>.
    /// </summary>
    public async Task FetchLatestVersionsAsync(LocalRunConfig config)
    {
        logger.LogInformation(LogEvents.Progress, "Querying your NuGet sources and {LocalNugetPath} for the latest versions ('--csproj' skips this)...", config.LocalNugetPath);
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
                logger.LogDebug("{Project}: not found, pack will start from the csproj version {Version}", project, proj.Version);
                continue;
            }
            // Normalized keeps the prerelease label (1.4.2-beta); .Version would give 1.4.2.0
            proj.LatestVersion = ProjectVersion.Parse(latest.ToNormalizedString());
            logger.LogDebug("{Project}: {LatestVersion} (csproj {Version})", project, proj.LatestVersion, proj.Version);
        }
        int found = config.Projects.Count(proj => proj.LatestVersion != null);
        if (config.Projects.Count == 0)
            return;
        if (found == 0)
            logger.LogInformation("No project found on your sources: pack starts from the csproj versions");
        else if (found == config.Projects.Count)
            logger.LogInformation("Latest versions found for all {Projects}", Text.Count(found, "project"));
        else
            logger.LogInformation("Latest versions found for {Found} of {Count} projects, the others start from their csproj version", found, config.Projects.Count);
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
        // The changed projects of each repository are listed by GitChangeDetector
        if (changed.Count == 0)
            logger.LogWarning("--git-changes: no pending changes under {RootPath}, nothing to keep", rootPath);
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
