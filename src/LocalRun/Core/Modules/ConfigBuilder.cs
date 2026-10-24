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
        // TODO if git repo and stuff
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
        var addedForServices = graph.Retain(options.RetainDependsOn, options.RetainIsDependencyOf, options.Union, options.WithServices);
        if (options.WithServices)
        {
            logger.LogInformation("[⚙ ] --with-services added: {Projects}",
                addedForServices.Count == 0 ? "nothing (no other service uses the kept projects)" : string.Join(", ", addedForServices));
        }
        if (options.RetainDependsOn != null && options.RetainIsDependencyOf != null && !options.Union && !graph.AllProjects.Any())
        {
            logger.LogWarning("No project is both a user of {UsersOf} and needed by {DepsOf}: the latter doesn't depend on the former. Use --union, or one flag alone.",
                string.Join(", ", options.RetainDependsOn), string.Join(", ", options.RetainIsDependencyOf));
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
