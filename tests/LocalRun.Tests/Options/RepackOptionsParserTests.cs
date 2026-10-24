namespace LocalRun.Tests.Options;

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
