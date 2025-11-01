namespace LocalRun.Contracts;

using LocalRun.Core.Model;
using LocalRun.Options;

public interface IConfigBuilder
{
    LocalRunConfig BuildConfig(string rootPath, BuildConfigOptions options, string localNugetRepoPath);
}

public interface IRepacker
{
    Task RepackAndRebuildAsync(LocalRunConfig config, RepackOptions repackOptions);
}