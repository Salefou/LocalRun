namespace LocalRun.Tests.Processing;

using LocalRun.Core.Processing;
using System.Diagnostics;

public class ProcessJobTests
{
    private static Process StartPing() => Process.Start(new ProcessStartInfo("ping", "-n 60 127.0.0.1")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
    })!;

    [Fact]
    public void LimitsStruct_HasTheSizeWindowsExpects()
    {
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION from the Windows SDK: 144 bytes in 64-bit processes, 112 in 32-bit ones
        Assert.Equal(Environment.Is64BitProcess ? 144 : 112, ProcessJob.ExtendedLimitInformationSize);
    }

    [Fact]
    public void Dispose_KillsTheProcessesOfTheJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var ping = StartPing();
        var job = ProcessJob.TryCreate();
        Assert.NotNull(job);
        job.Add(ping, "ping");

        job.Dispose();

        Assert.True(ping.WaitForExit(TimeSpan.FromSeconds(10)), "ping should have been killed with the job");
    }

    [Fact]
    public void Dispose_KillsTheChildrenOfTheProcessesOfTheJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        var job = ProcessJob.TryCreate();
        Assert.NotNull(job);
        // cmd starts ping after joining the job, as `dotnet run` starts the app. It waits for a line on stdin first
        // (set /p is built in, no child process): started before job.Add, ping would be outside the job
        using var cmd = Process.Start(new ProcessStartInfo("cmd", "/c set /p go= & ping -n 60 127.0.0.1 & echo done")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;
        job.Add(cmd, "cmd");
        cmd.StandardInput.WriteLine("go");
        Process ping = WaitForChild(cmd.Id, "PING.EXE");

        job.Dispose();

        Assert.True(ping.WaitForExit(TimeSpan.FromSeconds(10)), "the child ping should have been killed with the job");
        Assert.True(cmd.WaitForExit(TimeSpan.FromSeconds(10)));
    }

    // Process has no API for children: take the process of that name started after the parent
    private static Process WaitForChild(int parentId, string imageName)
    {
        var parent = Process.GetProcessById(parentId);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var child = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(imageName))
                .FirstOrDefault(process => process.StartTime >= parent.StartTime && process.Id != parentId);
            if (child != null) return child;
            Thread.Sleep(50);
        }
        throw new TimeoutException($"{imageName} was not started by process {parentId}");
    }
}
