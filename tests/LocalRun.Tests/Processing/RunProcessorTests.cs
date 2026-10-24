namespace LocalRun.Tests.Processing;

using LocalRun.Core.Model;
using LocalRun.Core.Processing;

public class RunProcessorTests
{
    private static ProjectNode Project(string name, string type, bool skipRun = false, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = type,
        Version = ProjectVersion.Parse("1.0.0"),
        Dependencies = [.. dependencies],
        SkipRun = skipRun,
    };

    [Fact]
    public void PlanRun_KeepsExecutablesInDependencyOrder_WithoutSkipRun()
    {
        var config = new LocalRunConfig
        {
            Projects =
            [
                Project("Gateway", "executable", dependencies: ["Pricing", "Risk"]),
                Project("Pricing", "library"),
                Project("Risk", "executable", dependencies: ["Pricing"]),
                Project("Reports", "executable", skipRun: true, dependencies: ["Pricing"]),
                Project("Batch", "executable"),
            ],
        };

        Assert.Equal(["Batch", "Risk", "Gateway"], RunProcessor.PlanRun(config).Select(p => p.Name));
    }

    [Fact]
    public void ExistingLocalFeed_IsAddedToTheRestoreSources()
    {
        string feed = Directory.CreateTempSubdirectory("localrun-feed-").FullName;
        try
        {
            Assert.Equal($" -p:RestoreAdditionalProjectSources=\"{feed}\"",
                RunProcessor.LocalFeedRestoreArgument(new LocalRunConfig { LocalNugetPath = feed }));
        }
        finally
        {
            Directory.Delete(feed);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("does-not-exist-feed")]
    public void MissingLocalFeed_IsNotAdded(string feed)
    {
        Assert.Equal(string.Empty, RunProcessor.LocalFeedRestoreArgument(new LocalRunConfig { LocalNugetPath = feed }));
    }
}
