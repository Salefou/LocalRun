namespace LocalRun.Tests.Options;

using LocalRun.Options;

public class BuildConfigOptionsParserTests
{
    private static BuildConfigOptions Parse(params string[] argv)
    {
        var args = new CommandLineArgs(argv);
        var options = BuildConfigOptionsParser.Parse(args);
        args.EnsureAllConsumed();
        return options;
    }

    [Fact]
    public void Filters_AreReadFromUsersOfAndDepsOf()
    {
        var options = Parse("--users-of", "Shared.Pricing", "--deps-of", "OrderGateway", "RiskService");

        Assert.Equal(["Shared.Pricing"], options.RetainDependsOn);
        Assert.Equal(["OrderGateway", "RiskService"], options.RetainIsDependencyOf);
    }

    [Fact]
    public void NoFilter_KeepsEverything()
    {
        var options = Parse();

        Assert.Null(options.RetainDependsOn);
        Assert.Null(options.RetainIsDependencyOf);
    }

    [Theory]
    [InlineData("--retain-depends-on")]
    [InlineData("--retain-is-dependency-of")]
    public void FormerFilterNames_AreRejected(string option)
    {
        Assert.Throws<UsageException>(() => Parse(option, "A"));
    }
}
