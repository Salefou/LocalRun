namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;

public class VersionChangesTests
{
    private static (string, ProjectVersion, ProjectVersion) Plan(string name, string from, string to) =>
        (name, ProjectVersion.Parse(from), ProjectVersion.Parse(to));

    [Theory]
    [InlineData("1.4.3-local.60", "1.4.3-local.61", "local.60 -> local.61")]
    [InlineData("1.4.3", "1.4.3-local.1", "release -> local.1")]
    [InlineData("1.4.3-local.60", "1.4.4-dev.1", "patch bump, local.60 -> dev.1")]
    [InlineData("1.4.3", "1.5.0-dev.1", "minor bump, release -> dev.1")]
    [InlineData("1.4.3", "2.0.0-dev.1", "major bump, release -> dev.1")]
    [InlineData("1.4.3", "1.4.3", "unchanged")]
    public void Describe_LeavesTheNumbersOut(string from, string to, string expected)
    {
        Assert.Equal(expected, VersionChanges.Describe(ProjectVersion.Parse(from), ProjectVersion.Parse(to), "->"));
    }

    [Theory]
    [InlineData("1.4.3", "1.4.5-dev.1")]
    [InlineData("1.4.3", "1.5.1-dev.1")]
    public void Describe_IsNull_ForAnythingButABump(string from, string to)
    {
        Assert.Null(VersionChanges.Describe(ProjectVersion.Parse(from), ProjectVersion.Parse(to), "->"));
    }

    [Fact]
    public void Summarize_TellsTheSharedChangeOnce_AndListsTheOthers()
    {
        var (common, count, others) = VersionChanges.Summarize(
        [
            Plan("A", "1.0.0-local.60", "1.0.0-local.61"),
            Plan("B", "2.3.1-local.60", "2.3.1-local.61"),
            Plan("Next", "11.0.0-preview", "11.0.0-local.61"),
            Plan("C", "0.1.0-local.60", "0.1.0-local.61"),
        ], "->");

        Assert.Equal("local.60 -> local.61", common);
        Assert.Equal(3, count);
        Assert.Equal([("Next", "11.0.0-preview -> 11.0.0-local.61")], others);
    }

    [Fact]
    public void Summarize_HasNoSharedChange_WhenNoTwoProjectsShareOne()
    {
        var (common, _, others) = VersionChanges.Summarize(
            [Plan("A", "1.0.0", "1.0.0-local.1"), Plan("B", "2.0.0-preview", "2.0.0-local.1")], "->");

        Assert.Null(common);
        Assert.Equal(["A", "B"], others.Select(other => other.Name));
    }
}
