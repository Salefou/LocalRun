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

        if (options.RetainDependsOn != null)
            graph.RetainDependsOn(options.RetainDependsOn);
        if (options.RetainIsDependencyOf != null)
            graph.RetainIsDependencyOf(options.RetainIsDependencyOf);

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
}
