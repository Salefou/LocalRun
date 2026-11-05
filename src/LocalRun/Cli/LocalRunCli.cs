namespace LocalRun.Cli;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    /// <summary>Name of the installed command, used in messages and help.</summary>
    public const string CommandName = "lrun";

    /// <summary>
    /// Folder of the current directory holding every file LocalRun writes by default, so one line ignores them all;
    /// it holds its own <c>.gitignore</c> anyway.
    /// </summary>
    internal const string WorkFolder = ".lrun";

    private static readonly string DefaultConfigPath = Path.Combine(WorkFolder, "localrun.config.json");
    private static readonly string DefaultRepackConfigPath = Path.Combine(WorkFolder, "repack.config.json");
    private static readonly string DefaultVisualsFolder = Path.Combine(WorkFolder, "plots");

    private static readonly ILogger logger = Log.For(typeof(LocalRunCli).FullName!);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>The package version, with the commit it was built from (e.g. <c>0.1.0+1a2b3c4…</c>).</summary>
    internal static string Version =>
        typeof(LocalRunCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string DefaultLocalNugetPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".localnuget");

    /// <param name="args">
    /// The command name followed by its options, e.g. <c>["plot", "--open"]</c>. <c>--json</c> may also come first; it
    /// only switches to JSON output when <see cref="OutputMode.Json"/> is set (by <c>Main</c>, before anything logs).
    /// </param>
    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        if (args.Length > 0 && args[0] == OutputMode.JsonFlag)
            args = [.. args.Skip(1).Take(1), OutputMode.JsonFlag, .. args.Skip(2)];

        string? cmd = args.Length == 0 ? null : args[0].ToLowerInvariant();
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
                await RunCommandAsync(cmd, new CommandLineArgs(args.Skip(1)), output);
                exitCode = ExitSuccess;
            }
        }
        catch (UsageException ex)
        {
            exitCode = ExitUsage;
            error = new(ErrorCodes.Usage, ex.Message);
            diagnostics = Log.Diagnostics;
            logger.LogError("{Error} Run '{Command} help' for usage.", ex.Message, CommandName);
        }
        catch (Exception ex)
        {
            exitCode = ExitFailure;
            error = new(ErrorCodes.Of(ex), ex.Message);
            diagnostics = Log.Diagnostics;
            logger.LogError(LogEvents.Failed, "{Command} failed: {Error}", cmd, ex.Message);
            logger.LogDebug(ex, "Command '{Command}' failure details", cmd);
        }

        if (OutputMode.Json)
        {
            var report = new CommandReport(CommandReport.CurrentSchemaVersion, cmd, exitCode == ExitSuccess ? "succeeded" : "failed",
                exitCode, stopwatch.ElapsedMilliseconds, output.Result, diagnostics ?? Log.Diagnostics, output.Written, error);
            Console.Out.WriteLine(report.ToJson());
            Console.Out.Flush();
        }
        return exitCode;
    }

    private static async Task RunCommandAsync(string cmd, CommandLineArgs options, CommandOutput output)
    {
        // Main has read it already: consumed here so that a repeated or valued --json is rejected like any option
        options.Flag(OutputMode.JsonFlag);
        switch (cmd)
        {
            case "build":
                await BuildAsync(options, output);
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
                await RunExecutablesAsync(options);
                break;
            case "settings":
                Settings(options, output);
                break;
            case "help" or "--help" or "-h":
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
            output.Result = new HelpResult(help.ToString());
        }
        else
        {
            WriteHelp(Console.Out);
        }
    }

    private static async Task BuildAsync(CommandLineArgs args, CommandOutput output)
    {
        string? root = args.Value("--root");
        string? localNuget = args.Value("--local-nuget");
        string configPath = Path.GetFullPath(args.Value("--config") ?? DefaultConfigPath);
        BuildConfigOptions configOptions = BuildConfigOptionsParser.Parse(args);
        bool plot = args.Flag("--plot");
        args.EnsureAllConsumed();

        UserSettings settings = UserSettings.Load();
        string rootPath = Path.GetFullPath(root ?? settings.Root ?? Directory.GetCurrentDirectory());
        string localNugetPath = Path.GetFullPath(localNuget ?? settings.LocalNuget ?? DefaultLocalNugetPath);

        if (!Directory.Exists(rootPath))
            throw new RootNotFoundException($"Root directory not found: {rootPath}");

        var configBuilder = new ConfigBuilder();
        var (localRunConfig, summary) = configBuilder.BuildConfig(rootPath, configOptions, localNugetPath);
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

        output.Result = new BuildResult(configPath, rootPath, localNugetPath, ConfigBuilder.Filters(configOptions), summary.Scanned,
            new ProjectCounts(localRunConfig.Projects.Count, localRunConfig.Projects.Count - services.Count, services.Count),
            services, summary.LeftOut, summary.GitChanges, plotPath);
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
        await ExecutePlanAsync(repackConfig, repackConfigPath, output);
    }

    /// <summary>Packs a plan; the result says what became of each project, after a failure too.</summary>
    private static async Task ExecutePlanAsync(RepackConfig repackConfig, string repackConfigPath, CommandOutput output)
    {
        var executor = new RepackExecutor();
        bool packed = false;
        try
        {
            await executor.RepackAndRebuildAsync(repackConfig);
            packed = true;
        }
        finally
        {
            output.Result = PackResult.Of(repackConfigPath, packed, repackConfig, executor.Outcomes);
        }
    }

    private static async Task RunExecutablesAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        List<string>? exclude = args.Values("--exclude");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        await new RunProcessor().RunAsync(config, exclude);
    }

    private static void Settings(CommandLineArgs args, CommandOutput output)
    {
        bool pathOnly = args.Flag("--path");
        args.EnsureAllConsumed();

        string path = UserSettings.DefaultPath;
        if (pathOnly)
        {
            if (OutputMode.Json)
                output.Result = new SettingsResult(path, Created: false);
            else
                Console.WriteLine(path);
            return;
        }

        bool created = UserSettings.CreateIfMissing(path);
        output.Result = new SettingsResult(path, created);
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
            catch (Exception ex) { logger.LogWarning("Could not open notepad: {Error}", ex.Message); }
        }
    }

    private static async Task<T> ReadFromFile<T>(string pathToFile, string option, string producingCommand)
        where T : class
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
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(fullPath))
            ?? throw new InvalidDataException($"'{fullPath}' is empty.");
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
                logger.LogWarning("Unsupported platform: cannot open {Path}", filePath);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not open {Path}: {Error}", filePath, ex.Message);
            return false;
        }
    }

    internal static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("LocalRun — rebuild and run only what matters locally: scan repositories,");
        writer.WriteLine("track dependency graphs and repack components to a local NuGet source.");
        writer.WriteLine();
        writer.WriteLine($"Usage: {CommandName} <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Relative paths are resolved against the current directory.");
        writer.WriteLine("Exit codes: 0 success, 1 command failed, 2 invalid command line.");
        writer.WriteLine();
        writer.WriteLine("Every command also takes:");
        writer.WriteLine("  --json                        Write one JSON document on stdout (result, warnings and errors, files");
        writer.WriteLine("                                written), even when it fails; the logs go to stderr, plain");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine();

        writer.WriteLine("  build [options]");
        writer.WriteLine("      Scan a directory tree for projects and write their dependency graph to a config file.");
        writer.WriteLine("      Options:");
        writer.WriteLine("        --root <dir>            Directory to scan (default: your settings, else current directory)");
        writer.WriteLine("        --local-nuget <dir>     Local NuGet feed to repack into (default: your settings, else ~/.localnuget)");
        writer.WriteLine($"        --config <file>         Config file to write (default: {DefaultConfigPath})");
        writer.WriteLine("        --users-of <names>      Keep the given projects and all that depend on them, transitively");
        writer.WriteLine("        --deps-of <names>       Keep the given projects and all they depend on, transitively");
        writer.WriteLine("                                With both: only the projects between them (AND)");
        writer.WriteLine("        --git-changes           Add the projects with uncommitted changes or commits not on origin's");
        writer.WriteLine("                                default branch to --users-of (compares with your last fetch)");
        writer.WriteLine("        --union                 With both: users of --users-of OR deps of --deps-of");
        writer.WriteLine("        --with-services         Also keep the executables using any kept project, with the libraries in between");
        writer.WriteLine("        --include-external      Keep dependencies on projects that no solution references");
        writer.WriteLine("        --tests                 Keep the test projects (left out by default)");
        writer.WriteLine("        --csproj                Read the csproj files as they are: don't ask MSBuild for versions and package ids");
        writer.WriteLine("                                set elsewhere (props files, $(...)), and don't query your NuGet sources and local");
        writer.WriteLine("                                feed for the latest versions (pack then starts from the csproj versions)");
        writer.WriteLine("        --plot                  Then plot the graph, as 'plot' does with its defaults, and open it");
        writer.WriteLine("                                (only on a terminal: with output redirected, the page is just written)");
        writer.WriteLine();

        writer.WriteLine("  plot [options]");
        writer.WriteLine("      Render the dependency graph of a config file as an interactive HTML page: projects framed");
        writer.WriteLine("      by solution, collapsible into the solution graph, hover or click for users-of / deps-of.");
        writer.WriteLine("      Options:");
        writer.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        writer.WriteLine($"        --output <path>         Output path without extension (default: {DefaultVisualsFolder}/dependencies-<date-time>)");
        writer.WriteLine("        --open                  Open the page with the default browser");
        writer.WriteLine();

        writer.WriteLine("  pack [options]");
        writer.WriteLine("      Plan what will be repacked and to which versions, write the plan to a repack config, then pack");
        writer.WriteLine("      in dependency order into the local NuGet feed, as 'go' does.");
        writer.WriteLine("      Options:");
        writer.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        writer.WriteLine($"        --repack-config <file>  Repack config file to write (default: {DefaultRepackConfigPath})");
        writer.WriteLine("        --major                 Increment major version (resets minor/patch)");
        writer.WriteLine("        --minor                 Increment minor version (resets patch)");
        writer.WriteLine("        --patch                 Increment patch version");
        writer.WriteLine("        --next                  Continue a prerelease (1.5.0-alpha.9 -> 1.5.0-alpha.10), or patch a release");
        writer.WriteLine("        --alpha                 Shorthand for --next --suffix alpha");
        writer.WriteLine("        --suffix <text>         Set the prerelease label, without the dash (default: your settings, else local; e.g., 'dev', 'beta.2')");
        writer.WriteLine("                                An iteration number follows it: 1.4.3-dev.1, then .2 when packed again");
        writer.WriteLine("        --clean                 Clean projects before restoring them");
        writer.WriteLine("        --dry-run               Only write the plan and print what would run; 'go' runs it later");
        writer.WriteLine();

        writer.WriteLine("  go [options]");
        writer.WriteLine("      Pack projects in dependency order into the local NuGet feed, following a repack config");
        writer.WriteLine("      (from 'pack --dry-run', possibly edited by hand).");
        writer.WriteLine("      Options:");
        writer.WriteLine($"        --repack-config <file>  Repack config file to read (default: {DefaultRepackConfigPath})");
        writer.WriteLine();

        writer.WriteLine("  up [options]");
        writer.WriteLine("      Build the executables of a config file, then run them side by side; Ctrl+C stops them all.");
        writer.WriteLine("      Options:");
        writer.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        writer.WriteLine("        --exclude <names>       Executables not to run, e.g. the one you debug from your IDE");
        writer.WriteLine();

        writer.WriteLine("  settings [options]");
        writer.WriteLine("      Create your settings file if missing (all commented out), then open it. It holds what differs");
        writer.WriteLine("      from one user to the next: the local NuGet feed, the root to scan, the default suffix.");
        writer.WriteLine("      An option given on the command line always wins over it.");
        writer.WriteLine("      Options:");
        writer.WriteLine($"        --path                  Only print where the file is (default: ~/.lrun/settings.json, or ${UserSettings.PathVariable})");
        writer.WriteLine();

        writer.WriteLine("  help");
        writer.WriteLine("      Show this help message.");
        writer.WriteLine();

        writer.WriteLine("  version");
        writer.WriteLine("      Show the installed version (also --version). Please include it when reporting an issue.");
        writer.WriteLine();

        writer.WriteLine("Example:");
        writer.WriteLine($"  {CommandName} settings                            (once per machine: your local feed, your repos folder...)");
        writer.WriteLine($"  {CommandName} build --root ./repos --deps-of WebApp --plot");
        writer.WriteLine($"  {CommandName} plot --open                         (the same graph, opened again)");
        writer.WriteLine($"  {CommandName} pack --patch --suffix dev");
        writer.WriteLine($"  {CommandName} pack --patch --suffix dev --dry-run (review .lrun/repack.config.json, then:)");
        writer.WriteLine($"  {CommandName} go");
        writer.WriteLine($"  {CommandName} up");
    }
}
