namespace LocalRun.Tests.Logger;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;

public class LogLevelTests
{
    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("NONE", LogLevel.None)]
    [InlineData("2", null)]          // numbers are not names
    [InlineData("Loud", null)]
    [InlineData(null, null)]
    public void ParseLevel_TakesTheNamesInAnyCase(string? name, LogLevel? expected)
    {
        Assert.Equal(expected, Log.ParseLevel(name));
    }

    [Fact]
    public void ChooseLevel_TheOptionWins_ThenTheSettings_ThenInformation()
    {
        Assert.Equal(LogLevel.Debug, Log.ChooseLevel(["build", "--log-level", "debug"], () => "Error"));
        Assert.Equal(LogLevel.Error, Log.ChooseLevel(["build"], () => "Error"));
        Assert.Equal(LogLevel.Information, Log.ChooseLevel(["build"], () => null));
    }

    [Fact]
    public void ChooseLevel_FallsBackToInformation_TheCommandReportingTheBadValue()
    {
        Assert.Equal(LogLevel.Information, Log.ChooseLevel(["build", "--log-level", "loud"], () => "Error"));
        Assert.Equal(LogLevel.Information, Log.ChooseLevel(["build", "--log-level"], () => "Error"));
        Assert.Equal(LogLevel.Information, Log.ChooseLevel(["build"], () => throw new InvalidOperationException("bad settings")));
    }
}
