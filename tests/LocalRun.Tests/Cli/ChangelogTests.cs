namespace LocalRun.Tests.Cli;

using LocalRun.Cli;
using NuGet.Versioning;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

public class ChangelogTests
{
    private const string Sample = """
        # Changelog

        Preamble, not a release.

        ## [Unreleased]

        ### Added

        - `lrun next`: a new
          command.

        ## [0.2.0] - 2026-11-02

        A summary
        over two lines.

        ### Changed

        - **Breaking:** `--old` is now `--new`.
        - The output:
          - one line per depth
          - fewer marks

        ### Changed (before the first release)

        - Something older.

        ## [0.1.0] - 2026-10-01

        ### Fixed

        - A fix.

        [Unreleased]: https://example.com/compare/v0.2.0...HEAD
        [0.2.0]: https://example.com/compare/v0.1.0...v0.2.0
        """;

    [Fact]
    public void Parse_ReadsReleasesSectionsAndEntries()
    {
        var releases = Changelog.Parse(Sample, "0.3.0");

        Assert.Equal(["0.3.0", "0.2.0", "0.1.0"], releases.Select(release => release.Version));
        var unreleased = releases[0];
        Assert.True(unreleased.Unreleased);
        Assert.Null(unreleased.Date);
        Assert.Equal("`lrun next`: a new command.", unreleased.Sections.Single().Entries.Single().Text);

        var release = releases[1];
        Assert.False(release.Unreleased);
        Assert.Equal("2026-11-02", release.Date);
        Assert.Equal("A summary over two lines.", release.Summary);
        Assert.Equal(["Changed", "Changed (before the first release)"], release.Sections.Select(section => section.Kind));
        var entries = release.Sections[0].Entries;
        Assert.Equal([true, false], entries.Select(entry => entry.Breaking));
        Assert.Equal("The output:\n  - one line per depth\n  - fewer marks", entries[1].Text);
        Assert.StartsWith("## [0.2.0] - 2026-11-02", release.Markdown);
        Assert.EndsWith("- Something older.", release.Markdown);

        // The link references are not part of the last release
        Assert.Equal("## [0.1.0] - 2026-10-01\n\n### Fixed\n\n- A fix.", releases[2].Markdown);
    }

    [Fact]
    public void Unreleased_IsLeftOut_OnceItsVersionIsReleased_OrWhenEmpty()
    {
        Assert.Equal(["0.2.0", "0.1.0"], Changelog.Parse(Sample, "0.2.0").Select(release => release.Version));

        const string justReleased = "## [Unreleased]\n\n## [0.2.0] - 2026-11-02\n\n### Fixed\n\n- A fix.\n";
        Assert.Equal(["0.2.0"], Changelog.Parse(justReleased, "0.2.1").Select(release => release.Version));
    }

    [Theory]
    [InlineData(null, new[] { "0.3.0", "0.2.0", "0.1.0" })]
    [InlineData("0.1.0", new[] { "0.3.0", "0.2.0" })]
    [InlineData("0.2.0-local-0003", new[] { "0.3.0", "0.2.0" })] // a local build before 0.2.0
    [InlineData("0.3.0", new string[0])]
    [InlineData("9.0.0", new string[0])]
    public void Since_KeepsTheNewerReleases(string? since, string[] expected)
    {
        var releases = Changelog.Since(Changelog.Parse(Sample, "0.3.0"), since == null ? null : NuGetVersion.Parse(since));

        Assert.Equal(expected, releases.Select(release => release.Version));
    }

    [Fact]
    public void TheShippedChangelog_ParsesWithAVersionAndADateForEachRelease()
    {
        var releases = Changelog.Parse(Changelog.Text(), LocalRunCli.ReleaseVersion);

        Assert.Contains(releases, release => release.Version == "0.1.0");
        Assert.All(releases.Where(release => !release.Unreleased), release =>
        {
            Assert.True(NuGetVersion.TryParse(release.Version, out _), release.Version);
            Assert.NotNull(release.Date);
            Assert.NotEmpty(release.Sections);
            Assert.All(release.Sections, section => Assert.NotEmpty(section.Entries));
        });
        Assert.Contains(releases.SelectMany(release => release.Sections).SelectMany(section => section.Entries), entry => entry.Breaking);
    }

    [Fact]
    public void TheAgentInstructions_AreStamped_AndTheReadmeShowsThemWordForWord()
    {
        string instructions = AgentInstructions.Text(LocalRunCli.ReleaseVersion);
        Assert.StartsWith($"<!-- written for lrun {LocalRunCli.ReleaseVersion} -->\n## ", instructions);

        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md")).Replace("\r\n", "\n");
        var block = Regex.Match(readme, @"### From an agent\n.*?```markdown\n(?<snippet>.*?)```", RegexOptions.Singleline);
        Assert.True(block.Success, "README has no markdown block under 'From an agent'.");
        // The README shows the stamp of some release; only the version may differ
        var stamp = new Regex(@"^<!-- written for lrun \d+\.\d+\.\d+ -->");
        Assert.Equal(stamp.Replace(instructions, "<!-- stamp -->"), stamp.Replace(block.Groups["snippet"].Value, "<!-- stamp -->"));
    }

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
