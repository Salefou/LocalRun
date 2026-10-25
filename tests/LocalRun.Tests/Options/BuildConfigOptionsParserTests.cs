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

    [Fact]
    public void Union_IsReadWithBothFilters()
    {
        Assert.True(Parse("--users-of", "X", "--deps-of", "Y", "--union").Union);
        Assert.False(Parse("--users-of", "X", "--deps-of", "Y").Union);
    }

    [Theory]
    [InlineData("--union")]
    [InlineData("--union", "--users-of", "X")]
    [InlineData("--union", "--deps-of", "Y")]
    public void Union_WithoutBothFilters_IsRejected(params string[] argv)
    {
        var ex = Assert.Throws<UsageException>(() => Parse(argv));
        Assert.Contains("--union", ex.Message);
    }

    [Theory]
    [InlineData("--with-services", "--users-of", "X")]
    [InlineData("--with-services", "--deps-of", "Y")]
    public void WithServices_IsReadWithAtLeastOneFilter(params string[] argv)
    {
        Assert.True(Parse(argv).WithServices);
    }

    [Fact]
    public void GitChanges_IsOffByDefault()
    {
        Assert.False(Parse().RetainGitChanges);
        Assert.True(Parse("--git-changes").RetainGitChanges);
    }

    [Theory]
    [InlineData("--git-changes", "--deps-of", "Y", "--union")]
    [InlineData("--git-changes", "--with-services")]
    public void GitChanges_CountsAsTheUsersOfSide(params string[] argv)
    {
        Assert.True(Parse(argv).RetainGitChanges);
    }

    [Fact]
    public void Union_WithGitChangesButNoDepsOf_IsRejected()
    {
        var ex = Assert.Throws<UsageException>(() => Parse("--git-changes", "--union"));
        Assert.Contains("--union", ex.Message);
    }

    [Fact]
    public void Remote_IsOffByDefault()
    {
        Assert.False(Parse().RemoteVersions);
        Assert.True(Parse("--remote").RemoteVersions);
        Assert.False(Parse("--remote").RemoteAllSources);
    }

    [Fact]
    public void AllSources_IsReadWithRemote()
    {
        Assert.True(Parse("--remote", "--all-sources").RemoteAllSources);
    }

    [Fact]
    public void AllSources_WithoutRemote_IsRejected()
    {
        var ex = Assert.Throws<UsageException>(() => Parse("--all-sources"));
        Assert.Contains("--remote", ex.Message);
    }

    [Fact]
    public void WithServices_WithoutAnyFilter_IsRejected()
    {
        var ex = Assert.Throws<UsageException>(() => Parse("--with-services"));
        Assert.Contains("--with-services", ex.Message);
    }
}
