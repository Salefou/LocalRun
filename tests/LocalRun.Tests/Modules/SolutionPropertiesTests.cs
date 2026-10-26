namespace LocalRun.Tests.Modules;

using LocalRun.Core.Modules;

public class SolutionPropertiesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithoutASolution_NothingIsDefined(string? solutionPath)
    {
        Assert.Equal(string.Empty, SolutionProperties.Arguments(solutionPath));
    }

    [Fact]
    public void TheSolutionPropertiesAreDefinedLikeVisualStudioDoes()
    {
        string solution = Path.GetFullPath(Path.Combine("My Repos", "App", "App.sln"));
        string dir = Path.GetDirectoryName(solution)! + Path.DirectorySeparatorChar;
        // A backslash before the closing quote is doubled, or it would escape the quote
        string quotedDir = OperatingSystem.IsWindows() ? $"\"{dir}\\\"" : $"\"{dir}\"";

        Assert.Equal(
            $" -p:SolutionDir={quotedDir} -p:SolutionPath=\"{solution}\" -p:SolutionName=\"App\" -p:SolutionFileName=\"App.sln\" -p:SolutionExt=\".sln\"",
            SolutionProperties.Arguments(solution));
    }
}
