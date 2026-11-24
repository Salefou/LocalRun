namespace LocalRun.Tests.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Options;

public class ConfigBuilderTests
{
    [Fact]
    public void Filters_AreToldAsTyped_OrNullWithoutAny()
    {
        Assert.Null(ConfigBuilder.Filters(new BuildConfigOptions()));
        Assert.Equal("--users-of A B, --git-changes, --deps-of WebApp, --with-services", ConfigBuilder.Filters(new BuildConfigOptions
        {
            RetainDependsOn = ["A", "B"],
            RetainGitChanges = true,
            RetainIsDependencyOf = ["WebApp"],
            WithServices = true,
        }));
    }

    [Fact]
    public void TheRecord_KeepsTheFlags_WhatWasFound_AndWhen()
    {
        var options = Options("--users-of", "A", "--git-changes", "--with-services", "--tests", "--csproj");
        var summary = new BuildSummary(5, [new("filter", ["C"])], ["A", "B"], []);
        var time = DateTimeOffset.Now;

        var record = ConfigBuilder.Record("root", options, summary, "0.6.0-local", time);

        Assert.Equal((time, "0.6.0-local", "--users-of A, --git-changes, --with-services"), (record.Time, record.LrunVersion, record.Filters));
        Assert.Equal(["A"], record.UsersOf);
        Assert.Null(record.DepsOf);
        Assert.Equal((true, false, true, false, true, true),
            (record.GitChanges, record.Union, record.WithServices, record.IncludeExternal, record.Tests, record.Csproj));
        Assert.Equal(["A", "B"], record.ChangedProjects);
        Assert.Equal(5, record.Scanned);
        Assert.Equal("filter", Assert.Single(record.LeftOut).Reason);
        Assert.Empty(record.Repositories);
    }

    private static DependencyGraph Graph(params string[] names) => DependencyGraph.FromConfig(new LocalRunConfig
    {
        Projects = [.. names.Select(name => new ProjectNode
        {
            Name = name,
            Path = $"{name}/{name}.csproj",
            Type = "library",
            Version = ProjectVersion.Parse("1.0.0"),
            Dependencies = [],
        })],
    });

    private static BuildConfigOptions Options(params string[] argv) => BuildConfigOptionsParser.Parse(new CommandLineArgs(argv));

    [Fact]
    public void Exclude_IsToldAsTyped_AndRecorded()
    {
        var options = Options("--deps-of", "WebApp", "--exclude", "AotTestApp", "TestApp");

        var record = ConfigBuilder.Record("root", options, new BuildSummary(5, [], null, []), "0.7.1-local", DateTimeOffset.Now);

        Assert.Equal("--deps-of WebApp, --exclude AotTestApp TestApp", record.Filters);
        Assert.Equal(["AotTestApp", "TestApp"], record.Exclude);
        Assert.Null(ConfigBuilder.Record("root", Options(), new BuildSummary(5, [], null, []), "0.7.1-local", DateTimeOffset.Now).Exclude);
    }

    [Fact]
    public void UnknownExcludedName_IsAUsageError_NamingTheFlag()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ConfigBuilder.ValidateFilterNames(Graph("Core", "Api"), Options("--exclude", "Typo")));

        Assert.Contains("'--exclude'", ex.Message);
        Assert.Contains("Typo", ex.Message);
    }

    [Fact]
    public void UnknownFilterName_IsAUsageError_NamingTheFlag()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ConfigBuilder.ValidateFilterNames(Graph("Core", "Api"), Options("--users-of", "Core", "--deps-of", "Typo")));

        Assert.Contains("--deps-of", ex.Message);
        Assert.Contains("Typo", ex.Message);
    }

    [Fact]
    public void CollidedFilterName_IsAUsageError_SayingItIsAmbiguous()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ConfigBuilder.ValidateFilterNames(Graph("Core", "Shared", "Shared"), Options("--users-of", "shared")));

        Assert.Contains("Ambiguous", ex.Message);
        Assert.Contains("--users-of", ex.Message);
    }

    [Fact]
    public void KnownFilterNames_PassInAnyCase()
    {
        ConfigBuilder.ValidateFilterNames(Graph("Core", "Api"), Options("--users-of", "core", "--deps-of", "API"));
    }
}
