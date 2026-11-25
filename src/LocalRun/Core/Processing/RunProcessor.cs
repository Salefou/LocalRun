namespace LocalRun.Core.Processing;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Builds the executables of a config one by one, then starts them all side by side and waits until they
/// have all exited. Ctrl+C, or <see cref="RequestStop"/> (<c>lrun down</c>), stops them: on Windows each process runs on
/// a hidden console of its own, to which Ctrl+C is sent (<see cref="ConsoleSignal"/>; elsewhere they get the terminal's
/// themselves), and whatever is still running after the stop grace period (<see cref="DefaultStopGracePeriod"/> unless
/// given, cut short by a second Ctrl+C or <c>down --force</c>) is killed with its child processes. On Windows they also run in a <see cref="ProcessJob"/>, so they are stopped
/// when LocalRun is killed. Each service's output also goes to its own file in the logs folder, and what happens is
/// reported as <see cref="UpEvent"/>s and in <see cref="Services"/>. One run per instance.
/// </summary>
public class RunProcessor : IRunProcessor
{
    private static readonly ILogger<RunProcessor> logger = Log.For<RunProcessor>();

    internal static readonly TimeSpan DefaultStopGracePeriod = TimeSpan.FromSeconds(10);

    private readonly string? logsFolder;

    private readonly Action<UpEvent>? onEvent;

    private readonly TimeSpan stopGracePeriod;

    private readonly Func<IReadOnlyCollection<string>, string>? commandExcluding;

    private readonly ConcurrentDictionary<string, ServiceOutcome> services = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of <see cref="services"/> in run order, which the dictionary doesn't keep.</summary>
    private List<string> order = [];

    private readonly CancellationTokenSource stopping = new();

    /// <summary>Cancelled by a forced stop: the grace period ends there, and what still runs is killed.</summary>
    private readonly CancellationTokenSource forcing = new();

    /// <summary>Every process started, builds included: a stop sends Ctrl+C to those still running.</summary>
    private readonly ConcurrentQueue<RunningProcess> started = new();

    /// <param name="logsFolder">Where each service's output goes, as <c>&lt;name&gt;.log</c> (overwritten); null for none.</param>
    /// <param name="onEvent">Told of each <see cref="UpEvent"/> as it happens, from any thread.</param>
    /// <param name="stopGracePeriod">How long the processes get to exit after Ctrl+C before being killed.</param>
    /// <param name="commandExcluding">
    /// The command line of this run with the given executables excluded too: a failure suggests it, so that the others can
    /// run without the failed ones. Null for no suggestion.
    /// </param>
    public RunProcessor(string? logsFolder = null, Action<UpEvent>? onEvent = null, TimeSpan? stopGracePeriod = null,
        Func<IReadOnlyCollection<string>, string>? commandExcluding = null)
    {
        this.logsFolder = logsFolder;
        this.onEvent = onEvent;
        this.stopGracePeriod = stopGracePeriod ?? DefaultStopGracePeriod;
        this.commandExcluding = commandExcluding;
    }

    /// <summary>
    /// "{lead}: {command}", the command line that runs the others without <paramref name="failed"/>; null when there
    /// would be no other to run, or no command line to suggest.
    /// </summary>
    internal static string? ExcludeHint(Func<IReadOnlyCollection<string>, string>? commandExcluding,
        IReadOnlyCollection<string> failed, int executableCount, string lead)
    {
        return commandExcluding == null || failed.Count == 0 || failed.Count >= executableCount
            ? null
            : $"{lead}: {commandExcluding(failed)}";
    }

    /// <summary>Each executable of the last run, by name, dependencies first; complete after a failure too.</summary>
    public IReadOnlyList<KeyValuePair<string, ServiceOutcome>> Services => [.. order.Select(name => KeyValuePair.Create(name, services[name]))];

    private void Emit(UpEvent upEvent) => onEvent?.Invoke(upEvent with { Time = DateTimeOffset.UtcNow });

    public async Task RunAsync(LocalRunConfig config, IReadOnlyCollection<string>? exclude = null, IReadOnlyCollection<string>? only = null)
    {
        List<ProjectNode> executables = PlanRun(config, exclude, only);
        if (exclude is { Count: > 0 })
        {
            logger.LogInformation("Not running {Projects} (excluded)", Text.Abbreviate(exclude));
        }
        if (executables.Count == 0)
        {
            logger.LogWarning(DiagnosticEvents.EmptySelection, "No executable to run in this config.");
            return;
        }
        services.Clear();
        order = [.. executables.Select(exe => exe.Name)];
        foreach (var exe in executables)
            services[exe.Name] = new ServiceOutcome { Log = logsFolder == null ? null : Path.Combine(logsFolder, $"{exe.Name}.log") };

        // From the first build: a Ctrl+C while building stops up too, and it still reports what it did
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true; // keep LocalRun alive to stop the processes properly
            // Pressed again while stopping (by Ctrl+C or 'down'): no more waiting
            RequestStop(force: stopping.IsCancellationRequested);
        };
        Console.CancelKeyPress += onCancel;

        // Stops the processes even when LocalRun is killed, when the stopping code below can't run
        using var job = ProcessJob.TryCreate();
        int prefixWidth = executables.Max(exe => exe.Name.Length);
        var styles = Terminal.ColorsEnabled ? ServiceStyle.Assign(executables.Select(exe => exe.Name)) : null;
        var running = new List<RunningProcess>();
        // Each completes once its process exited and that was reported
        var exitsReported = new List<Task>();
        // Processes that failed on their own, before any stop was requested: those make `up` fail
        List<RunningProcess> failed = [];
        if (logsFolder != null)
            Directory.CreateDirectory(logsFolder);
        try
        {
            // One at a time, dependencies first: executables sharing a project reference would otherwise
            // build it concurrently and collide on its output files. After a failure the others are still built, so that
            // one run reports every service a change breaks; then nothing is started.
            string feedSource = LocalFeedRestoreArgument(config);
            List<string> buildFailures = [];
            foreach (var exe in executables)
            {
                if (stopping.IsCancellationRequested)
                    return;
                logger.LogInformation(LogEvents.Start, "Building {Project}", exe.Name);
                Emit(new(UpEvents.Building) { Project = exe.Name });
                try
                {
                    // --force: a no-op restore would keep floating *-* references on the iteration of the last restore,
                    // not the latest in the feed (a pack that failed or skipped this project, a pack of another config)
                    await BuildAsync(exe, $"build \"{FullPath(config, exe)}\" --force --nologo --verbosity quiet{RepackExecutor.NoSummary}{feedSource}{SolutionProperties.Arguments(exe.SolutionPath)}");
                }
                catch (OperationCanceledException)
                {
                    return; // the build got the Ctrl+C too
                }
                catch (DotnetFailedException)
                {
                    buildFailures.Add(exe.Name);
                }
            }
            if (stopping.IsCancellationRequested)
                return;
            if (buildFailures.Count != 0)
            {
                // The failure often comes from the machine (a missing tool) or a project's own build, not from the change
                throw ErrorHint.With(new DotnetFailedException($"dotnet build failed on {string.Join(", ", buildFailures)}: nothing was started."),
                    ExcludeHint(commandExcluding, buildFailures, executables.Count, "To start the others"));
            }

            foreach (var exe in executables)
            {
                if (stopping.IsCancellationRequested)
                    break;
                logger.LogInformation(LogEvents.Launched, "Starting {Project}", exe.Name);
                var outcome = services[exe.Name];
                var process = RunningProcess.Start(exe.Name.PadRight(prefixWidth),
                    "dotnet", $"run --no-build --project \"{FullPath(config, exe)}\"{SolutionProperties.Arguments(exe.SolutionPath)}",
                    job: job, style: styles?[exe.Name], logPath: outcome.Log);
                running.Add(process);
                started.Enqueue(process);
                // Requested while it was starting: the stop's Ctrl+C may have missed it
                if (stopping.IsCancellationRequested && OperatingSystem.IsWindows())
                    _ = ConsoleSignal.SendCtrlCAsync([process.Id]);
                outcome.Status = ServiceStatus.Running;
                Emit(new(UpEvents.Started) { Project = exe.Name, Pid = process.Id, Log = outcome.Log });
                exitsReported.Add(process.Exited.ContinueWith(exited =>
                {
                    outcome.ExitCode = exited.Result;
                    outcome.Status = process.Killed ? ServiceStatus.Killed
                        : stopping.IsCancellationRequested ? ServiceStatus.Stopped
                        : exited.Result == 0 ? ServiceStatus.Exited
                        : ServiceStatus.Failed;
                    Emit(new(UpEvents.Exited) { Project = exe.Name, ExitCode = exited.Result, Status = outcome.Status });
                    // Most likely the service just edited: debugging it on the side is the way on
                    if (outcome.Status == ServiceStatus.Failed)
                    {
                        if (ExcludeHint(commandExcluding, [exe.Name], executables.Count, $"To debug {exe.Name}, run it from your IDE, and the others with") is { } hint)
                            logger.LogInformation(LogEvents.Hint, "{Hint}", hint);
                    }
                }, TaskScheduler.Default));
            }
            logger.LogInformation(LogEvents.Done, "{Processes} started. Press Ctrl+C (or run 'lrun down') to stop them all, again to kill them.",
                Text.Count(running.Count, "process", "processes"));
            if (logsFolder != null)
                logger.LogInformation("Logs: {Folder}", logsFolder);
            Emit(new(UpEvents.Running) { Count = running.Count });

            var allExited = Task.WhenAll(running.Select(process => process.Exited));
            await Task.WhenAny(allExited, Task.Delay(Timeout.Infinite, stopping.Token));
            failed = [.. running.Where(process => process.Exited.IsCompletedSuccessfully && process.Exited.Result != 0)];
            if (stopping.IsCancellationRequested)
            {
                // A second Ctrl+C, or 'down --force', cuts it short
                await Task.WhenAny(allExited, Task.Delay(stopGracePeriod, forcing.Token));
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            var stragglers = running.Where(process => !process.Exited.IsCompleted).ToList();
            foreach (var process in stragglers)
            {
                logger.LogWarning(DiagnosticEvents.ProcessKilled, "{Project} did not stop, killing it", process.Name.Trim());
                process.Kill();
                services[process.Name.Trim()].Status = ServiceStatus.Killed;
            }
            // Let the killed processes flush their last output lines, and every exit be reported
            await Task.WhenAny(Task.WhenAll(exitsReported), Task.Delay(TimeSpan.FromSeconds(5)));
        }

        if (failed.Count != 0)
        {
            throw ErrorHint.With(new InvalidOperationException(
                $"{string.Join(", ", failed.Select(process => $"{process.Name.Trim()} (exit code {process.Exited.Result})"))} failed."),
                ExcludeHint(commandExcluding, [.. failed.Select(process => process.Name.Trim())], executables.Count,
                    $"To debug, run {(failed.Count == 1 ? "it" : "them")} from your IDE, and the others with"));
        }
    }

    /// <summary>
    /// Stops the run as Ctrl+C does: tells the processes still running to stop, then kills them after the grace period.
    /// A <paramref name="force"/>d call (a Ctrl+C during a stop, <c>down --force</c>) ends the grace period, or skips it:
    /// what still runs is killed right away (user feedback, 2026-11-25: a long grace period, cut short at will). Safe from
    /// any thread, before or during <see cref="RunAsync"/>; a call adding nothing is ignored.
    /// </summary>
    public void RequestStop(bool force = false)
    {
        bool first;
        lock (stopping)
        {
            first = !stopping.IsCancellationRequested;
            bool forced = force && !forcing.IsCancellationRequested;
            if (!first && !forced)
                return;
            stopping.Cancel();
            if (forced)
                forcing.Cancel();
        }
        if (first)
        {
            logger.LogInformation(LogEvents.Stopped, "Stopping all processes");
            Emit(new(UpEvents.Stopping));
            // Elsewhere the processes share the terminal's foreground group, and got its Ctrl+C themselves
            if (OperatingSystem.IsWindows())
                _ = ConsoleSignal.SendCtrlCAsync([.. started.Where(process => !process.Exited.IsCompleted).Select(process => process.Id)]);
        }
        if (forcing.IsCancellationRequested)
            logger.LogInformation(LogEvents.Stopped, "Not waiting any longer: killing what still runs");
    }

    /// <summary>
    /// The executables to run, dependencies first: the <paramref name="only"/> ones if given, without the excluded ones.
    /// </summary>
    internal static List<ProjectNode> PlanRun(LocalRunConfig config, IReadOnlyCollection<string>? exclude = null,
        IReadOnlyCollection<string>? only = null)
    {
        var executables = DependencyGraph.FromConfig(config).TopologicalOrder()
            .Where(project => project.Type == "executable")
            .ToList();
        var excluded = KnownExecutables("--exclude", exclude, executables);
        var kept = KnownExecutables("--only", only, executables);

        return [.. executables.Where(project => (only == null || kept.Contains(project.Name)) && !excluded.Contains(project.Name))];
    }

    private static HashSet<string> KnownExecutables(string option, IReadOnlyCollection<string>? names, List<ProjectNode> executables)
    {
        var set = new HashSet<string>(names ?? [], StringComparer.OrdinalIgnoreCase);

        // A misspelt name would silently run the very service being debugged elsewhere, or not the one asked for
        var unknown = set.Where(name => !executables.Any(exe => exe.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count != 0)
        {
            throw new UsageException($"'{option}': not an executable of this config: {string.Join(", ", unknown)}. " +
                $"Executables: {string.Join(", ", executables.Select(exe => exe.Name))}.");
        }
        return set;
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

    /// <summary>
    /// Builds an executable silently. If it fails, its warning and error lines are printed as <c>pack</c> prints them (or
    /// all of its output when it printed no error line), and the failure goes into its <see cref="Services"/> outcome.
    /// Once a stop was requested, a failure is the Ctrl+C the build got: cancelled, not reported.
    /// </summary>
    private async Task BuildAsync(ProjectNode exe, string args)
    {
        var output = new ConcurrentQueue<string>();
        var process = RunningProcess.Start(prefix: null, "dotnet", args, output.Enqueue);
        started.Enqueue(process);
        // Requested while it was starting: the stop's Ctrl+C may have missed it
        if (stopping.IsCancellationRequested && OperatingSystem.IsWindows())
            await ConsoleSignal.SendCtrlCAsync([process.Id]);
        int exitCode = await process.Exited;
        if (exitCode != 0)
        {
            stopping.Token.ThrowIfCancellationRequested();
            var failure = DotnetFailure.Of("build", exitCode, [.. output]);
            if (!output.Any(line => DotnetOutput.DiagnosticLevel(line) == LogLevel.Error))
            {
                foreach (var line in output)
                    logger.LogWarning(DiagnosticEvents.DotnetOutput, "{Line}", line);
            }
            else
            {
                foreach (var line in output)
                {
                    switch (DotnetOutput.DiagnosticLevel(line))
                    {
                        case LogLevel.Error:
                            logger.LogError(DiagnosticEvents.DotnetError, "{Line}", line);
                            break;
                        case LogLevel.Warning:
                            logger.LogWarning(DiagnosticEvents.DotnetWarning, "{Line}", line);
                            break;
                    }
                }
            }
            services[exe.Name].Failure = failure;
            throw new DotnetFailedException($"dotnet build failed on {exe.Name} with exit code {exitCode}");
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

        public int Id => process.Id;

        public Task<int> Exited { get; }

        /// <summary>Whether <see cref="Kill"/> was called.</summary>
        public bool Killed { get; private set; }

        /// <param name="capture">When given, receives the output lines instead of the log.</param>
        /// <param name="job">When given, the process is added to it.</param>
        /// <param name="style">When given, the tag is colored by service and the message by the level read from each line.</param>
        /// <param name="logPath">When given, every output line also goes to this file, without its colors.</param>
        public static RunningProcess Start(string? prefix, string fileName, string args, Action<string>? capture = null,
            ProcessJob? job = null, ServiceStyle? style = null, string? logPath = null)
        {
            var process = Process.Start(new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                // A hidden console of its own: a stop's Ctrl+C reaches it alone (ConsoleSignal), never up's console
                CreateNoWindow = true,
            })!;
            job?.Add(process, prefix?.Trim() ?? fileName);
            var logFile = logPath == null ? null : new LogFile(logPath);
            string tag = prefix == null ? string.Empty : $"[{prefix}] ";
            // Continuation lines (stack frames, the message under a "fail:" line) carry no level: they keep the last one
            var lastLevel = OutputLevel.Information;
            void LogColored(string data)
            {
                string line = ServiceOutput.StripAnsi(data);
                lastLevel = ServiceOutput.DetectLevel(line) ?? lastLevel;
                logger.LogInformation("{Tag}{Line}", style!.Tag(prefix!), ServiceStyle.Message(line, lastLevel));
            }
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                logFile?.WriteLine(e.Data);
                if (capture != null) capture(e.Data);
                else if (style != null && prefix != null) LogColored(e.Data);
                else logger.LogInformation("{Tag}{Line}", tag, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                logFile?.WriteLine(e.Data);
                if (capture != null) capture(e.Data);
                else if (style != null && prefix != null) LogColored(e.Data);
                else logger.LogWarning(DiagnosticEvents.ServiceOutput, "{Tag}{Line}", tag, e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exited = WaitForExitAsync(process, prefix, logFile);
            return new RunningProcess(prefix ?? fileName, process, exited);
        }

        private static async Task<int> WaitForExitAsync(Process process, string? prefix, LogFile? logFile)
        {
            // Returns once the output streams are read to their end too
            await process.WaitForExitAsync();
            logFile?.Dispose();
            if (prefix != null)
            {
                logger.LogInformation(LogEvents.Stopped, "{Project} exited with code {ExitCode}", prefix.Trim(), process.ExitCode);
            }
            return process.ExitCode;
        }

        public void Kill()
        {
            Killed = true;
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

    /// <summary>A service's output, line by line, without colors; written as it comes, so it can be read while it runs.</summary>
    private sealed class LogFile(string path) : IDisposable
    {
        private readonly StreamWriter writer = new(path, append: false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };

        private bool disposed;

        public void WriteLine(string line)
        {
            // stdout and stderr lines arrive on different threads
            lock (writer)
            {
                if (!disposed)
                    writer.WriteLine(ServiceOutput.StripAnsi(line));
            }
        }

        public void Dispose()
        {
            lock (writer)
            {
                disposed = true;
                writer.Dispose();
            }
        }
    }
}
