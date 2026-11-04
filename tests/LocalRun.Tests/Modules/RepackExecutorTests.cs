namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using System.Xml.Linq;

public class RepackExecutorTests
{
    private static ProjectRepack Repack(string name, string path, string toVersion, int depth = 0) => new()
    {
        Name = name,
        PathToCsproj = path,
        Depth = depth,
        FromVersion = ProjectVersion.Parse("1.0.0"),
        ToVersion = ProjectVersion.Parse(toVersion),
    };

    [Fact]
    public void Depths_GatherEveryTree_InDepthOrder()
    {
        var config = new RepackConfig
        {
            LocalNugetPath = "feed",
            Clean = false,
            RepackTrees =
            [
                new() { Projects = [Repack("Core", "Core.csproj", "1.0.1", 0), Repack("Api", "Api.csproj", "1.0.1", 2)] },
                new() { Projects = [Repack("Island", "Island.csproj", "1.0.1", 0), Repack("Bridge", "Bridge.csproj", "1.0.1", 1)] },
            ],
        };

        var depths = RepackExecutor.Depths(config);

        Assert.Equal([["Core", "Island"], ["Bridge"], ["Api"]], depths.Select(depth => depth.Select(proj => proj.Name)));
    }

    [Fact]
    public void VersionsProps_GivesEachProjectItsOwnPlannedVersion()
    {
        string root = Path.GetFullPath("repos");
        var config = new RepackConfig
        {
            LocalNugetPath = "feed",
            Clean = false,
            RepackTrees =
            [
                new() { Projects = [Repack("Core", Path.Combine(root, "Core", "Core.csproj"), "1.0.1-dev"), Repack("Api", Path.Combine(root, "Api", "Api.csproj"), "2.3.2-dev")] },
                new() { Projects = [Repack("Island", Path.Combine(root, "Island", "Island.csproj"), "0.1.1-dev")] },
            ],
        };

        var groups = XDocument.Parse(RepackExecutor.BuildVersionsProps(config)).Root!.Elements("PropertyGroup").ToList();

        Assert.Equal(3, groups.Count);
        Assert.Equal($"'$(MSBuildProjectFullPath)' == '{Path.Combine(root, "Api", "Api.csproj")}'", groups[1].Attribute("Condition")!.Value);
        Assert.Equal(["1.0.1-dev", "2.3.2-dev", "0.1.1-dev"], groups.Select(group => group.Element("PackageVersion")!.Value));
    }

    [Fact]
    public void VersionsProps_EscapesXmlCharactersInPaths()
    {
        string path = Path.GetFullPath(Path.Combine("R&D <repos>", "Lib", "Lib.csproj"));
        var config = new RepackConfig { LocalNugetPath = "feed", Clean = false, RepackTrees = [new() { Projects = [Repack("Lib", path, "1.0.0")] }] };

        var group = XDocument.Parse(RepackExecutor.BuildVersionsProps(config)).Root!.Element("PropertyGroup")!;

        Assert.Equal($"'$(MSBuildProjectFullPath)' == '{path}'", group.Attribute("Condition")!.Value);
    }
}
