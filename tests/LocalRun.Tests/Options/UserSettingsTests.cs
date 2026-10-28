namespace LocalRun.Tests.Options;

using LocalRun.Options;

public class UserSettingsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("localrun-settings-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Write(string content)
    {
        string path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void MissingFile_MeansNoSettings()
    {
        Assert.Equal(new UserSettings(), UserSettings.Load(Path.Combine(folder, "nope.json")));
    }

    [Fact]
    public void Template_IsValid_AndSetsNothing()
    {
        Assert.Equal(new UserSettings(), UserSettings.Load(Write(UserSettings.Template)));
    }

    [Fact]
    public void Settings_AreRead_WithCommentsAndAnyCase()
    {
        string feed = Path.Combine(folder, "feed");
        string path = Write($$"""
            {
              // my feed
              "LocalNuget": {{System.Text.Json.JsonSerializer.Serialize(feed)}},
              "suffix": "dev.2",
            }
            """);

        UserSettings settings = UserSettings.Load(path);

        Assert.Equal(feed, settings.LocalNuget);
        Assert.Equal("dev.2", settings.Suffix);
        Assert.Null(settings.Root);
    }

    [Fact]
    public void Tilde_IsTheHomeFolder()
    {
        UserSettings settings = UserSettings.Load(Write("""{ "root": "~/source/repos", "localNuget": "~" }"""));

        Assert.Equal(Path.Combine(UserSettings.HomeFolder, "source", "repos"), settings.Root);
        Assert.Equal(UserSettings.HomeFolder, settings.LocalNuget);
    }

    [Theory]
    [InlineData("""{ "localNuget": "feed" }""")]
    [InlineData("""{ "root": "..\\repos" }""")]
    [InlineData("""{ "suffix": "-dev" }""")]
    [InlineData("""{ "localnuget2": "~" }""")]
    [InlineData("{ not json")]
    public void BadSettings_Fail_NamingTheFile(string content)
    {
        string path = Write(content);

        var ex = Assert.Throws<InvalidDataException>(() => UserSettings.Load(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void CreateIfMissing_WritesTheTemplateOnce_NeverOverwrites()
    {
        string path = Path.Combine(folder, "sub", "settings.json");

        Assert.True(UserSettings.CreateIfMissing(path));
        Assert.StartsWith("{", File.ReadAllText(path));

        File.WriteAllText(path, """{ "suffix": "mine" }""");
        Assert.False(UserSettings.CreateIfMissing(path));
        Assert.Equal("mine", UserSettings.Load(path).Suffix);
    }

    [Fact]
    public void DefaultSuffix_ComesFromTheSettings_UnlessTheOptionIsGiven()
    {
        Assert.Equal("dev", RepackOptionsParser.Parse(new CommandLineArgs([]), "dev").BumpOptions!.Suffix);
        Assert.Equal("beta", RepackOptionsParser.Parse(new CommandLineArgs(["--suffix", "beta"]), "dev").BumpOptions!.Suffix);
        Assert.Equal("local", RepackOptionsParser.Parse(new CommandLineArgs([]), null).BumpOptions!.Suffix);
    }
}
