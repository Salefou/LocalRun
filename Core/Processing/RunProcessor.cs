namespace LocalRun.Core.Processing;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

public class RunProcessor : IRunProcessor
{
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
        Console.WriteLine($"[▶] Starting {project.Name}");
        await RunCommandAsync("dotnet", $"run --project \"{project.Path}\"");
    }

    private static async Task RunCommandAsync(string fileName, string args)
    {
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false,
        })!;

        await Task.WhenAll(
            proc.StandardOutput.ReadToEndAsync(),
            proc.StandardError.ReadToEndAsync()
        );

        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            throw new Exception($"Command `{fileName} {args}` failed with exit code {proc.ExitCode}");
        }
    }
}

