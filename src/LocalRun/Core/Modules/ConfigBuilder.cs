namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System;

public class ConfigBuilder
{
    private static readonly ILogger<ConfigBuilder> logger = Log.For<ConfigBuilder>();

    public LocalRunConfig BuildConfig(string rootPath, BuildConfigOptions options, string localNugetRepoPath)
    {
        logger.LogInformation("[⚙ ] Building dependency graph from {RootPath}...", rootPath);

        string[] csprojs = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
        var graph = DependencyGraph.FromSource(rootPath, csprojs, options.IncludeExternal);
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
    /// <c>--remote</c>: records each project's highest version across the NuGet sources that apply to its folder,
    /// except the local feed, which only holds our own builds: starting from those would bump an iteration again.
    /// </summary>
    public async Task FetchRemoteVersionsAsync(LocalRunConfig config)
    {
        logger.LogInformation("[🌐] Fetching the versions on your NuGet sources...");
        var nugetVersionChecker = new NugetVersionChecker();
        foreach (var proj in config.Projects)
        {
            string projectFolder = Path.GetDirectoryName(Path.Combine(config.RootPath, proj.Path))!;
            var remote = await nugetVersionChecker.GetLatestVersionAcrossSourcesAsync(proj.Name, projectFolder, config.LocalNugetPath);
            if (remote == null)
            {
                logger.LogInformation("  {Project}: not found, pack will start from the csproj version {Version}", proj.Name, proj.Version);
                continue;
            }
            // Normalized keeps the prerelease label (1.4.2-beta); .Version would give 1.4.2.0
            proj.RemoteVersion = ProjectVersion.Parse(remote.ToNormalizedString());
            logger.LogInformation("  {Project}: {RemoteVersion} (csproj {Version})", proj.Name, proj.RemoteVersion, proj.Version);
        }
    }

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
                throw new UsageException($"Unknown project(s) for '{flag}': {string.Join(", ", unknown)}.");
        }
    }
}
