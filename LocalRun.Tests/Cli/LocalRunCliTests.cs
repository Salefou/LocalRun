namespace LocalRun.Tests.Cli;

using LocalRun.Cli;

public class LocalRunCliTests
{
    [Theory]
    [InlineData("build-config")]
    [InlineData("repack-preview")]
    [InlineData("repack-go")]
    public async Task FormerCommandNames_AreUnknown(string command)
    {
        Assert.Equal(LocalRunCli.ExitUsage, await LocalRunCli.RunAsync([command]));
    }

    [Theory]
    [InlineData("build", "--nope")]
    [InlineData("pack", "--nope")]
    [InlineData("pack", "--go", "yes")]
    [InlineData("go", "--nope")]
    [InlineData("run", "--nope")]
    public async Task Commands_RejectBadOptionsBeforeDoingAnyWork(params string[] args)
    {
        Assert.Equal(LocalRunCli.ExitUsage, await LocalRunCli.RunAsync(args));
    }
}
