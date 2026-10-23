namespace LocalRun.Core.Processing;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

public class RunProcessor : IRunProcessor
{
    private static readonly ILogger<RunProcessor> logger = Log.For<RunProcessor>();

    public async Task RunAsync(LocalRunConfig config)
    {
        var orderedProjects = DependencyGraph.FromConfig(config).TopologicalOrder();
        foreach (var exe in orderedProjects.Where(p => p.Type == "executable"))
        {
            await StartExecutableAsync(exe);
        }
    }

    private static async Task StartExecutableAsync(ProjectNode project)
    {
        logger.LogInformation("[▶] Starting {Project}", project.Name);
        await RunCommandAsync("dotnet", $"run --project \"{project.Path}\"");
    }


    //private static async Task RunCommandAsync(string fileName, string args)
    //{
    //    var psi = new ProcessStartInfo(fileName, args)
    //    {
    //        RedirectStandardOutput = true,
    //        RedirectStandardError = true,
    //        UseShellExecute = false
    //    };

    //    var proc = Process.Start(psi)!;
    //    proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    //    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    //    proc.BeginOutputReadLine();
    //    proc.BeginErrorReadLine();
    //    await proc.WaitForExitAsync();
    //}

    private static async Task RunCommandAsync(string fileName, string args)
    {
        var proc = Process.Start(new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            //CreateNoWindow = false,
        })!;

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) logger.LogInformation("{Line}", e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.LogWarning("{Line}", e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        //await Task.WhenAll(
        //    proc.StandardOutput.ReadToEndAsync(),
        //    proc.StandardError.ReadToEndAsync(),
        //    proc.WaitForExitAsync()
        //);

        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            throw new Exception($"Command `{fileName} {args}` failed with exit code {proc.ExitCode}");
        }
    }
}

