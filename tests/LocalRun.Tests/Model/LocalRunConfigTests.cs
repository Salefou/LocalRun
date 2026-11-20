namespace LocalRun.Tests.Model;

using LocalRun.Core.Model;
using System.Text.Json;

public class LocalRunConfigTests
{
    [Fact]
    public void ConfigWithTheRemovedSkipFields_StillLoads()
    {
        // SkipRepack / SkipRun were removed on 2026-10-24: configs written before must still be readable
        var config = JsonSerializer.Deserialize<LocalRunConfig>("""
            {"Projects":[{"Name":"A","Path":"A/A.csproj","Type":"executable","Version":{"Major":1,"Minor":0,"PatchAndSuffix":"0"},
             "Dependencies":[],"SkipRepack":true,"SkipRun":true}]}
            """);

        Assert.Equal("A", Assert.Single(config!.Projects).Name);
    }

    [Fact]
    public void SchemaVersion_IsWrittenFirst_AndReadsAs1WhenAbsent()
    {
        Assert.StartsWith("""{"SchemaVersion":1,""", JsonSerializer.Serialize(new LocalRunConfig()));
        Assert.StartsWith("""{"SchemaVersion":1,""",
            JsonSerializer.Serialize(new RepackConfig { LocalNugetPath = "feed", RepackTrees = [], Clean = false }));

        // Configs written before the field existed
        Assert.Equal(1, JsonSerializer.Deserialize<LocalRunConfig>("""{"Projects":[]}""")!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<RepackConfig>("""{"LocalNugetPath":"feed","RepackTrees":[],"Clean":false}""")!.SchemaVersion);
    }

    [Fact]
    public void TheBuildRecord_RoundTrips_AndIsAbsentFromOlderConfigs()
    {
        var config = new LocalRunConfig
        {
            RootPath = "root",
            Build = new BuildRecord
            {
                Time = new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.FromHours(1)),
                LrunVersion = "0.6.0",
                Filters = "--users-of A",
                UsersOf = ["A"],
                Tests = true,
                LeftOut = [new("name-collision", ["Dup"]) { Paths = ["One/Dup.csproj", "Two/Dup.csproj"], Dependents = ["B"] }],
                Repositories = [new(".", "1a2b3c", "main")],
            },
        };

        var read = JsonSerializer.Deserialize<LocalRunConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal(config.Build.Time, read.Build!.Time);
        Assert.Equal(["A"], read.Build.UsersOf);
        Assert.True(read.Build.Tests);
        var collision = Assert.Single(read.Build.LeftOut);
        Assert.Equal("name-collision", collision.Reason);
        Assert.Equal(["Dup"], collision.Projects);
        Assert.Equal(["One/Dup.csproj", "Two/Dup.csproj"], collision.Paths);
        Assert.Equal(new RepositoryHead(".", "1a2b3c", "main"), Assert.Single(read.Build.Repositories));
        Assert.Null(JsonSerializer.Deserialize<LocalRunConfig>("""{"Projects":[]}""")!.Build);
        Assert.DoesNotContain("\"Build\"", JsonSerializer.Serialize(new LocalRunConfig()));
    }

    [Fact]
    public void Test_IsWrittenOnlyForTestProjects()
    {
        var project = new ProjectNode { Name = "A", Path = "A/A.csproj", Type = "library", Version = ProjectVersion.Parse("1.0.0"), Dependencies = [] };
        Assert.DoesNotContain("\"Test\"", JsonSerializer.Serialize(project));

        var test = new ProjectNode { Name = "A.Tests", Path = "A.Tests/A.Tests.csproj", Type = "library", Test = true, Version = ProjectVersion.Parse("1.0.0"), Dependencies = [] };
        Assert.True(JsonSerializer.Deserialize<ProjectNode>(JsonSerializer.Serialize(test))!.Test);
    }

    [Fact]
    public void LatestVersion_IsWrittenOnlyWhenKnown()
    {
        var project = new ProjectNode { Name = "A", Path = "A/A.csproj", Type = "library", Version = ProjectVersion.Parse("1.0.0"), Dependencies = [] };
        Assert.DoesNotContain("LatestVersion", JsonSerializer.Serialize(project));

        project.LatestVersion = ProjectVersion.Parse("1.2.0-beta");
        var roundTripped = JsonSerializer.Deserialize<ProjectNode>(JsonSerializer.Serialize(project));
        Assert.Equal(ProjectVersion.Parse("1.2.0-beta"), roundTripped!.LatestVersion);
    }
}
