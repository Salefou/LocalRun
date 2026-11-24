namespace LocalRun.Cli;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// Runs one LocalRun command, as given on the command line, and returns its exit code.
/// </summary>
public static class LocalRunCli
{
    public const int ExitSuccess = 0;
    /// <summary>Any failure without a code of its own.</summary>
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    /// <summary>An input is missing or invalid: a config file, the root to scan, the settings.</summary>
    public const int ExitInput = 3;
    /// <summary>A <c>dotnet</c> command failed: fix the code or the project, retrying won't help.</summary>
    public const int ExitDotnetFailed = 4;
    /// <summary><c>--strict</c>: the command completed, but left something out or may have got a version wrong.</summary>
    public const int ExitStrict = 5;
    /// <summary><c>verify</c>: a library's package is missing from the local feed, or older than its csproj or its files.</summary>
    public const int ExitVerifyFailed = 6;

    private const string StrictFlag = "--strict";

    /// <summary>Options every command takes, accepted before the command name too (<see cref="Log.LevelOption"/> with its value).</summary>
    private static readonly string[] GlobalFlags = [OutputMode.JsonFlag, StrictFlag];

    /// <summary>Name of the installed command, used in messages and help.</summary>
    public const string CommandName = "lrun";

    /// <summary>
    /// Folder of the current directory holding every file LocalRun writes by default, so one line ignores them all;
    /// it holds its own <c>.gitignore</c> anyway.
    /// </summary>
    internal const string WorkFolder = ".lrun";

    internal static readonly string DefaultConfigPath = Path.Combine(WorkFolder, "localrun.config.json");
    internal static readonly string DefaultRepackConfigPath = Path.Combine(WorkFolder, "repack.config.json");
    internal static readonly string DefaultVisualsFolder = Path.Combine(WorkFolder, "plots");
    internal static readonly string DefaultLogsFolder = Path.Combine(WorkFolder, "logs");

    private static readonly ILogger logger = Log.For(typeof(LocalRunCli).FullName!);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>The package version, with the commit it was built from (e.g. <c>0.1.0+1a2b3c4…</c>).</summary>
    internal static string Version =>
        typeof(LocalRunCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// The release this build is or comes before (<c>0.6.0</c> for <c>0.6.0-local-0012-1a2b3c4+…</c>): what the agent
    /// instructions are stamped with and what <c>changelog</c> calls the unreleased changes.
    /// </summary>
    internal static string ReleaseVersion =>
        NuGetVersion.TryParse(Version, out var version)
            ? $"{version.Major}.{version.Minor}.{version.Patch}"
            : typeof(LocalRunCli).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static string DefaultLocalNugetPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".localnuget");

    /// <param name="args">
    /// The command name followed by its options, e.g. <c>["plot", "--open"]</c>. The global options (<c>--json</c>,
    /// <c>--strict</c>, <c>--log-level</c>) may also come first. <c>--json</c> and <c>--log-level</c> only take effect once
    /// <c>Main</c> has set <see cref="OutputMode.Json"/> and <see cref="Log.MinimumLevel"/>, before anything logs.
    /// </param>
    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        // A caller running several commands in one process gets each one's own
        int diagnosticsBefore = Log.Diagnostics.Count;
        IReadOnlyList<Diagnostic> DiagnosticsOfThisRun() => [.. Log.Diagnostics.Skip(diagnosticsBefore)];

        int leadingFlags = CountLeadingGlobalOptions(args);
        string? cmd = leadingFlags < args.Length ? args[leadingFlags].ToLowerInvariant() : null;
        var output = new CommandOutput();
        int exitCode;
        ReportError? error = null;
        // Taken before the failure is logged, so the report doesn't carry it twice
        IReadOnlyList<Diagnostic>? diagnostics = null;
        try
        {
            if (cmd == null)
            {
                ShowHelp(output);
                exitCode = ExitUsage;
                error = new(ErrorCodes.Usage, "No command given.");
            }
            else
            {
                // The global options given first go last, so the command's own arguments still come before any option
                var options = new CommandLineArgs(args.Skip(leadingFlags + 1).Concat(args.Take(leadingFlags)));
                // Main has read --json already: consumed here so that a repeated or valued one is rejected like any option
                options.Flag(OutputMode.JsonFlag);
                string? logLevel = options.Value(Log.LevelOption);
                if (logLevel != null && Log.ParseLevel(logLevel) == null)
                    throw new UsageException($"'{Log.LevelOption}' must be one of {string.Join(", ", Log.LevelNames)} (got '{logLevel}').");
                bool strict = options.Flag(StrictFlag);
                await RunCommandAsync(cmd, options, output);
                (exitCode, error) = strict ? CheckStrict(DiagnosticsOfThisRun()) : (ExitSuccess, null);
                if (error != null)
                {
                    diagnostics = DiagnosticsOfThisRun();
                    logger.LogError(LogEvents.Failed, "{Error}", error.Message);
                }
            }
        }
        catch (Exception ex)
        {
            (string code, exitCode) = ErrorCodes.Of(ex);
            string? hint = ErrorHint.Of(ex);
            error = new(code, ex.Message, hint);
            diagnostics = DiagnosticsOfThisRun();
            if (ex is UsageException)
            {
                logger.LogError("{Error} Run '{Command} help' for usage.", ex.Message, CommandName);
            }
            else
            {
                logger.LogError(LogEvents.Failed, "{Command} failed: {Error}", cmd, ex.Message);
                // On a line of its own, marked, after the failure: the next thing to do
                if (hint != null)
                    logger.LogInformation(LogEvents.Hint, "{Hint}", hint);
                logger.LogDebug(ex, "Command '{Command}' failure details", cmd);
            }
        }

        if (OutputMode.Json)
        {
            var report = new CommandReport(CommandReport.CurrentSchemaVersion, cmd, exitCode == ExitSuccess ? "succeeded" : "failed",
                exitCode, stopwatch.ElapsedMilliseconds, output.Result, diagnostics ?? DiagnosticsOfThisRun(), output.Written, error);
            // up streams events first, a line each: its document is a line too, whether it got to stream or not
            Console.Out.WriteLine(report.ToJson(indented: cmd != "up"));
            Console.Out.Flush();
        }
        return exitCode;
    }

    /// <summary>How many arguments before the command name are global options (<see cref="Log.LevelOption"/> counts its value).</summary>
    private static int CountLeadingGlobalOptions(string[] args)
    {
        int count = 0;
        while (count < args.Length)
        {
            if (GlobalFlags.Contains(args[count], StringComparer.Ordinal))
                count++;
            else if (args[count] == Log.LevelOption && count + 1 < args.Length)
                count += 2;
            else
                break;
        }
        return count;
    }

    /// <summary>
    /// <c>--strict</c>: fails a completed command whose diagnostics say its result left something out or may be wrong
    /// (<see cref="DiagnosticEvents.Strict"/>). The files are written all the same, to look at.
    /// </summary>
    internal static (int ExitCode, ReportError? Error) CheckStrict(IReadOnlyList<Diagnostic> diagnostics)
    {
        var codes = diagnostics.Where(diagnostic => DiagnosticEvents.Strict.Contains(diagnostic.Code))
            .GroupBy(diagnostic => diagnostic.Code)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} ({group.Count()})")
            .ToList();
        return codes.Count == 0
            ? (ExitSuccess, null)
            : (ExitStrict, new ReportError(ErrorCodes.Strict, $"--strict: the result is incomplete or may be wrong: {string.Join(", ", codes)}."));
    }

    internal static async Task RunCommandAsync(string cmd, CommandLineArgs options, CommandOutput output)
    {
        switch (cmd)
        {
            case "build":
                await BuildAsync(options, output);
                break;
            case "list":
                List(options, output);
                break;
            case "verify":
                await VerifyAsync(options, output);
                break;
            case "status":
                await StatusAsync(options, output);
                break;
            case "plot":
                await PlotAsync(options, output);
                break;
            case "pack":
                await PackAsync(options, output);
                break;
            case "go":
                await GoAsync(options, output);
                break;
            case "up":
                await RunExecutablesAsync(options, output);
                break;
            case "down":
                await DownAsync(options, output);
                break;
            case "settings":
                Settings(options, output);
                break;
            case "doctor":
                await DoctorAsync(options, output);
                break;
            case "schema":
                Schema(options, output);
                break;
            case "changelog":
                ShowChangelog(options, output);
                break;
            case "help" or "--help" or "-h":
                bool agent = options.Flag("--agent");
                options.EnsureAllConsumed();
                // With --json, the document carries the agent instructions anyway
                if (agent && !OutputMode.Json)
                    Console.Write(AgentInstructions.Text(ReleaseVersion));
                else
                    ShowHelp(output);
                break;
            case "version" or "--version":
                options.EnsureAllConsumed();
                if (OutputMode.Json)
                    output.Result = new VersionResult(Version);
                else
                    Console.WriteLine($"{CommandName} {Version}");
                break;
            default:
                throw new UsageException($"Unknown command '{cmd}'.");
        }
    }

    private static void ShowHelp(CommandOutput output)
    {
        if (OutputMode.Json)
        {
            using var help = new StringWriter();
            WriteHelp(help);
            output.Result = new HelpResult(help.ToString(), CommandCatalog.GlobalOptions, CommandCatalog.Commands, CommandCatalog.ExitCodes,
                ErrorCodes.All, DiagnosticEvents.All, AgentInstructions.Text(ReleaseVersion));
        }
        else
        {
            WriteHelp(Console.Out);
        }
    }

    /// <summary>The embedded changelog, all of it or the releases after <c>--since</c>: as written, or as data with <c>--json</c>.</summary>
    private static void ShowChangelog(CommandLineArgs args, CommandOutput output)
    {
        string? sinceText = args.Value("--since");
        args.EnsureAllConsumed();
        NuGetVersion? since = null;
        if (sinceText != null && !NuGetVersion.TryParse(sinceText, out since))
            throw new UsageException($"'--since' takes a version, e.g. 0.5.1 (got '{sinceText}').");

        var releases = Changelog.Since(Changelog.Parse(Changelog.Text(), ReleaseVersion), since);
        if (OutputMode.Json)
            output.Result = new ChangelogResult(sinceText, releases);
        else if (releases.Count == 0)
            logger.LogInformation("No changes after {Since} in {Command} {Version}.", sinceText, CommandName, Version);
        else
            Console.WriteLine(string.Join("\n\n", releases.Select(release => release.Markdown)));
    }

    private static async Task BuildAsync(CommandLineArgs args, CommandOutput output)
    {
        string? root = args.Value("--root");
        string? localNuget = args.Value("--local-nuget");
        string configPath = Path.GetFullPath(args.Value("--config") ?? DefaultConfigPath);
        BuildConfigOptions configOptions = BuildConfigOptionsParser.Parse(args);
        bool plot = args.Flag("--plot");
        bool verify = args.Flag("--verify");
        args.EnsureAllConsumed();

        UserSettings settings = UserSettings.Load();
        string rootPath = RootPath(root, settings);
        string localNugetPath = Path.GetFullPath(localNuget ?? settings.LocalNuget ?? DefaultLocalNugetPath);

        var configBuilder = new ConfigBuilder();
        var (localRunConfig, summary) = configBuilder.BuildConfig(rootPath, configOptions, localNugetPath);
        localRunConfig.Build = ConfigBuilder.Record(rootPath, configOptions, summary, Version, DateTimeOffset.Now);
        if (!configOptions.CsprojVersionsOnly)
            await configBuilder.FetchLatestVersionsAsync(localRunConfig);
        await WriteToFile(configPath, localRunConfig);
        output.Written.Add(configPath);

        var services = localRunConfig.Projects.Where(proj => proj.Type == "executable").Select(proj => proj.Name).ToList();
        logger.LogInformation("Services: {Services}", services.Count == 0 ? "none" : Text.Abbreviate(services));
        logger.LogInformation("Config: {Path}", configPath);
        // As 'plot' with its defaults, from the config just written. Opened for someone at a terminal only: an agent or a
        // CI job can't use a browser window popping up on its machine
        string? plotPath = null;
        if (plot)
            plotPath = await PlotGraphAsync(localRunConfig, UniqueVisualsPath(DefaultVisualsFolder, DateTime.Now), open: Terminal.IsAttached, output);
        else
            logger.LogInformation("Next: {Command} pack", CommandName);

        var verifyResult = verify ? VerifyResult.Of(configPath, localNugetPath, FeedVerifier.Verify(localRunConfig)) : null;
        output.Result = new BuildResult(configPath, rootPath, localNugetPath, ConfigBuilder.Filters(configOptions), summary.Scanned,
            new ProjectCounts(localRunConfig.Projects.Count, localRunConfig.Projects.Count - services.Count, services.Count),
            services, summary.LeftOut, summary.GitChanges, plotPath, verifyResult);
        verifyResult?.ThrowIfFailed();
    }

    /// <summary>Whether the local feed holds an up-to-date package of every library of the config (exit code 6 if not).</summary>
    private static async Task VerifyAsync(CommandLineArgs args, CommandOutput output)
    {
        string configPath = Path.GetFullPath(args.Value("--config") ?? DefaultConfigPath);
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        // Only for what go found produced no package: a plan is optional
        RepackConfig? plan = File.Exists(repackConfigPath)
            ? await ReadFromFile<RepackConfig>(repackConfigPath, "--repack-config", "pack")
            : null;
        var result = VerifyResult.Of(configPath, config.LocalNugetPath, FeedVerifier.Verify(config, plan));
        output.Result = result;
        result.ThrowIfFailed();
    }

    /// <summary>
    /// What a config holds, with the plan made from it: read from the files alone (no scan, no NuGet source, no git), so it
    /// shows what <c>pack</c>, <c>go</c> and <c>up</c> will work from. Without a plan, says so; a plan older than the config warns.
    /// </summary>
    private static async Task StatusAsync(CommandLineArgs args, CommandOutput output)
    {
        string configPath = Path.GetFullPath(args.Value("--config") ?? DefaultConfigPath);
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        RepackConfig? plan = File.Exists(repackConfigPath)
            ? await ReadFromFile<RepackConfig>(repackConfigPath, "--repack-config", "pack")
            : null;
        var result = StatusResult.Of(configPath, File.GetLastWriteTime(configPath), config,
            plan == null ? null : (repackConfigPath, File.GetLastWriteTime(repackConfigPath), plan),
            UpState.ReadRunning(UpState.PathFor(configPath)));
        StatusText.Print(result);
        output.Result = result;
    }

    /// <summary>
    /// Setup checks, before anything runs: SDK, settings, root, local feed, NuGet sources, git. Takes <c>build</c>'s root
    /// and feed, so it works before the first <c>build</c>. Fails (exit code 1) when a check fails, not on a warning.
    /// </summary>
    private static async Task DoctorAsync(CommandLineArgs args, CommandOutput output)
    {
        string? root = args.Value("--root");
        string? localNuget = args.Value("--local-nuget");
        args.EnsureAllConsumed();

        // An invalid settings file is a finding here, not a failure: the other checks go on with the defaults
        var (settingsCheck, settings) = Doctor.CheckSettings();
        string rootPath = Path.GetFullPath(root ?? settings.Root ?? Directory.GetCurrentDirectory());
        string localNugetPath = Path.GetFullPath(localNuget ?? settings.LocalNuget ?? DefaultLocalNugetPath);
        logger.LogInformation(LogEvents.Progress, "Checking {Root}...", rootPath);
        List<DoctorCheck> checks = [settingsCheck, .. await Doctor.RunAsync(rootPath, localNugetPath)];

        // One line per kind of check when all is well (a platform has dozens of repositories), one per problem
        foreach (var kind in checks.GroupBy(check => check.Check))
        {
            var ok = kind.Where(check => check.Status == Doctor.Ok).ToList();
            if (ok.Count == 1)
                logger.LogInformation(LogEvents.Done, "{Check} {Subject}: {Message}", kind.Key, ok[0].Subject, ok[0].Message);
            else if (ok.Count > 1)
                logger.LogInformation(LogEvents.Done, "{Check}: {Count} ok", kind.Key, ok.Count);
            foreach (var check in kind.Where(check => check.Status != Doctor.Ok))
            {
                string hint = check.Hint == null ? "" : $". {check.Hint}";
                if (check.Status == Doctor.Fail)
                    logger.LogError(DiagnosticEvents.DoctorFailure, "{Check} {Subject}: {Message}{Hint}", check.Check, check.Subject, check.Message, hint);
                else
                    logger.LogWarning(DiagnosticEvents.DoctorWarning, "{Check} {Subject}: {Message}{Hint}", check.Check, check.Subject, check.Message, hint);
            }
        }

        var result = DoctorResult.Of(rootPath, localNugetPath, checks);
        logger.LogInformation(result.Fail == 0 ? LogEvents.Done : new EventId(), "{Ok} ok, {Warnings}, {Failures}",
            result.Ok, Text.Count(result.Warn, "warning"), Text.Count(result.Fail, "failure"));
        output.Result = result;
        result.ThrowIfFailed();
    }

    /// <summary>The directory to scan: the option, else the settings, else the current directory; it must exist.</summary>
    private static string RootPath(string? root, UserSettings settings)
    {
        string rootPath = Path.GetFullPath(root ?? settings.Root ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(rootPath))
            throw new RootNotFoundException($"Root directory not found: {rootPath}");
        return rootPath;
    }

    /// <summary>
    /// What <c>build</c> would keep with the same filters, printed instead of written: a question about the graph
    /// ("what uses X?") that leaves the config alone. No NuGet source is queried.
    /// </summary>
    private static void List(CommandLineArgs args, CommandOutput output)
    {
        string? root = args.Value("--root");
        BuildConfigOptions options = BuildConfigOptionsParser.Parse(args);
        args.EnsureAllConsumed();

        string rootPath = RootPath(root, UserSettings.Load());
        var selection = new ConfigBuilder().SelectProjects(rootPath, options);

        // The list is the result: printed in full, one line per project under its depth
        var depths = selection.Projects.GroupBy(proj => selection.Depths[proj.Name]).ToList();
        int nameWidth = selection.Projects.Select(proj => proj.Name.Length).DefaultIfEmpty().Max();
        for (int i = 0; i < depths.Count; i++)
        {
            logger.LogInformation("Depth {Depth}/{Depths}:", i + 1, depths.Count);
            foreach (var proj in depths[i])
            {
                logger.LogInformation("  {Project}  {Type,-10}  {Version,-14}  {Solution}", proj.Name.PadRight(nameWidth), proj.Type,
                    proj.Version.AsVersionString(), proj.SolutionPath == null ? "(no solution)" : Path.GetFileName(proj.SolutionPath));
            }
        }
        output.Result = ListResult.Of(rootPath, ConfigBuilder.Filters(options), selection);
    }

    private static async Task PlotAsync(CommandLineArgs args, CommandOutput output)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string outputPath = args.Value("--output") ?? UniqueVisualsPath(DefaultVisualsFolder, DateTime.Now);
        bool open = args.Flag("--open");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        output.Result = new PlotResult(await PlotGraphAsync(config, outputPath, open, output));
    }

    /// <returns>The full path of the page.</returns>
    private static async Task<string> PlotGraphAsync(LocalRunConfig config, string outputPath, bool open, CommandOutput output)
    {
        PrepareFolderOf(Path.GetFullPath(outputPath));
        string htmlPath = Path.GetFullPath(await GraphPage.GenerateAsync(config, outputPath));
        output.Written.Add(htmlPath);
        logger.LogInformation("Plot: {Path}", htmlPath);
        if (open)
            OpenWithDefaultApp(htmlPath);
        return htmlPath;
    }

    /// <summary>
    /// <c>&lt;folder&gt;/dependencies-&lt;yyyyMMdd-HHmmss&gt;</c> (without extension), so successive graphs are kept side by
    /// side; a counter is appended when that name is already taken within the same second.
    /// </summary>
    internal static string UniqueVisualsPath(string folder, DateTime now)
    {
        string basePath = Path.Combine(folder, $"dependencies-{now:yyyyMMdd-HHmmss}");
        string path = basePath;
        for (int counter = 2; File.Exists($"{path}.html"); counter++)
            path = $"{basePath}-{counter}";
        return path;
    }

    private static async Task PackAsync(CommandLineArgs args, CommandOutput output)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        // The settings only supply the default suffix; --suffix wins
        RepackOptions repackOptions = RepackOptionsParser.Parse(args, UserSettings.Load().Suffix);
        bool dryRun = args.Flag("--dry-run");
        if (args.Flag("--go"))
            throw new UsageException($"'--go' was removed in 0.4.0: '{CommandName} pack' now packs, '{CommandName} pack --dry-run' only writes the plan.");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        RepackConfig repackConfig = await new RepackConfigBuilder().BuildRepackConfig(config, repackOptions);
        // Written before anything runs, dry run or not, so a failed or surprising repack can be inspected afterwards
        await WriteToFile(repackConfigPath, repackConfig);
        output.Written.Add(repackConfigPath);
        logger.LogInformation("Plan: {Path}", repackConfigPath);

        if (dryRun)
        {
            new RepackExecutor().DescribePlan(repackConfig);
            output.Result = PackResult.Of(repackConfigPath, packed: false, repackConfig, outcomes: null);
            logger.LogInformation("Dry run, nothing packed. Next: {Command} go (after editing the plan, if needed)", CommandName);
        }
        else
        {
            await ExecutePlanAsync(repackConfig, repackConfigPath, output);
        }
    }

    private static async Task GoAsync(CommandLineArgs args, CommandOutput output)
    {
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        args.EnsureAllConsumed();

        RepackConfig repackConfig = await ReadFromFile<RepackConfig>(repackConfigPath, "--repack-config", "pack");
        // pack says so when planning; a plan emptied by hand needs saying here
        if (repackConfig.RepackTrees.All(tree => tree.Projects.Count == 0))
            logger.LogWarning(DiagnosticEvents.EmptySelection, "Nothing to pack: the plan holds no project.");
        await ExecutePlanAsync(repackConfig, repackConfigPath, output);
    }

    /// <summary>
    /// Packs a plan; the result says what became of each project, after a failure too. A project packed under another
    /// version than planned has the version packed written into the plan, one that produced no package is marked
    /// <c>NotPacked</c> there, so that the plan and the result say what the feed holds.
    /// </summary>
    private static async Task ExecutePlanAsync(RepackConfig repackConfig, string repackConfigPath, CommandOutput output)
    {
        var executor = new RepackExecutor(PackExcludeHint);
        bool packed = false;
        try
        {
            await executor.RepackAndRebuildAsync(repackConfig);
            packed = true;
        }
        finally
        {
            if (executor.RecordPackedVersions(repackConfig))
            {
                await WriteToFile(repackConfigPath, repackConfig);
                if (!output.Written.Contains(repackConfigPath))
                    output.Written.Add(repackConfigPath);
                logger.LogInformation("Plan updated with what was packed: {Path}", repackConfigPath);
            }
            output.Result = PackResult.Of(repackConfigPath, packed, repackConfig, executor.Outcomes);
        }
    }

    private static async Task RunExecutablesAsync(CommandLineArgs args, CommandOutput output)
    {
        string? configOption = args.Value("--config");
        string configPath = configOption ?? DefaultConfigPath;
        List<string>? exclude = args.Values("--exclude");
        List<string>? only = args.Values("--only");
        string? logsOption = args.Value("--logs");
        string logsFolder = Path.GetFullPath(logsOption ?? DefaultLogsFolder);
        string? stopTimeoutOption = args.Value("--stop-timeout");
        args.EnsureAllConsumed();
        string CommandExcluding(IReadOnlyCollection<string> failed) =>
            UpCommandLine(configOption, only, [.. exclude ?? [], .. failed], logsOption, stopTimeoutOption);
        int? stopTimeout = stopTimeoutOption == null ? null
            : int.TryParse(stopTimeoutOption, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ? seconds
            : throw new UsageException($"'--stop-timeout' must be a number of seconds, 0 or more (got '{stopTimeoutOption}').");

        stopTimeout ??= UserSettings.Load().StopTimeout;

        // One up per config folder: a second would build over the running services and clash on their ports
        string statePath = UpState.PathFor(configPath);
        if (UpState.ReadRunning(statePath) is { } other)
        {
            throw new UpRunningException($"{CommandName} up is already running for this config (pid {other.Pid}, started " +
                $"{other.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}): run '{CommandName} down' to stop it first.");
        }
        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        // Creates the folder, and the work folder's .gitignore when the logs are in it
        PrepareFolderOf(Path.Combine(logsFolder, "_"));
        // With --json, events stream on stdout as they happen: an agent sees the services start without waiting for the end
        var events = OutputMode.Json ? new EventStream(Console.Out) : null;
        var state = new UpState(Environment.ProcessId, UpState.CurrentProcessStarted(), Path.GetFullPath(configPath), logsFolder,
            (int)(stopTimeout == null ? RunProcessor.DefaultStopGracePeriod.TotalSeconds : stopTimeout.Value), []);
        bool stateDeleted = false;
        void Record(Func<IReadOnlyList<UpStateService>, IReadOnlyList<UpStateService>> change)
        {
            lock (statePath)
            {
                // A late exit, once up is done, must not write the file again
                if (stateDeleted)
                    return;
                state = state with { Services = change(state.Services) };
                state.Write(statePath);
            }
        }
        void OnEvent(UpEvent upEvent)
        {
            events?.Write(upEvent);
            if (upEvent is { Event: UpEvents.Started, Project: { } project, Pid: { } pid })
                Record(services => [.. services, new UpStateService(project, pid, upEvent.Log)]);
            // So that status doesn't show a one-shot job, or a service that failed, as running
            else if (upEvent is { Event: UpEvents.Exited, Project: { } exited, ExitCode: { } exitCode })
                Record(services => [.. services.Select(service => service.Project == exited ? service with { ExitCode = exitCode } : service)]);
        }
        var processor = new RunProcessor(logsFolder, OnEvent, stopTimeout == null ? null : TimeSpan.FromSeconds(stopTimeout.Value),
            CommandExcluding);
        state.Write(statePath);
        // lrun down sets it: the same stop as Ctrl+C
        using var stopEvent = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.ManualReset, UpState.StopEventName(Environment.ProcessId))
            : null;
        var stopWait = stopEvent == null ? null
            : ThreadPool.RegisterWaitForSingleObject(stopEvent, (_, _) => processor.RequestStop(), null, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            await processor.RunAsync(config, exclude, only);
        }
        finally
        {
            stopWait?.Unregister(null);
            lock (statePath)
            {
                state.Delete(statePath);
                stateDeleted = true;
            }
            events?.Close();
            var services = processor.Services.Select(entry => new ServiceReport(entry.Key, entry.Value.Status, entry.Value.ExitCode,
                entry.Value.Status == ServiceStatus.NotStarted ? null : entry.Value.Log, entry.Value.Failure)).ToList();
            output.Result = new UpResult(logsFolder, services);
            output.Written.AddRange(services.Select(service => service.Log).OfType<string>());
        }
    }

    /// <summary>
    /// An <c>up</c> command line with these options, as a failure suggests it: the options of the run (global ones aside),
    /// each excluded name once. A value holding a space or a quote is quoted.
    /// </summary>
    internal static string UpCommandLine(string? config, IReadOnlyCollection<string>? only, IReadOnlyCollection<string> exclude,
        string? logs, string? stopTimeout)
    {

        var line = new List<string> { CommandName, "up" };
        if (config != null)
            line.AddRange(["--config", Quote(config)]);
        if (only is { Count: > 0 })
            line.AddRange(["--only", .. only.Select(Quote)]);
        var excluded = exclude.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (excluded.Count != 0)
            line.AddRange(["--exclude", .. excluded.Select(Quote)]);
        if (logs != null)
            line.AddRange(["--logs", Quote(logs)]);
        if (stopTimeout != null)
            line.AddRange(["--stop-timeout", stopTimeout]);
        return string.Join(' ', line);
    }

    /// <summary>A command-line value as typed in a shell: quoted when it holds a space or a quote.</summary>
    private static string Quote(string value) => value.Length != 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')
        ? value
        : $"\"{value.Replace("\"", "\\\"")}\"";

    /// <summary>
    /// The way past projects <c>pack</c> / <c>go</c> failed on: leave them out of the config, then plan again. The plan
    /// doesn't say which <c>build</c> made the config, by design (files say what is, not how it was reached), so the hint
    /// names the option to add, not a whole command line.
    /// </summary>
    internal static string PackExcludeHint(IReadOnlyCollection<string> failed) =>
        $"To pack the others: add '--exclude {string.Join(' ', failed.Select(Quote))}' to your '{CommandName} build', then run '{CommandName} pack' again.";

    /// <summary>How long <c>down</c> waits beyond <c>up</c>'s grace period: the kills, the last output lines, the result.</summary>
    private static readonly TimeSpan DownMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Stops the <c>up</c> started with a config, as Ctrl+C in its console would, and waits until it has exited. Without one
    /// running there is nothing to stop: a success. Windows only, as the stop request is a named event.
    /// </summary>
    private static async Task DownAsync(CommandLineArgs args, CommandOutput output)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        args.EnsureAllConsumed();

        string statePath = UpState.PathFor(configPath);
        var state = UpState.ReadRunning(statePath);
        if (state == null)
        {
            if (UpState.DeleteIfLeftBehind(statePath))
                logger.LogInformation("Deleted {State}, left by an {Command} up that was killed.", statePath, CommandName);
            logger.LogInformation("No {Command} up running for {Config}: nothing to stop.", CommandName, Path.GetFullPath(configPath));
            output.Result = new DownResult(statePath, null, false);
            return;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException($"down is Windows only: stop {CommandName} up (pid {state.Pid}) with Ctrl+C or a signal.");
        if (!EventWaitHandle.TryOpenExisting(UpState.StopEventName(state.Pid), out var stopEvent))
        {
            throw new InvalidOperationException($"{CommandName} up (pid {state.Pid}) doesn't listen for a stop request " +
                "(started by an older lrun?): stop it with Ctrl+C in its console.");
        }

        logger.LogInformation(LogEvents.Start, "Stopping {Command} up (pid {Pid})", CommandName, state.Pid);
        var limit = TimeSpan.FromSeconds(state.StopTimeout) + DownMargin;
        try
        {
            using var up = Process.GetProcessById(state.Pid);
            using (stopEvent)
                stopEvent.Set();
            await up.WaitForExitAsync().WaitAsync(limit);
        }
        catch (ArgumentException)
        {
            // Exited in the meantime
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"{CommandName} up (pid {state.Pid}) did not exit within {limit.TotalSeconds:0} s.");
        }
        logger.LogInformation(LogEvents.Done, "Stopped {Command} up (pid {Pid})", CommandName, state.Pid);
        output.Result = new DownResult(statePath, state, true);
    }

    /// <summary>Prints a JSON Schema as is, to save or pipe; with <c>--json</c>, the schema is the result's <c>schema</c>.</summary>
    private static void Schema(CommandLineArgs args, CommandOutput output)
    {
        string? name = args.Argument();
        args.EnsureAllConsumed();
        if (name == null || !Schemas.Names.Contains(name))
        {
            string got = name == null ? "" : $" (got '{name}')";
            throw new UsageException($"'schema' takes the name of a schema: {string.Join(", ", Schemas.Names)}{got}.");
        }

        if (OutputMode.Json)
            output.Result = new SchemaResult(name, Schemas.Of(name));
        else
            Console.Write(Schemas.Text(name));
    }

    private static void Settings(CommandLineArgs args, CommandOutput output)
    {
        bool pathOnly = args.Flag("--path");
        args.EnsureAllConsumed();

        string path = UserSettings.DefaultPath;
        if (pathOnly)
        {
            string? pathProblem = SettingsProblem(path);
            if (OutputMode.Json)
                output.Result = new SettingsResult(path, Created: false, pathProblem == null, pathProblem);
            else
                Console.WriteLine(path);
            return;
        }

        bool created = UserSettings.CreateIfMissing(path);
        string? problem = SettingsProblem(path);
        output.Result = new SettingsResult(path, created, problem == null, problem);
        if (created)
        {
            output.Written.Add(path);
            logger.LogInformation(LogEvents.Done, "Created {Path}", path);
        }
        else
        {
            logger.LogInformation("Settings: {Path}", path);
        }

        // A machine can have no app for .json: notepad is always there on Windows
        if (!OpenWithDefaultApp(path) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { Process.Start("notepad.exe", $"\"{path}\""); }
            catch (Exception ex) { logger.LogWarning(DiagnosticEvents.OpenFailed, "Could not open notepad: {Error}", ex.Message); }
        }
    }

    /// <summary>
    /// Why the settings file can't be used, warned (<c>settings-invalid</c>), or null when it can or doesn't exist. The
    /// commands that read it fail on it and send you to <c>settings</c>: that's where you see whether your fix holds.
    /// </summary>
    private static string? SettingsProblem(string path)
    {
        try
        {
            UserSettings.Load(path);
            return null;
        }
        catch (SettingsInvalidException ex)
        {
            logger.LogWarning(DiagnosticEvents.SettingsInvalid, "{Problem}", ex.Message);
            return ex.Message;
        }
    }

    private static async Task<T> ReadFromFile<T>(string pathToFile, string option, string producingCommand)
        where T : class, IConfigFile
    {
        string fullPath = Path.GetFullPath(pathToFile);
        if (!File.Exists(fullPath))
        {
            // Before 0.3.0, the default files were written in the current directory itself
            string legacy = Path.GetFullPath(Path.GetFileName(fullPath));
            string hint = Path.GetDirectoryName(fullPath) == Path.GetFullPath(WorkFolder) && File.Exists(legacy)
                ? $" Found '{legacy}', written by an older {CommandName}: move it into '{WorkFolder}', or point at it with {option}."
                : "";
            throw new ConfigNotFoundException($"'{fullPath}' not found. Run '{CommandName} {producingCommand}' first.{hint}");
        }
        string json = await File.ReadAllTextAsync(fullPath);
        T config;
        try
        {
            config = JsonSerializer.Deserialize<T>(json)
                ?? throw new ConfigInvalidException($"'{fullPath}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ConfigInvalidException($"'{fullPath}' is not a valid {producingCommand} output: {ex.Message} Run '{CommandName} {producingCommand}' again, or fix it.");
        }

        // A newer shape could read without error and mean something else: refuse it rather than guess
        int version = config.SchemaVersion;
        if (version > IConfigFile.CurrentSchemaVersion)
        {
            throw new ConfigInvalidException($"'{fullPath}' has SchemaVersion {version}, written by a newer {CommandName} " +
                $"(this one reads up to {IConfigFile.CurrentSchemaVersion}). Update {CommandName}, or run '{CommandName} {producingCommand}' again.");
        }
        if (version < 1)
            throw new ConfigInvalidException($"'{fullPath}' has SchemaVersion {version}, not a version. Run '{CommandName} {producingCommand}' again, or fix it.");

        // A hand edit with a typo would otherwise be ignored without a word; a warning keeps older files loading
        string schema = typeof(T) == typeof(RepackConfig) ? Schemas.Repack : Schemas.Config;
        using var document = JsonDocument.Parse(json);
        var unknown = Schemas.UnknownFields(Schemas.Of(schema), document.RootElement);
        if (unknown.Count != 0)
        {
            logger.LogWarning(DiagnosticEvents.UnknownConfigField, "'{Path}' has fields unknown to this version, ignored: {Fields}. " +
                "'{Command} schema {Schema}' lists the known ones; or run '{Command} {Producer}' again.", fullPath,
                Text.Abbreviate(unknown), CommandName, schema, CommandName, producingCommand);
        }
        return config;
    }

    private static async Task WriteToFile<T>(string fullPath, T content)
    {
        PrepareFolderOf(fullPath);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(content, WriteOptions));
    }

    /// <summary>
    /// Creates the folder of a file about to be written. Within the work folder of <paramref name="currentDirectory"/>
    /// (default: the current directory), also writes the work folder's <c>.gitignore</c> ignoring all of it, unless one is there.
    /// </summary>
    internal static void PrepareFolderOf(string fullPath, string? currentDirectory = null)
    {
        string folder = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(folder);

        string workFolder = Path.GetFullPath(Path.Combine(currentDirectory ?? Directory.GetCurrentDirectory(), WorkFolder));
        string relative = Path.GetRelativePath(workFolder, folder);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative))
            return;
        string gitignore = Path.Combine(workFolder, ".gitignore");
        if (!File.Exists(gitignore))
            File.WriteAllText(gitignore, $"# Written by {CommandName}: everything it writes here stays out of git.\n*\n");
    }

    /// <returns>Whether it was started.</returns>
    private static bool OpenWithDefaultApp(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", filePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", filePath);
            }
            else
            {
                logger.LogWarning(DiagnosticEvents.OpenFailed, "Unsupported platform: cannot open {Path}", filePath);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(DiagnosticEvents.OpenFailed, "Could not open {Path}: {Error}", filePath, ex.Message);
            return false;
        }
    }

    internal static void WriteHelp(TextWriter writer) => CommandCatalog.WriteHelp(writer);
}
