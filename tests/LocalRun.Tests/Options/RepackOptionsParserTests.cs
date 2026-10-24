namespace LocalRun.Tests.Options;

using LocalRun.Core.Model;
using LocalRun.Options;

public class RepackOptionsParserTests
{
    private static RepackOptions Parse(params string[] argv)
    {
        var args = new CommandLineArgs(argv);
        var options = RepackOptionsParser.Parse(args);
        args.EnsureAllConsumed();
        return options;
    }

    [Fact]
    public void Suffix_IsReadWithTheBump()
    {
        var options = Parse("--patch", "--suffix", "dev", "--remote");

        Assert.Equal(BumpType.Patch, options.BumpOptions!.BumpType);
        Assert.Equal("dev", options.BumpOptions.Suffix);
        Assert.True(options.UseRemoteAsVersionReference);
        Assert.False(options.CleanBeforeRestore);
    }

    [Fact]
    public void Alpha_IsAPatchWithAlphaSuffix()
    {
        var options = Parse("--alpha");

        Assert.Equal(BumpType.Patch, options.BumpOptions!.BumpType);
        Assert.Equal("alpha", options.BumpOptions.Suffix);
    }

    [Theory]
    [InlineData]
    [InlineData("--patch")]
    [InlineData("--major", "--remote")]
    public void NoSuffix_DefaultsToLocal(params string[] argv)
    {
        Assert.Equal("local", Parse(argv).BumpOptions!.Suffix);
    }

    [Theory]
    [InlineData("1.4.2", "1.4.2-local")]
    [InlineData("1.4.2-beta", "1.4.2-local")]
    public void NoOptions_PlanAPrereleaseOfTheCurrentVersion(string version, string expected)
    {
        var planned = Parse().BumpOptions!.Bump(ProjectVersion.Parse(version));

        Assert.Equal(expected, planned.AsVersionString());
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("beta.2")]
    [InlineData("ci-42")]
    public void ValidSuffix_IsAccepted(string suffix)
    {
        Assert.Equal(suffix, Parse("--suffix", suffix).BumpOptions!.Suffix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-dev")]
    [InlineData("beta..2")]
    [InlineData("dev.")]
    [InlineData("dev_1")]
    [InlineData("dev+sha")]
    public void InvalidSuffix_IsRejected(string suffix)
    {
        Assert.Throws<UsageException>(() => Parse("--suffix", suffix));
    }

    [Theory]
    [InlineData("--patch", "--minor")]
    [InlineData("--alpha", "--major")]
    [InlineData("--alpha", "--suffix", "dev")]
    public void ConflictingBumps_AreRejected(params string[] argv)
    {
        Assert.Throws<UsageException>(() => Parse(argv));
    }
}
