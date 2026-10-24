namespace LocalRun.Tests.Model;

using LocalRun.Core.Model;

public class ProjectVersionTests
{
    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1", "1.0.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("1.4.2", "1.4.2")]
    [InlineData("1.4.2-beta.1", "1.4.2-beta.1")]
    [InlineData("1.4.2.7", "1.4.2.7")]
    public void Parse_FillsMissingComponentsWithZero(string? value, string expected)
    {
        Assert.Equal(expected, ProjectVersion.Parse(value).AsVersionString());
    }

    [Theory]
    [InlineData("v1.4.2")]
    [InlineData("1.x.2")]
    public void Parse_RejectsNonNumericMajorOrMinor(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ProjectVersion.Parse(value));
    }
}
