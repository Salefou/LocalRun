namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;

public sealed class FeedVerifierTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("localrun-verify-").FullName;
    private string Feed => Path.Combine(root, "feed");

    private static readonly DateTime Packed = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private ProjectNode Project(string name, string version = "1.0.0", string type = "library", string? packageId = null, string? folder = null)
    {
        folder ??= name;
        Directory.CreateDirectory(Path.Combine(root, "src", folder));
        string csproj = Path.Combine(root, "src", folder, $"{name}.csproj");
        File.WriteAllText(csproj, "<Project />");
        File.SetLastWriteTimeUtc(csproj, Packed.AddDays(-1));
        return new ProjectNode
        {
            Name = name,
            PackageId = packageId,
            Path = Path.Combine("src", folder, $"{name}.csproj"),
            Type = type,
            Version = ProjectVersion.Parse(version),
            Dependencies = [],
        };
    }

    private void Pack(string packageId, string version, DateTime? at = null)
    {
        Directory.CreateDirectory(Feed);
        string nupkg = Path.Combine(Feed, $"{packageId}.{version}.nupkg");
        File.WriteAllText(nupkg, "");
        File.SetLastWriteTimeUtc(nupkg, at ?? Packed);
    }

    private void Edit(string relativePath, DateTime at)
    {
        string path = Path.Combine(root, "src", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, at);
    }

    private List<PackageCheck> Verify(params ProjectNode[] projects) =>
        FeedVerifier.Verify(new LocalRunConfig { RootPath = root, LocalNugetPath = Feed, Projects = [.. projects] });

    [Fact]
    public void NoFeed_EveryLibraryIsMissing_ExecutablesAreLeftOut()
    {
        var checks = Verify(Project("Lib"), Project("App", type: "executable"));

        var check = Assert.Single(checks);
        Assert.Equal(("Lib", FeedVerifier.Missing), (check.Project, check.Status));
        Assert.Null(check.FeedVersion);
    }

    [Fact]
    public void APackageWrittenAfterTheFiles_IsOk()
    {
        var lib = Project("Lib");
        Edit("Lib/Class.cs", Packed.AddHours(-1));
        Pack("Lib", "1.0.1-local.1");

        var check = Assert.Single(Verify(lib));

        Assert.Equal(FeedVerifier.Ok, check.Status);
        Assert.Equal("1.0.1-local.1", check.FeedVersion);
        Assert.Equal(Packed, check.PackedAt);
    }

    [Fact]
    public void AFileChangedAfterTheNewestPackage_IsStale_AndNamed()
    {
        var lib = Project("Lib");
        Pack("Lib", "1.0.1-local.1", Packed.AddDays(-2));
        Pack("Lib", "1.0.1-local.2");
        Edit("Lib/Sub/Class.cs", Packed.AddMinutes(5));

        var check = Assert.Single(Verify(lib));

        Assert.Equal((FeedVerifier.Stale, "edited"), (check.Status, check.Reason));
        Assert.Equal("1.0.1-local.2", check.FeedVersion);
        Assert.Equal(Path.Combine("Sub", "Class.cs"), check.EditedFile);
        Assert.Equal(Packed.AddMinutes(5), check.EditedAt);
    }

    [Theory]
    [InlineData("Lib/bin/Debug/Lib.dll")]
    [InlineData("Lib/obj/project.assets.json")]
    [InlineData("Lib/.vs/state")]
    [InlineData("Lib/Nested/Nested.cs")]
    public void BuildOutputs_DotFolders_AndNestedProjects_DoNotCount(string file)
    {
        var lib = Project("Lib");
        var nested = Project("Nested", type: "executable", folder: Path.Combine("Lib", "Nested"));
        Pack("Lib", "1.0.0-local.1");
        Edit(file, Packed.AddHours(1));

        var check = Assert.Single(Verify(lib, nested));

        Assert.Equal(FeedVerifier.Ok, check.Status);
    }

    [Theory]
    [InlineData("1.5.0", "1.4.3-local.2", true)]
    [InlineData("11.0.0-preview", "10.7.0-local.1", true)]
    [InlineData("1.5.0", "1.5.0-alpha.9", false)]
    [InlineData("1.4.2", "1.4.3-local.1", false)]
    public void ACsprojVersionAheadOfTheFeed_IsStale_ByTheNumberAlone(string csproj, string packed, bool stale)
    {
        var lib = Project("Lib", csproj);
        Pack("Lib", packed);

        var check = Assert.Single(Verify(lib));

        Assert.Equal(stale ? (FeedVerifier.Stale, "csproj-ahead") : (FeedVerifier.Ok, null), (check.Status, check.Reason));
    }

    [Fact]
    public void ThePackageId_IsWhatTheFeedIsSearchedFor_InEitherLayout()
    {
        var flat = Project("Flat", packageId: "Acme.Flat");
        var tree = Project("Tree", packageId: "Acme.Tree");
        Pack("Acme.Flat", "1.0.0-local.1");
        Pack("Flat", "9.0.0");
        string versionFolder = Directory.CreateDirectory(Path.Combine(Feed, "acme.tree", "1.0.0-local.3")).FullName;
        File.WriteAllText(Path.Combine(versionFolder, "acme.tree.1.0.0-local.3.nupkg"), "");
        File.SetLastWriteTimeUtc(Path.Combine(versionFolder, "acme.tree.1.0.0-local.3.nupkg"), Packed);

        var checks = Verify(flat, tree);

        Assert.Equal([("Acme.Flat", "1.0.0-local.1"), ("Acme.Tree", "1.0.0-local.3")], checks.Select(check => (check.PackageId, check.FeedVersion)));
        Assert.All(checks, check => Assert.Equal(FeedVerifier.Ok, check.Status));
    }
}
