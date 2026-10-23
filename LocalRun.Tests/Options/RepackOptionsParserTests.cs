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
    [InlineData("--patch", "--minor")]
    [InlineData("--alpha", "--major")]
    [InlineData("--alpha", "--suffix", "dev")]
    public void ConflictingBumps_AreRejected(params string[] argv)
    {
        Assert.Throws<UsageException>(() => Parse(argv));
    }
}
