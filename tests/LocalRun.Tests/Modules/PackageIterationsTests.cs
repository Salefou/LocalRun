namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using NuGet.Versioning;

public sealed class PackageIterationsTests : IDisposable
{
    private readonly string feed = Directory.CreateTempSubdirectory("localrun-feed-").FullName;
    private readonly string cache = Directory.CreateTempSubdirectory("localrun-cache-").FullName;

    public void Dispose()
    {
        Directory.Delete(feed, recursive: true);
        Directory.Delete(cache, recursive: true);
    }

    private void Packed(string fileName) => File.WriteAllText(Path.Combine(feed, fileName), "");

    private void Cached(string packageId, string version) => Directory.CreateDirectory(Path.Combine(cache, packageId.ToLowerInvariant(), version));

    private int Next(params (string PackageId, string Target)[] plan)
        => PackageIterations.Next(plan.Select(p => (p.PackageId, ProjectVersion.Parse(p.Target))), [feed, cache, Path.Combine(feed, "missing")]);

    [Fact]
    public void NothingPacked_StartsAtOne()
    {
        Assert.Equal(1, Next(("Lib", "1.5.0-alpha")));
    }

    [Fact]
    public void FollowsTheHighestIteration_InTheFeedAndInTheCache()
    {
        Packed("Lib.1.5.0-alpha.1.nupkg");
        Packed("Lib.1.5.0-alpha.2.nupkg");
        Cached("Lib", "1.5.0-alpha.10");

        Assert.Equal(11, Next(("Lib", "1.5.0-alpha")));
    }

    [Fact]
    public void IsSharedByThePlan()
    {
        Packed("Lib.1.5.0-alpha.3.nupkg");
        Packed("App.2.1.0-alpha.1.nupkg");

        Assert.Equal(4, Next(("Lib", "1.5.0-alpha"), ("App", "2.1.0-alpha")));
    }

    [Theory]
    [InlineData("Lib.1.5.1-alpha.7.nupkg")]       // another version
    [InlineData("Lib.1.5.0-beta.7.nupkg")]        // another label
    [InlineData("Lib.1.5.0-alpha.x.7.nupkg")]     // a longer label
    [InlineData("Lib.1.5.0-alpha.nupkg")]         // no iteration
    [InlineData("Lib.Extra.1.5.0-alpha.7.nupkg")] // another package sharing the prefix
    [InlineData("Other.1.5.0-alpha.7.nupkg")]
    public void OtherVersions_AreIgnored(string fileName)
    {
        Packed(fileName);

        Assert.Equal(1, Next(("Lib", "1.5.0-alpha")));
    }

    [Theory]
    [InlineData("1.4.2.7-dev.3", "1.4.2.7-dev", 3)]
    [InlineData("1.5.0-ALPHA.2", "1.5.0-alpha", 2)]
    [InlineData("1.5.0-beta.2.4", "1.5.0-beta.2", 4)]
    [InlineData("1.4.2-dev.3", "1.4.2.7-dev", null)]
    [InlineData("1.5.0-alpha.-1", "1.5.0-alpha", null)]
    public void IterationOf_MatchesTheTargetFollowedByANumber(string packed, string target, int? expected)
    {
        Assert.Equal(expected, PackageIterations.IterationOf(NuGetVersion.Parse(packed), NuGetVersion.Parse(target)));
    }
}
