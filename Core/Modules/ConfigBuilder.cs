namespace LocalRun.Core.Modules;

using LocalRun.Contracts;
using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Options;
using System;

public class ConfigBuilder : IConfigBuilder
{
    public LocalRunConfig BuildConfig(string rootPath, BuildConfigOptions options, string localNugetRepoPath)
    {
        Console.WriteLine($"[⚙ ] Building dependency graph from {rootPath}...");

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
