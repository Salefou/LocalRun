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
    public void LatestVersion_IsWrittenOnlyWhenKnown()
    {
        var project = new ProjectNode { Name = "A", Path = "A/A.csproj", Type = "library", Version = ProjectVersion.Parse("1.0.0"), Dependencies = [] };
        Assert.DoesNotContain("LatestVersion", JsonSerializer.Serialize(project));

        project.LatestVersion = ProjectVersion.Parse("1.2.0-beta");
        var roundTripped = JsonSerializer.Deserialize<ProjectNode>(JsonSerializer.Serialize(project));
        Assert.Equal(ProjectVersion.Parse("1.2.0-beta"), roundTripped!.LatestVersion);
    }
}
