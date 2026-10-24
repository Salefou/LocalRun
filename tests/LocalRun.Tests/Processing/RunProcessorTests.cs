namespace LocalRun.Tests.Processing;

using LocalRun.Core.Model;
using LocalRun.Core.Processing;
using LocalRun.Options;

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

    private static LocalRunConfig Config() => new()
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

    [Fact]
    public void PlanRun_KeepsExecutablesInDependencyOrder_WithoutSkipRun()
    {
        Assert.Equal(["Batch", "Risk", "Gateway"], RunProcessor.PlanRun(Config()).Select(p => p.Name));
    }

    [Fact]
    public void PlanRun_LeavesOutExcludedExecutables_InAnyCase()
    {
        Assert.Equal(["Batch", "Gateway"], RunProcessor.PlanRun(Config(), ["risk"]).Select(p => p.Name));
    }

    [Fact]
    public void PlanRun_AcceptsExcludingASkipRunExecutable()
    {
        Assert.Equal(["Batch", "Risk", "Gateway"], RunProcessor.PlanRun(Config(), ["Reports"]).Select(p => p.Name));
    }

    [Theory]
    [InlineData("Pricing")] // a library
    [InlineData("Nope")]
    public void PlanRun_RejectsExcludingWhatIsNotAnExecutable(string name)
    {
        var ex = Assert.Throws<UsageException>(() => RunProcessor.PlanRun(Config(), [name]));

        Assert.Contains($"not an executable of this config: {name}.", ex.Message);
        Assert.Contains("Executables: Batch, Reports, Risk, Gateway.", ex.Message);
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
