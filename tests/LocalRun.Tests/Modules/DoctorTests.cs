namespace LocalRun.Tests.Modules;

using LibGit2Sharp;
using LocalRun.Core.Modules;

public sealed class DoctorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("localrun-doctor-").FullName;

    public void Dispose()
    {
        // git marks its object files read-only, which Directory.Delete refuses on Windows
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void NoSettingsFile_IsOk_WithTheDefaults()
    {
        var (check, settings) = Doctor.CheckSettings(Path.Combine(root, "settings.json"));

        Assert.Equal(Doctor.Ok, check.Status);
        Assert.Null(settings.Root);
    }

    [Fact]
    public void InvalidSettings_Fail_AndTheOtherChecksUseTheDefaults()
    {
        string path = Write("settings.json", """{ "root": "relative/path", "localNuget": "~/feed" }""");

        var (check, settings) = Doctor.CheckSettings(path);

        Assert.Equal(Doctor.Fail, check.Status);
        Assert.Contains(path, check.Message);
        Assert.Null(settings.LocalNuget);
    }

    [Fact]
    public async Task Dotnet_ReportsTheSdkVersion()
    {
        var check = await Doctor.CheckDotnetAsync(root);

        Assert.Equal(Doctor.Ok, check.Status);
        Assert.Matches(@"^\d+\.\d+\.\d+", check.Message);
    }

    [Fact]
    public async Task AGlobalJsonPinningAMissingSdk_Fails()
    {
        Write("global.json", """{ "sdk": { "version": "1.0.100", "rollForward": "disable" } }""");

        var check = await Doctor.CheckDotnetAsync(root);

        Assert.Equal(Doctor.Fail, check.Status);
        Assert.Contains("1.0.100", check.Message);
    }

    [Fact]
    public void AMissingLocalFeed_IsOk_TheFirstGoCreatesIt()
    {
        var check = Doctor.CheckLocalFeed(Path.Combine(root, "feed"));

        Assert.Equal(Doctor.Ok, check.Status);
        Assert.False(Directory.Exists(Path.Combine(root, "feed")));
    }

    [Fact]
    public void AWritableLocalFeed_IsOk_AndLeftAsItWas()
    {
        string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        Write("feed/Lib.1.0.0.nupkg", "");

        var check = Doctor.CheckLocalFeed(feed);

        Assert.Equal(Doctor.Ok, check.Status);
        Assert.Equal("writable, 1 package", check.Message);
        Assert.Equal(["Lib.1.0.0.nupkg"], Directory.GetFiles(feed).Select(Path.GetFileName));
    }

    [Fact]
    public async Task LocalSources_FailWhenTheirFolderIsMissing_ExceptTheLocalFeedNotPackedYet()
    {
        string present = Directory.CreateDirectory(Path.Combine(root, "present")).FullName;
        string missing = Path.Combine(root, "missing");
        string feed = Path.Combine(root, "feed");
        Write("A/NuGet.Config", $"""
            <configuration><packageSources><clear />
              <add key="present" value="{present}" /><add key="missing" value="{missing}" /><add key="feed" value="{feed}" />
            </packageSources></configuration>
            """);
        Write("A/A.csproj", "<Project />");

        var checks = await Doctor.CheckSourcesAsync([Path.Combine(root, "A")], feed);

        Assert.Equal([("present", Doctor.Ok), ("missing", Doctor.Fail)],
            checks.Select(check => (check.Subject.Split(' ')[0], check.Status)));
    }

    [Fact]
    public async Task ASourceSharedByProjects_IsCheckedOnce()
    {
        string present = Directory.CreateDirectory(Path.Combine(root, "present")).FullName;
        Write("NuGet.Config", $"""<configuration><packageSources><clear /><add key="present" value="{present}" /></packageSources></configuration>""");
        Write("A/A.csproj", "<Project />");
        Write("B/B.csproj", "<Project />");

        var checks = await Doctor.CheckSourcesAsync([Path.Combine(root, "A"), Path.Combine(root, "B")], Path.Combine(root, "feed"));

        Assert.Single(checks);
    }

    private Repository InitRepo()
    {
        Repository.Init(root);
        var repo = new Repository(root);
        Write("A/A.csproj", "<Project />");
        Commands.Stage(repo, "*");
        var author = new Signature("test", "test@example.com", DateTimeOffset.Now);
        repo.Commit("initial", author, author);
        return repo;
    }

    [Fact]
    public void ARepoWithoutOriginBranch_Warns_AndANeverFetchedOneSaysNothingOfItsFetch()
    {
        using (InitRepo()) { }

        var check = Assert.Single(Doctor.CheckRepository(Path.Combine(root, ".git"), DateTime.UtcNow));

        Assert.Equal(("git-base", Doctor.Warn), (check.Check, check.Status));
    }

    [Theory]
    [InlineData(2, "ok")]
    [InlineData(10, "warn")]
    public void TheLastFetch_WarnsWhenOld(int daysAgo, string status)
    {
        using (var repo = InitRepo())
            repo.Refs.Add("refs/remotes/origin/main", repo.Head.Tip.Id);
        string fetchHead = Write(".git/FETCH_HEAD", "");
        var now = File.GetLastWriteTimeUtc(fetchHead).AddDays(daysAgo);

        var checks = Doctor.CheckRepository(Path.Combine(root, ".git"), now);

        Assert.Equal([("git-base", Doctor.Ok, "origin/main"), ("git-fetch", status, checks[1].Message)],
            checks.Select(check => (check.Check, check.Status, check.Message)));
    }
}
