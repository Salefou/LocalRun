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
        Assert.Equal(["1.0.1-dev", "2.3.2-dev", "0.1.1-dev"], groups.Select(group => group.Element("MinVerVersionOverride")!.Value));
    }

    [Fact]
    public void MoveToFeed_MovesEveryFile_AndReadsTheVersionFromThePackage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lrun-test-{Guid.NewGuid():N}");
        string output = Path.Combine(root, "out");
        string feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(feed);
        try
        {
            // The file name says what was planned; the nuspec, what was packed: the nuspec wins
            WritePackage(Path.Combine(output, "Lib.1.0.1-dev.1.nupkg"), "Lib", "1.0.1-alpha.0.38");
            File.WriteAllText(Path.Combine(output, "Lib.1.0.1-dev.1.snupkg"), "symbols");

            var version = RepackExecutor.MoveToFeed(output, feed);

            Assert.Equal("1.0.1-alpha.0.38", version!.ToNormalizedString());
            Assert.Empty(Directory.EnumerateFiles(output));
            Assert.Equal(["Lib.1.0.1-dev.1.nupkg", "Lib.1.0.1-dev.1.snupkg"],
                Directory.EnumerateFiles(feed).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MoveToFeed_NoPackageProduced_GivesNull()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"lrun-test-{Guid.NewGuid():N}");

        Assert.Null(RepackExecutor.MoveToFeed(missing, missing));
    }

    [Theory]
    [InlineData("1.0.1-dev.1", "1.0.1-dev.1", null)]
    [InlineData("1.0.1-dev.1", "1.0.1-dev.1+abc123", null)]
    [InlineData("1.0", "1.0.0", null)]
    [InlineData("1.21.1-dev.1", "1.21.1-alpha.0.38", "1.21.1-alpha.0.38")]
    [InlineData("1.21.1-dev.1", "1.0.0", "1.0.0")]
    public void PackedVersionIfOther_IgnoresMetadataAndShortForms(string planned, string packed, string? expected)
    {
        var proj = Repack("Lib", "Lib.csproj", planned);

        Assert.Equal(expected, RepackExecutor.PackedVersionIfOther(proj, NuGet.Versioning.NuGetVersion.Parse(packed)));
    }

    private static void WritePackage(string path, string id, string version)
    {
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry($"{id}.nuspec").Open());
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>{id}</id><version>{version}</version><authors>a</authors><description>d</description></metadata>
            </package>
            """);
    }

    private static ProjectRepack WithDependencies(string name, int depth, params string[] dependsOn) => new()
    {
        Name = name,
        PathToCsproj = $"{name}.csproj",
        Depth = depth,
        DependsOn = [.. dependsOn],
        FromVersion = ProjectVersion.Parse("1.0.0"),
        ToVersion = ProjectVersion.Parse("1.0.1"),
    };

    [Fact]
    public void SkippedFor_NamesTheFailedProjectsBehind_TransitivelyAndOnce()
    {
        // Core and Island failed, Mid was skipped for Core
        var failedBehind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Core"] = ["Core"],
            ["Island"] = ["Island"],
            ["Mid"] = ["Core"],
        };

        Assert.Equal(["Core", "Island"], RepackExecutor.SkippedFor(WithDependencies("Top", 2, "mid", "Island", "Core"), failedBehind));
        Assert.Null(RepackExecutor.SkippedFor(WithDependencies("Other", 1, "Lib"), failedBehind));
        Assert.Null(RepackExecutor.SkippedFor(WithDependencies("Root", 0), failedBehind));
    }

    [Fact]
    public void SkippedFor_APlanWithoutDependencies_SkipsEverythingAfterAFailure()
    {
        var failedBehind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Core"] = ["Core"] };
        var unrelated = Repack("Unrelated", "Unrelated.csproj", "1.0.1", 1);

        Assert.Equal(["Core"], RepackExecutor.SkippedFor(unrelated, failedBehind));
        Assert.Null(RepackExecutor.SkippedFor(unrelated, new Dictionary<string, IReadOnlyList<string>>()));
    }

    [Fact]
    public void FailureMessage_NamesTheFailedAndSkipped_ThenTheHint()
    {
        var plan = new[] { WithDependencies("Core", 0), WithDependencies("Side", 0), WithDependencies("Mid", 1, "Core"), WithDependencies("Top", 2, "Mid") };
        var outcomes = new Dictionary<string, PackOutcome>
        {
            ["Core"] = new() { Status = PackStatus.Failed, Failure = DotnetFailure.Of("restore", 1, []) },
            ["Side"] = new() { Status = PackStatus.Packed },
            ["Mid"] = new() { Status = PackStatus.Skipped, SkippedFor = ["Core"] },
            ["Top"] = new() { Status = PackStatus.Skipped, SkippedFor = ["Core"] },
        };

        Assert.Equal("dotnet failed on Core (restore); skipped, as they depend on it: Mid, Top. Exclude Core",
            RepackExecutor.FailureMessage(plan, outcomes, failed => $"Exclude {string.Join(' ', failed)}"));
    }

    [Fact]
    public void FailureMessage_NoHint_WhenEveryProjectFailed()
    {
        var plan = new[] { WithDependencies("Core", 0) };
        var outcomes = new Dictionary<string, PackOutcome>
        {
            ["Core"] = new() { Status = PackStatus.Failed, Failure = DotnetFailure.Of("pack", 1, []) },
        };

        Assert.Equal("dotnet failed on Core (pack).", RepackExecutor.FailureMessage(plan, outcomes, _ => "Exclude"));
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
