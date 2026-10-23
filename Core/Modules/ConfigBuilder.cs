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

        var config = new LocalRunConfig
        {
            RootPath = rootPath,
            LocalNugetPath = localNugetRepoPath,
            Projects = [.. graph.TopologicalOrder()]
        };
        return config;
    }
}
