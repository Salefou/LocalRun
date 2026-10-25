namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Options;

public sealed class RepackConfigBuilderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("localrun-repack-").FullName;

    private string Feed => Path.Combine(root, "feed");

    private string Cache => Path.Combine(root, "cache");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static ProjectNode Project(string name, string version, string? latestVersion = null, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = "library",
        Version = ProjectVersion.Parse(version),
        LatestVersion = latestVersion == null ? null : ProjectVersion.Parse(latestVersion),
        Dependencies = [.. dependencies],
    };

    private async Task<Dictionary<string, ProjectRepack>> Plan(BumpOptions bump, params ProjectNode[] projects)
    {
        var config = new LocalRunConfig { RootPath = root, LocalNugetPath = Feed, Projects = [.. projects] };
        var repackConfig = await new RepackConfigBuilder(_ => Cache)
            .BuildRepackConfig(config, new RepackOptions { BumpOptions = bump, CleanBeforeRestore = false });
        return repackConfig.RepackTrees.SelectMany(tree => tree.Projects).ToDictionary(proj => proj.Name);
    }

    [Fact]
    public async Task LatestVersion_IsTheBase_WhenRecorded()
    {
        var plan = await Plan(new BumpOptions { BumpType = BumpType.Minor, Suffix = "alpha" },
            Project("Lib", "1.4.2", latestVersion: "1.5.0"),
            Project("New", "0.1.0"));

        Assert.Equal("1.5.0", plan["Lib"].FromVersion.AsVersionString());
        Assert.Equal("1.6.0-alpha.1", plan["Lib"].ToVersion.AsVersionString());
        Assert.Equal("0.2.0-alpha.1", plan["New"].ToVersion.AsVersionString());
    }

    [Fact]
    public async Task PackingAgain_TakesTheNextIteration_ForTheWholePlan()
    {
        Directory.CreateDirectory(Feed);
        File.WriteAllText(Path.Combine(Feed, "Lib.1.5.0-alpha.1.nupkg"), "");
        Directory.CreateDirectory(Path.Combine(Cache, "app", "2.1.0-alpha.2"));

        var plan = await Plan(new BumpOptions { BumpType = BumpType.Minor, Suffix = "alpha" },
            Project("Lib", "1.4.2"),
            Project("App", "2.0.3", null, "Lib"));

        Assert.Equal("1.5.0-alpha.3", plan["Lib"].ToVersion.AsVersionString());
        Assert.Equal("2.1.0-alpha.3", plan["App"].ToVersion.AsVersionString());
    }

    [Fact]
    public async Task Alpha_ContinuesTheLatestAlpha_AndStartsOneAfterARelease()
    {
        // Day 2: build found your alpha.9 for Lib; Logging joins the plan with its last release
        var plan = await Plan(new BumpOptions { BumpType = BumpType.Next, Suffix = "alpha" },
            Project("Lib", "1.4.2", latestVersion: "1.5.0-alpha.9"),
            Project("Logging", "2.3.1", latestVersion: "2.3.1"));

        Assert.Equal("1.5.0-alpha.10", plan["Lib"].ToVersion.AsVersionString());
        Assert.Equal("2.3.2-alpha.10", plan["Logging"].ToVersion.AsVersionString());
    }

    [Fact]
    public async Task NoSuffix_KeepsTheLabel_WithoutIteration()
    {
        // Only reachable from code: the CLI always sets a suffix
        var plan = await Plan(new BumpOptions { BumpType = BumpType.None, Suffix = null }, Project("Lib", "1.4.2-beta+sha"));

        Assert.Equal("1.4.2-beta+sha", plan["Lib"].ToVersion.AsVersionString());
    }
}
