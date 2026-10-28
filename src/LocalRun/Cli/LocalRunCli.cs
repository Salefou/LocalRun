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

    /// <param name="args">The command name followed by its options, e.g. <c>["plot", "--open"]</c>.</param>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return ExitUsage;
        }

        string cmd = args[0].ToLowerInvariant();
        // Listed again at the end, so they aren't buried under the output of the work that followed
        List<string> written = [];
        try
        {
            var options = new CommandLineArgs(args.Skip(1));
            switch (cmd)
            {
                case "build":
                    await BuildAsync(options, written);
                    break;
                case "plot":
                    await PlotAsync(options, written);
                    break;
                case "pack":
                    await PackAsync(options, written);
                    break;
                case "go":
                    await GoAsync(options);
                    break;
                case "up":
                    await RunExecutablesAsync(options);
                    break;
                case "settings":
                    Settings(options, written);
                    break;
                case "help" or "--help" or "-h":
                    PrintHelp();
                    break;
                case "version" or "--version":
                    options.EnsureAllConsumed();
                    Console.WriteLine($"{CommandName} {Version}");
                    break;
                default:
                    throw new UsageException($"Unknown command '{cmd}'.");
            }
            return ExitSuccess;
        }
        catch (UsageException ex)
        {
            logger.LogError("{Error} Run '{Command} help' for usage.", ex.Message, CommandName);
            return ExitUsage;
        }
        catch (Exception ex)
        {
            logger.LogError("Command '{Command}' failed: {Error}", cmd, ex.Message);
            logger.LogDebug(ex, "Command '{Command}' failure details", cmd);
            return ExitFailure;
        }
        finally
        {
            PrintWritten(written);
        }
    }

    private static async Task BuildAsync(CommandLineArgs args, List<string> written)
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
            throw new DirectoryNotFoundException($"Root directory not found: {rootPath}");

        var configBuilder = new ConfigBuilder();
        LocalRunConfig localRunConfig = configBuilder.BuildConfig(rootPath, configOptions, localNugetPath);
        if (!configOptions.CsprojVersionsOnly)
            await configBuilder.FetchLatestVersionsAsync(localRunConfig);
        await WriteToFile(configPath, localRunConfig);
        written.Add(configPath);
        logger.LogInformation("[✔ ] Configuration written to {Path}", configPath);

        // As 'plot' with its defaults, from the config just written
        if (plot)
            await PlotGraphAsync(localRunConfig, UniqueVisualsPath(DefaultVisualsFolder, DateTime.Now), open: false, written);
    }

    private static async Task PlotAsync(CommandLineArgs args, List<string> written)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string outputPath = args.Value("--output") ?? UniqueVisualsPath(DefaultVisualsFolder, DateTime.Now);
        bool open = args.Flag("--open");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        await PlotGraphAsync(config, outputPath, open, written);
    }

    private static async Task PlotGraphAsync(LocalRunConfig config, string outputPath, bool open, List<string> written)
    {
        PrepareFolderOf(Path.GetFullPath(outputPath));
        string htmlPath = await GraphPage.GenerateAsync(config, outputPath);
        written.Add(htmlPath);
        if (open)
            OpenWithDefaultApp(htmlPath);
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

    private static async Task PackAsync(CommandLineArgs args, List<string> written)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        // The settings only supply the default suffix; --suffix wins
        RepackOptions repackOptions = RepackOptionsParser.Parse(args, UserSettings.Load().Suffix);
        bool go = args.Flag("--go");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        RepackConfig repackConfig = await new RepackConfigBuilder().BuildRepackConfig(config, repackOptions);
        await WriteToFile(repackConfigPath, repackConfig);
        written.Add(repackConfigPath);
        logger.LogInformation("[✔ ] Repack configuration written to {Path}", repackConfigPath);

        // The plan is still written first, so a failed or surprising repack can be inspected afterwards
        if (go)
            await new RepackExecutor().RepackAndRebuildAsync(repackConfig);
    }

    private static async Task GoAsync(CommandLineArgs args)
    {
        string repackConfigPath = args.Value("--repack-config") ?? DefaultRepackConfigPath;
        args.EnsureAllConsumed();

        RepackConfig repackConfig = await ReadFromFile<RepackConfig>(repackConfigPath, "--repack-config", "pack");
        await new RepackExecutor().RepackAndRebuildAsync(repackConfig);
    }

    private static async Task RunExecutablesAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        List<string>? exclude = args.Values("--exclude");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "--config", "build");
        await new RunProcessor().RunAsync(config, exclude);
    }

    private static void Settings(CommandLineArgs args, List<string> written)
    {
        bool pathOnly = args.Flag("--path");
        args.EnsureAllConsumed();

        string path = UserSettings.DefaultPath;
        if (pathOnly)
        {
            Console.WriteLine(path);
            return;
        }

        if (UserSettings.CreateIfMissing(path))
        {
            written.Add(path);
            logger.LogInformation("[✔ ] Settings file created at {Path}", path);
        }
        else
        {
            logger.LogInformation("[⚙ ] Settings file: {Path}", path);
        }

        // A machine can have no app for .json: notepad is always there on Windows
        if (!OpenWithDefaultApp(path) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { Process.Start("notepad.exe", $"\"{path}\""); }
            catch (Exception ex) { logger.LogWarning("Could not open notepad: {Error}", ex.Message); }
        }
    }

    /// <summary>The files the command wrote, as its last lines (Ctrl+click opens them).</summary>
    private static void PrintWritten(List<string> written)
    {
        if (written.Count == 0) return;
        logger.LogInformation("[📄] Written:");
        foreach (var path in written)
            logger.LogInformation("     {Path}", path);
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
            throw new FileNotFoundException($"'{fullPath}' not found. Run '{CommandName} {producingCommand}' first.{hint}");
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

    public static void PrintHelp()
    {
        Console.WriteLine("LocalRun — rebuild and run only what matters locally: scan repositories,");
        Console.WriteLine("track dependency graphs and repack components to a local NuGet source.");
        Console.WriteLine();
        Console.WriteLine($"Usage: {CommandName} <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Relative paths are resolved against the current directory.");
        Console.WriteLine("Exit codes: 0 success, 1 command failed, 2 invalid command line.");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine();

        Console.WriteLine("  build [options]");
        Console.WriteLine("      Scan a directory tree for projects and write their dependency graph to a config file.");
        Console.WriteLine("      Options:");
        Console.WriteLine("        --root <dir>            Directory to scan (default: your settings, else current directory)");
        Console.WriteLine("        --local-nuget <dir>     Local NuGet feed to repack into (default: your settings, else ~/.localnuget)");
        Console.WriteLine($"        --config <file>         Config file to write (default: {DefaultConfigPath})");
        Console.WriteLine("        --users-of <names>      Keep the given projects and all that depend on them, transitively");
        Console.WriteLine("        --deps-of <names>       Keep the given projects and all they depend on, transitively");
        Console.WriteLine("                                With both: only the projects between them (AND)");
        Console.WriteLine("        --git-changes           Add the projects with uncommitted changes or commits not on origin's");
        Console.WriteLine("                                default branch to --users-of (compares with your last fetch)");
        Console.WriteLine("        --union                 With both: users of --users-of OR deps of --deps-of");
        Console.WriteLine("        --with-services         Also keep the executables using any kept project, with the libraries in between");
        Console.WriteLine("        --include-external      Keep dependencies on projects that no solution references");
        Console.WriteLine("        --tests                 Keep the test projects (left out by default)");
        Console.WriteLine("        --csproj                Don't query your NuGet sources and local feed for the latest versions:");
        Console.WriteLine("                                pack then starts from the csproj versions (default: from the latest)");
        Console.WriteLine("        --plot                  Then plot the graph, as 'plot' does with its defaults");
        Console.WriteLine();

        Console.WriteLine("  plot [options]");
        Console.WriteLine("      Render the dependency graph of a config file as an interactive HTML page: projects framed");
        Console.WriteLine("      by solution, collapsible into the solution graph, hover or click for users-of / deps-of.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --output <path>         Output path without extension (default: {DefaultVisualsFolder}/dependencies-<date-time>)");
        Console.WriteLine("        --open                  Open the page with the default browser");
        Console.WriteLine();

        Console.WriteLine("  pack [options]");
        Console.WriteLine("      Plan what will be repacked and to which versions, and write it to a repack config.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --repack-config <file>  Repack config file to write (default: {DefaultRepackConfigPath})");
        Console.WriteLine("        --major                 Increment major version (resets minor/patch)");
        Console.WriteLine("        --minor                 Increment minor version (resets patch)");
        Console.WriteLine("        --patch                 Increment patch version");
        Console.WriteLine("        --next                  Continue a prerelease (1.5.0-alpha.9 -> 1.5.0-alpha.10), or patch a release");
        Console.WriteLine("        --alpha                 Shorthand for --next --suffix alpha");
        Console.WriteLine("        --suffix <text>         Set the prerelease label, without the dash (default: your settings, else local; e.g., 'dev', 'beta.2')");
        Console.WriteLine("                                An iteration number follows it: 1.4.3-dev.1, then .2 when packed again");
        Console.WriteLine("        --clean                 Clean projects before restoring them");
        Console.WriteLine("        --go                    Execute the plan right away, as 'go' does (default: only write it)");
        Console.WriteLine();

        Console.WriteLine("  go [options]");
        Console.WriteLine("      Pack projects in dependency order into the local NuGet feed, following a repack config.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --repack-config <file>  Repack config file to read (default: {DefaultRepackConfigPath})");
        Console.WriteLine();

        Console.WriteLine("  up [options]");
        Console.WriteLine("      Build the executables of a config file, then run them side by side; Ctrl+C stops them all.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine("        --exclude <names>       Executables not to run, e.g. the one you debug from your IDE");
        Console.WriteLine();

        Console.WriteLine("  settings [options]");
        Console.WriteLine("      Create your settings file if missing (all commented out), then open it. It holds what differs");
        Console.WriteLine("      from one user to the next: the local NuGet feed, the root to scan, the default suffix.");
        Console.WriteLine("      An option given on the command line always wins over it.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --path                  Only print where the file is (default: ~/.lrun/settings.json, or ${UserSettings.PathVariable})");
        Console.WriteLine();

        Console.WriteLine("  help");
        Console.WriteLine("      Show this help message.");
        Console.WriteLine();

        Console.WriteLine("  version");
        Console.WriteLine("      Show the installed version (also --version). Please include it when reporting an issue.");
        Console.WriteLine();

        Console.WriteLine("Example:");
        Console.WriteLine($"  {CommandName} settings                        (once per machine: your local feed, your repos folder...)");
        Console.WriteLine($"  {CommandName} build --root ./repos --deps-of WebApp --plot");
        Console.WriteLine($"  {CommandName} plot --open                     (the same graph, opened)");
        Console.WriteLine($"  {CommandName} pack --patch --suffix dev       (review .lrun/repack.config.json, then:)");
        Console.WriteLine($"  {CommandName} go");
        Console.WriteLine($"  {CommandName} pack --patch --suffix dev --go  (both steps at once)");
        Console.WriteLine($"  {CommandName} up");
    }
}
