namespace LocalRun.Tests.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Options;

public class ConfigBuilderTests
{
    private static DependencyGraph Graph(params string[] names) => DependencyGraph.FromConfig(new LocalRunConfig
    {
        Projects = [.. names.Select(name => new ProjectNode
        {
            Name = name,
            Path = $"{name}/{name}.csproj",
            Type = "library",
            Version = ProjectVersion.Parse("1.0.0"),
            Dependencies = [],
        })],
    });

    private static BuildConfigOptions Options(params string[] argv) => BuildConfigOptionsParser.Parse(new CommandLineArgs(argv));

    [Fact]
    public void UnknownFilterName_IsAUsageError_NamingTheFlag()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ConfigBuilder.ValidateFilterNames(Graph("Core", "Api"), Options("--users-of", "Core", "--deps-of", "Typo")));

        Assert.Contains("--deps-of", ex.Message);
        Assert.Contains("Typo", ex.Message);
    }

    [Fact]
    public void CollidedFilterName_IsAUsageError_SayingItIsAmbiguous()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ConfigBuilder.ValidateFilterNames(Graph("Core", "Shared", "Shared"), Options("--users-of", "shared")));

        Assert.Contains("Ambiguous", ex.Message);
        Assert.Contains("--users-of", ex.Message);
    }

    [Fact]
    public void KnownFilterNames_PassInAnyCase()
    {
        ConfigBuilder.ValidateFilterNames(Graph("Core", "Api"), Options("--users-of", "core", "--deps-of", "API"));
    }
}
