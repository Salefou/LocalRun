namespace LocalRun.Core.Processing;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Builds the executables of a config one by one, then starts them all side by side and waits until they
/// have all exited. Ctrl+C stops them: they receive it too (same console), and whatever is still running
/// after <see cref="StopGracePeriod"/> is killed with its child processes. On Windows they also run in a
/// <see cref="ProcessJob"/>, so they are stopped when LocalRun is killed.
/// </summary>
public class RunProcessor : IRunProcessor
{
    private static readonly ILogger<RunProcessor> logger = Log.For<RunProcessor>();

    internal static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(10);

    public async Task RunAsync(LocalRunConfig config, IReadOnlyCollection<string>? exclude = null)
    {
        List<ProjectNode> executables = PlanRun(config, exclude);
        if (exclude is { Count: > 0 })
        {
            logger.LogInformation("[⏭ ] Not running {Projects}: start them yourself (e.g. from your IDE to debug them)",
                string.Join(", ", exclude));
        }
        if (executables.Count == 0)
        {
            logger.LogWarning("No executable to run in this config.");
            return;
        }

        // One at a time, dependencies first: executables sharing a project reference would otherwise
        // build it concurrently and collide on its output files
        string feedSource = LocalFeedRestoreArgument(config);
        foreach (var exe in executables)
        {
            logger.LogInformation("[🔨] Building {Project}...", exe.Name);
            await RunToCompletionAsync("dotnet", $"build \"{FullPath(config, exe)}\" --nologo --verbosity quiet{feedSource}{SolutionProperties.Arguments(exe.SolutionPath)}");
        }

        using var stopping = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true; // keep LocalRun alive to stop the processes properly
            if (!stopping.IsCancellationRequested)
            {
                logger.LogInformation("[■] Stopping all processes...");
                stopping.Cancel();
            }
        };
        Console.CancelKeyPress += onCancel;

        // Stops the processes even when LocalRun is killed, when the stopping code below can't run
        using var job = ProcessJob.TryCreate();
        int prefixWidth = executables.Max(exe => exe.Name.Length);
        var running = new List<RunningProcess>();
        // Processes that failed on their own, before any stop was requested: those make `up` fail
        List<RunningProcess> failed = [];
        try
        {
            foreach (var exe in executables)
            {
                logger.LogInformation("[▶] Starting {Project}", exe.Name);
                running.Add(RunningProcess.Start(exe.Name.PadRight(prefixWidth),
                    "dotnet", $"run --no-build --project \"{FullPath(config, exe)}\"{SolutionProperties.Arguments(exe.SolutionPath)}", job: job));
            }
            logger.LogInformation("[✔ ] {Count} process(es) started. Press Ctrl+C to stop them all.", running.Count);

            var allExited = Task.WhenAll(running.Select(process => process.Exited));
            await Task.WhenAny(allExited, Task.Delay(Timeout.Infinite, stopping.Token));
            failed = [.. running.Where(process => process.Exited.IsCompletedSuccessfully && process.Exited.Result != 0)];
            if (stopping.IsCancellationRequested)
            {
                await Task.WhenAny(allExited, Task.Delay(StopGracePeriod));
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            var stragglers = running.Where(process => !process.Exited.IsCompleted).ToList();
            foreach (var process in stragglers)
            {
                logger.LogWarning("{Project} did not stop, killing it", process.Name.Trim());
                process.Kill();
            }
            // Let the killed processes flush their last output lines
            await Task.WhenAny(Task.WhenAll(stragglers.Select(process => process.Exited)), Task.Delay(TimeSpan.FromSeconds(5)));
        }

        if (failed.Count != 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", failed.Select(process => $"{process.Name.Trim()} (exit code {process.Exited.Result})"))} failed");
        }
    }

    /// <summary>The executables to run, dependencies first, without the excluded ones.</summary>
    internal static List<ProjectNode> PlanRun(LocalRunConfig config, IReadOnlyCollection<string>? exclude = null)
    {
        var executables = DependencyGraph.FromConfig(config).TopologicalOrder()
            .Where(project => project.Type == "executable")
            .ToList();
        var excluded = new HashSet<string>(exclude ?? [], StringComparer.OrdinalIgnoreCase);

        // A misspelt name would silently run the very service being debugged elsewhere
        var unknown = excluded.Where(name => !executables.Any(exe => exe.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count != 0)
        {
            throw new UsageException($"'--exclude': not an executable of this config: {string.Join(", ", unknown)}. " +
                $"Executables: {string.Join(", ", executables.Select(exe => exe.Name))}.");
        }

        return [.. executables.Where(project => !excluded.Contains(project.Name))];
    }

    /// <summary>
    /// Adds the config's local feed to the restore sources, as `go` does, so the executables find the packages
    /// `go` just packed even when their NuGet.config doesn't list that feed. Empty when the feed doesn't exist
    /// (nothing packed yet): NuGet fails on a missing local source.
    /// </summary>
    internal static string LocalFeedRestoreArgument(LocalRunConfig config)
    {
        return !string.IsNullOrEmpty(config.LocalNugetPath) && Directory.Exists(config.LocalNugetPath)
            ? $" -p:RestoreAdditionalProjectSources=\"{config.LocalNugetPath}\""
            : string.Empty;
    }

    // Project paths are relative to the scanned root, not to the current directory
    private static string FullPath(LocalRunConfig config, ProjectNode project) => Path.Combine(config.RootPath, project.Path);

    /// <summary>Runs a command silently; its output is only printed if it fails.</summary>
    private static async Task RunToCompletionAsync(string fileName, string args)
    {
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var process = RunningProcess.Start(prefix: null, fileName, args, output.Enqueue);
        int exitCode = await process.Exited;
        if (exitCode != 0)
        {
            foreach (var line in output)
                logger.LogWarning("{Line}", line);
            throw new InvalidOperationException($"Command `{fileName} {args}` failed with exit code {exitCode}");
        }
    }

    /// <summary>A child process whose output is logged line by line, optionally prefixed.</summary>
    private sealed class RunningProcess
    {
        private readonly Process process;

        private RunningProcess(string name, Process process, Task<int> exited)
        {
            Name = name;
            this.process = process;
            Exited = exited;
        }

        public string Name { get; }

        public Task<int> Exited { get; }

        /// <param name="capture">When given, receives the output lines instead of the log.</param>
        /// <param name="job">When given, the process is added to it.</param>
        public static RunningProcess Start(string? prefix, string fileName, string args, Action<string>? capture = null,
            ProcessJob? job = null)
        {
            var process = Process.Start(new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            job?.Add(process, prefix?.Trim() ?? fileName);
            string tag = prefix == null ? string.Empty : $"[{prefix}] ";
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (capture != null) capture(e.Data);
                else logger.LogInformation("{Tag}{Line}", tag, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (capture != null) capture(e.Data);
                else logger.LogWarning("{Tag}{Line}", tag, e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exited = WaitForExitAsync(process, prefix);
            return new RunningProcess(prefix ?? fileName, process, exited);
        }

        private static async Task<int> WaitForExitAsync(Process process, string? prefix)
        {
            await process.WaitForExitAsync();
            if (prefix != null)
            {
                logger.LogInformation("[■] {Project} exited with code {ExitCode}", prefix.Trim(), process.ExitCode);
            }
            return process.ExitCode;
        }

        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime
            }
        }
    }
}
