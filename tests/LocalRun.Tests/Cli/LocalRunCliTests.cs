namespace LocalRun.Tests.Cli;

using LocalRun.Cli;

public class LocalRunCliTests
{
    [Theory]
    [InlineData("build-config")]
    [InlineData("repack-preview")]
    [InlineData("repack-go")]
    [InlineData("visualize")]
    [InlineData("run")]
    public async Task FormerCommandNames_AreUnknown(string command)
    {
        Assert.Equal(LocalRunCli.ExitUsage, await LocalRunCli.RunAsync([command]));
    }

    [Theory]
    [InlineData("build", "--nope")]
    [InlineData("build", "--plot", "yes")]
    [InlineData("plot", "--nope")]
    [InlineData("pack", "--nope")]
    [InlineData("pack", "--go", "yes")]
    [InlineData("go", "--nope")]
    [InlineData("up", "--nope")]
    [InlineData("version", "--nope")]
    [InlineData("settings", "--nope")]
    [InlineData("settings", "--path", "yes")]
    public async Task Commands_RejectBadOptionsBeforeDoingAnyWork(params string[] args)
    {
        Assert.Equal(LocalRunCli.ExitUsage, await LocalRunCli.RunAsync(args));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public async Task Version_Succeeds(string command)
    {
        Assert.Equal(LocalRunCli.ExitSuccess, await LocalRunCli.RunAsync([command]));
    }

    [Fact]
    public void Version_IsThePackageVersion()
    {
        // VersionPrefix in the csproj, then the prerelease label and/or '+<commit>' the build adds
        Assert.Matches(@"^\d+\.\d+\.\d+([-+].*)?$", LocalRunCli.Version);
    }

    [Fact]
    public void VisualsPath_IsTimestamped_AndNeverOverwritesAGraph()
    {
        string folder = Directory.CreateTempSubdirectory("localrun-visuals-").FullName;
        try
        {
            var now = new DateTime(2026, 10, 24, 3, 15, 2);
            string first = LocalRunCli.UniqueVisualsPath(folder, now);
            Assert.Equal(Path.Combine(folder, "dependencies-20261024-031502"), first);

            File.WriteAllText($"{first}.html", "");
            string second = LocalRunCli.UniqueVisualsPath(folder, now);
            Assert.Equal($"{first}-2", second);

            File.WriteAllText($"{second}.html", "");
            Assert.Equal($"{first}-3", LocalRunCli.UniqueVisualsPath(folder, now));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WorkFolder_IgnoresItselfInGit_WithoutTouchingAnyOtherFolder()
    {
        string cwd = Directory.CreateTempSubdirectory("localrun-cwd-").FullName;
        try
        {
            string gitignore = Path.Combine(cwd, LocalRunCli.WorkFolder, ".gitignore");

            LocalRunCli.PrepareFolderOf(Path.Combine(cwd, "elsewhere", "localrun.config.json"), cwd);
            Assert.True(Directory.Exists(Path.Combine(cwd, "elsewhere")));
            Assert.False(File.Exists(Path.Combine(cwd, "elsewhere", ".gitignore")));
            Assert.False(File.Exists(gitignore));

            LocalRunCli.PrepareFolderOf(Path.Combine(cwd, LocalRunCli.WorkFolder, "plots", "dependencies"), cwd);
            Assert.True(Directory.Exists(Path.Combine(cwd, LocalRunCli.WorkFolder, "plots")));
            Assert.Contains("\n*\n", File.ReadAllText(gitignore));

            // A .gitignore of the user's is kept
            File.WriteAllText(gitignore, "plots/\n");
            LocalRunCli.PrepareFolderOf(Path.Combine(cwd, LocalRunCli.WorkFolder, "localrun.config.json"), cwd);
            Assert.Equal("plots/\n", File.ReadAllText(gitignore));
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }
}
