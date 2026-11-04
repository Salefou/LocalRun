namespace LocalRun.Tests.Logger;

using LocalRun.Logger;

public class TextTests
{
    [Fact]
    public void Abbreviate_ShowsAtMostTheGivenNumberOfNames()
    {
        Assert.Equal("A, B", Text.Abbreviate(["A", "B"], shown: 2));
        Assert.Equal("A, B and 3 more", Text.Abbreviate(["A", "B", "C", "D", "E"], shown: 2));
        Assert.Equal(string.Empty, Text.Abbreviate([]));
    }

    [Theory]
    [InlineData(850, "850 ms")]
    [InlineData(4_240, "4.2 s")]
    [InlineData(41_400, "41 s")]
    [InlineData(185_000, "3 min 05 s")]
    public void Duration_IsShort(int milliseconds, string expected)
    {
        Assert.Equal(expected, Text.Duration(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void Count_AgreesWithTheNumber()
    {
        Assert.Equal("1 project", Text.Count(1, "project"));
        Assert.Equal("12 projects", Text.Count(12, "project"));
        Assert.Equal("0 libraries", Text.Count(0, "library", "libraries"));
    }
}
