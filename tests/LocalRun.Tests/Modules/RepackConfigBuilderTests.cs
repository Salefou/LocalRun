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

    [Theory]
    [InlineData(BumpType.None, "11.0.0-preview", "10.7.0", "11.0.0-preview", "11.0.0-local.1")]   // in development: the csproj, bump or not
    [InlineData(BumpType.Patch, "11.0.0-preview", "10.7.0", "11.0.0-preview", "11.0.1-local.1")]
    [InlineData(BumpType.None, "1.4.2", "1.5.0", "1.5.0", "1.5.0-local.1")]                        // csproj behind: the latest
    [InlineData(BumpType.None, "1.5.0", "1.5.0-alpha.9", "1.5.0-alpha.9", "1.5.0-local.1")]        // same release: the latest, so alpha iterations go on
    public async Task TheBase_IsTheHigherOfTheLatestAndTheCsprojVersion(BumpType bump, string csproj, string latest, string from, string to)
    {
        var plan = await Plan(new BumpOptions { BumpType = bump, Suffix = "local" }, Project("Lib", csproj, latestVersion: latest));

        Assert.Equal(from, plan["Lib"].FromVersion.AsVersionString());
        Assert.Equal(to, plan["Lib"].ToVersion.AsVersionString());
    }

    [Theory]
    [InlineData("11.0.0-preview", "10.7.0", true)]
    [InlineData("10.7.0", "10.7.0", false)]
    [InlineData("10.6.0", "10.7.0", false)]
    [InlineData("10.7.0", "10.7.0-beta", false)]    // the number alone
    [InlineData("3.0.0", null, false)]              // no latest: the csproj, nothing to warn about
    public void BaseVersion_FlagsACsprojAheadOfTheLatest(string csproj, string? latest, bool ahead)
    {
        var (_, csprojAhead) = RepackConfigBuilder.BaseVersion(Project("Lib", csproj, latest));

        Assert.Equal(ahead, csprojAhead);
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

    [Fact]
    public async Task EarlierIterations_AreFoundUnderThePackageId()
    {
        Directory.CreateDirectory(Feed);
        File.WriteAllText(Path.Combine(Feed, "Acme.Lib.1.5.0-alpha.4.nupkg"), "");
        var lib = new ProjectNode
        {
            Name = "Lib",
            PackageId = "Acme.Lib",
            Path = "Lib/Lib.csproj",
            Type = "library",
            Version = ProjectVersion.Parse("1.4.2"),
            Dependencies = [],
        };

        var plan = await Plan(new BumpOptions { BumpType = BumpType.Minor, Suffix = "alpha" }, lib);

        Assert.Equal("1.5.0-alpha.5", plan["Lib"].ToVersion.AsVersionString());
    }

    [Theory]
    [InlineData("<PropertyGroup><PackageVersion>9.9.9</PackageVersion></PropertyGroup>", true)]
    [InlineData("<PropertyGroup><PackageVersion Condition=\"'$(PackageVersion)' == ''\">9.9.9</PackageVersion></PropertyGroup>", false)]
    [InlineData("<PropertyGroup><Version>9.9.9</Version></PropertyGroup>", false)]
    public void OverridesPackageVersion_OnlyWhenTheCsprojSetsIt(string body, bool expected)
    {
        string csproj = Path.Combine(root, "Lib.csproj");
        File.WriteAllText(csproj, $"""<Project Sdk="Microsoft.NET.Sdk">{body}</Project>""");

        Assert.Equal(expected, RepackConfigBuilder.OverridesPackageVersion(csproj));
    }

    [Fact]
    public void OverridesPackageVersion_IsFalse_ForAMissingCsproj()
    {
        Assert.False(RepackConfigBuilder.OverridesPackageVersion(Path.Combine(root, "Gone.csproj")));
    }
}
