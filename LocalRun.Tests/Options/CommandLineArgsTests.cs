namespace LocalRun.Tests.Options;

using LocalRun.Options;

public class CommandLineArgsTests
{
    [Fact]
    public void Values_RunUntilTheNextOption()
    {
        var args = new CommandLineArgs(["--retain-depends-on", "A", "B", "--include-external"]);

        Assert.Equal(["A", "B"], args.Values("--retain-depends-on"));
        Assert.True(args.Flag("--include-external"));
        args.EnsureAllConsumed();
    }

    [Fact]
    public void MissingOptions_ReadAsAbsent()
    {
        var args = new CommandLineArgs([]);

        Assert.False(args.Flag("--open"));
        Assert.Null(args.Value("--config"));
        Assert.Null(args.Values("--retain-depends-on"));
    }

    [Fact]
    public void UnknownOption_IsRejected()
    {
        var args = new CommandLineArgs(["--config", "a.json", "--typo"]);
        args.Value("--config");

        var ex = Assert.Throws<UsageException>(args.EnsureAllConsumed);
        Assert.Contains("--typo", ex.Message);
    }

    [Fact]
    public void ArgumentBeforeAnyOption_IsRejected()
    {
        Assert.Throws<UsageException>(() => new CommandLineArgs(["stray"]));
    }

    [Fact]
    public void RepeatedOption_IsRejected()
    {
        Assert.Throws<UsageException>(() => new CommandLineArgs(["--patch", "--patch"]));
    }

    [Theory]
    [InlineData("--suffix")]
    [InlineData("--suffix", "a", "b")]
    public void Value_RequiresExactlyOneValue(params string[] argv)
    {
        var args = new CommandLineArgs(argv);

        Assert.Throws<UsageException>(() => args.Value("--suffix"));
    }

    [Fact]
    public void Flag_RejectsAValue()
    {
        var args = new CommandLineArgs(["--open", "yes"]);

        Assert.Throws<UsageException>(() => args.Flag("--open"));
    }
}
