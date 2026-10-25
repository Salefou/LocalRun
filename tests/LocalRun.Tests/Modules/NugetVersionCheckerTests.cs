namespace LocalRun.Tests.Modules;

using LocalRun.Core.Modules;
using System.IO.Compression;

public sealed class NugetVersionCheckerTests : IDisposable
{
    // The project folder's NuGet.Config clears every source, like samples/NuGet.config minus its feeds
    private readonly string root = Directory.CreateTempSubdirectory("localrun-root-").FullName;
    private readonly string feed = Directory.CreateTempSubdirectory("localrun-feed-").FullName;

    public NugetVersionCheckerTests()
    {
        File.WriteAllText(Path.Combine(root, "NuGet.Config"),
            "<configuration><packageSources><clear /></packageSources></configuration>");
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(feed, recursive: true);
    }

    private void Packed(string packageId, string version)
    {
        using var nupkg = ZipFile.Open(Path.Combine(feed, $"{packageId}.{version}.nupkg"), ZipArchiveMode.Create);
        using var writer = new StreamWriter(nupkg.CreateEntry($"{packageId}.nuspec").Open());
        writer.Write($"""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>{packageId}</id><version>{version}</version><authors>a</authors><description>d</description></metadata>
            </package>
            """);
    }

    [Fact]
    public async Task TheLocalFeed_IsQueried_EvenWhenNoNuGetConfigListsIt()
    {
        Packed("Lib", "1.5.0-alpha.9");
        Packed("Lib", "1.5.0-alpha.10");

        var latest = await new NugetVersionChecker().GetLatestVersionAcrossSourcesAsync("Lib", root, feed);

        Assert.Equal("1.5.0-alpha.10", latest?.ToNormalizedString());
    }

    [Fact]
    public async Task WithoutTheLocalFeed_OnlyTheConfiguredSourcesCount()
    {
        Packed("Lib", "1.5.0-alpha.9");

        Assert.Null(await new NugetVersionChecker().GetLatestVersionAcrossSourcesAsync("Lib", root));
    }
}
