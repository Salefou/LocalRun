namespace LocalRun.Tests.Processing;

using LocalRun.Core.Processing;
using System.Diagnostics;

public sealed class UpStateTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("localrun-upstate-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string StatePath => Path.Combine(dir, UpState.FileName);

    private static UpState State(int pid, DateTimeOffset started) =>
        new(pid, started, "config.json", "logs", 10, [new UpStateService("Api", 4242, "logs/Api.log")]);

    [Fact]
    public void PathFor_IsNextToTheConfig()
    {
        Assert.Equal(StatePath, UpState.PathFor(Path.Combine(dir, "localrun.config.json")));
    }

    [Fact]
    public void ReadRunning_WithoutAFile_IsNull()
    {
        Assert.Null(UpState.ReadRunning(StatePath));
    }

    [Fact]
    public void ReadRunning_OfALiveProcess_ReturnsItsState()
    {
        State(Environment.ProcessId, UpState.CurrentProcessStarted()).Write(StatePath);

        var state = UpState.ReadRunning(StatePath);

        Assert.NotNull(state);
        Assert.Equal(Environment.ProcessId, state.Pid);
        Assert.Equal(("Api", 4242), (state.Services[0].Project, state.Services[0].Pid));
    }

    [Fact]
    public void ReadRunning_OfAnExitedProcess_IsNull()
    {
        using var exited = Process.Start(new ProcessStartInfo("dotnet", "--version") { UseShellExecute = false, RedirectStandardOutput = true })!;
        exited.WaitForExit();
        State(exited.Id, DateTimeOffset.UtcNow).Write(StatePath);

        Assert.Null(UpState.ReadRunning(StatePath));
    }

    [Fact]
    public void ReadRunning_OfAReusedProcessId_IsNull()
    {
        // The id is alive, but a process started at another time holds it now
        State(Environment.ProcessId, UpState.CurrentProcessStarted().AddMinutes(-5)).Write(StatePath);

        Assert.Null(UpState.ReadRunning(StatePath));
    }

    [Fact]
    public void ReadRunning_OfAHalfWrittenFile_IsNull()
    {
        File.WriteAllText(StatePath, "{ \"pid\": 12");

        Assert.Null(UpState.ReadRunning(StatePath));
    }

    [Fact]
    public void Delete_RemovesOnlyItsOwnFile()
    {
        var mine = State(Environment.ProcessId, UpState.CurrentProcessStarted());
        State(Environment.ProcessId + 1, DateTimeOffset.UtcNow).Write(StatePath);

        mine.Delete(StatePath);
        Assert.True(File.Exists(StatePath));

        mine.Write(StatePath);
        mine.Delete(StatePath);
        Assert.False(File.Exists(StatePath));
    }
}
