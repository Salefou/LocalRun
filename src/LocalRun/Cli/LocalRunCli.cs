namespace LocalRun.Cli;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Logger;
using LocalRun.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
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

    private const string DefaultConfigPath = "localrun.config.json";
    private const string DefaultRepackConfigPath = "repack.config.json";
    private const string DefaultVisualsFolder = "lrun-visuals";

    private static readonly ILogger logger = Log.For(typeof(LocalRunCli).FullName!);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static string DefaultLocalNugetPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".localnuget");

    /// <param name="args">The command name followed by its options, e.g. <c>["visualize", "--open"]</c>.</param>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return ExitUsage;
        }

        string cmd = args[0].ToLowerInvariant();
        try
        {
            var options = new CommandLineArgs(args.Skip(1));
            switch (cmd)
            {
                case "build":
                    await BuildAsync(options);
                    break;
                case "visualize":
                    await VisualizeAsync(options);
                    break;
                case "pack":
                    await PackAsync(options);
                    break;
                case "go":
                    await GoAsync(options);
                    break;
                case "run":
                    await RunExecutablesAsync(options);
                    break;
                case "help" or "--help" or "-h":
                    PrintHelp();
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
    }

    private static async Task BuildAsync(CommandLineArgs args)
    {
        string rootPath = Path.GetFullPath(args.Value("--root") ?? Directory.GetCurrentDirectory());
        string localNugetPath = Path.GetFullPath(args.Value("--local-nuget") ?? DefaultLocalNugetPath);
        string configPath = Path.GetFullPath(args.Value("--config") ?? DefaultConfigPath);
        BuildConfigOptions configOptions = BuildConfigOptionsParser.Parse(args);
        args.EnsureAllConsumed();

        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Root directory not found: {rootPath}");

        LocalRunConfig localRunConfig = new ConfigBuilder().BuildConfig(rootPath, configOptions, localNugetPath);
        await WriteToFile(configPath, localRunConfig);
        logger.LogInformation("[✔ ] Configuration written to {Path}", configPath);
    }

    private static async Task VisualizeAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string outputPath = args.Value("--output") ?? UniqueVisualsPath(DefaultVisualsFolder, DateTime.Now);
        bool open = args.Flag("--open");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build");
        string svgPath = await GraphVisualizer.GenerateAsync(new GraphVisualizer.Config
        {
            Graph = DependencyGraph.FromConfig(config),
            OutputFilePath = outputPath,
        });
        if (open)
            OpenWithDefaultApp(svgPath);
    }

    /// <summary>
    /// <c>&lt;folder&gt;/dependencies-&lt;yyyyMMdd-HHmmss&gt;</c> (without extension), so successive graphs are kept side by
    /// side; a counter is appended when that name is already taken within the same second.
    /// </summary>
    internal static string UniqueVisualsPath(string folder, DateTime now)
    {
        string basePath = Path.Combine(folder, $"dependencies-{now:yyyyMMdd-HHmmss}");
        string path = basePath;
        for (int counter = 2; File.Exists($"{path}.svg") || File.Exists($"{path}.dot"); counter++)
            path = $"{basePath}-{counter}";
        return path;
    }

    private static async Task PackAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        RepackOptions repackOptions = RepackOptionsParser.Parse(args);
        bool go = args.Flag("--go");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build");
        RepackConfig repackConfig = await new RepackConfigBuilder().BuildRepackConfig(config, repackOptions);
        await WriteToFile(repackConfigPath, repackConfig);
        logger.LogInformation("[✔ ] Repack configuration written to {Path}", repackConfigPath);

        // The plan is still written first, so a failed or surprising repack can be inspected afterwards
        if (go)
            await new RepackExecutor().RepackAndRebuildAsync(repackConfig);
    }

    private static async Task GoAsync(CommandLineArgs args)
    {
        string repackConfigPath = args.Value("--repack-config") ?? DefaultRepackConfigPath;
        args.EnsureAllConsumed();

        RepackConfig repackConfig = await ReadFromFile<RepackConfig>(repackConfigPath, "pack");
        await new RepackExecutor().RepackAndRebuildAsync(repackConfig);
    }

    private static async Task RunExecutablesAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        List<string>? exclude = args.Values("--exclude");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build");
        await new RunProcessor().RunAsync(config, exclude);
    }

    private static async Task<T> ReadFromFile<T>(string pathToFile, string producingCommand)
        where T : class
    {
        string fullPath = Path.GetFullPath(pathToFile);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"'{fullPath}' not found. Run '{CommandName} {producingCommand}' first.");
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(fullPath))
            ?? throw new InvalidDataException($"'{fullPath}' is empty.");
    }

    private static async Task WriteToFile<T>(string fullPath, T content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(content, WriteOptions));
    }

    private static void OpenWithDefaultApp(string filePath)
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
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not open SVG: {Error}", ex.Message);
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
        Console.WriteLine("        --root <dir>            Directory to scan (default: current directory)");
        Console.WriteLine("        --local-nuget <dir>     Local NuGet feed to repack into (default: ~/.localnuget)");
        Console.WriteLine($"        --config <file>         Config file to write (default: {DefaultConfigPath})");
        Console.WriteLine("        --users-of <names>      Keep the given projects and all that depend on them, transitively");
        Console.WriteLine("        --deps-of <names>       Keep the given projects and all they depend on, transitively");
        Console.WriteLine("                                With both: only the projects between them (AND)");
        Console.WriteLine("        --union                 With both: users of --users-of OR deps of --deps-of");
        Console.WriteLine("        --with-services         Also keep the executables using any kept project, with the libraries in between");
        Console.WriteLine("        --include-external      Keep dependencies on projects that no solution references");
        Console.WriteLine();

        Console.WriteLine("  visualize [options]");
        Console.WriteLine("      Render the dependency graph of a config file as DOT and SVG.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --output <path>         Output path without extension (default: {DefaultVisualsFolder}/dependencies-<date-time>)");
        Console.WriteLine("        --open                  Open the SVG with the default application");
        Console.WriteLine();

        Console.WriteLine("  pack [options]");
        Console.WriteLine("      Plan what will be repacked and to which versions, and write it to a repack config.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --repack-config <file>  Repack config file to write (default: {DefaultRepackConfigPath})");
        Console.WriteLine("        --major                 Increment major version (resets minor/patch)");
        Console.WriteLine("        --minor                 Increment minor version (resets patch)");
        Console.WriteLine("        --patch                 Increment patch version");
        Console.WriteLine("        --alpha                 Shorthand for --patch --suffix alpha");
        Console.WriteLine("        --suffix <text>         Set the prerelease label, without the dash (default: local; e.g., 'dev', 'beta.2')");
        Console.WriteLine("        --remote                Query the configured NuGet sources for version info");
        Console.WriteLine("        --clean                 Clean projects before restoring them");
        Console.WriteLine("        --go                    Execute the plan right away, as 'go' does (default: only write it)");
        Console.WriteLine();

        Console.WriteLine("  go [options]");
        Console.WriteLine("      Pack projects in dependency order into the local NuGet feed, following a repack config.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --repack-config <file>  Repack config file to read (default: {DefaultRepackConfigPath})");
        Console.WriteLine();

        Console.WriteLine("  run [options]");
        Console.WriteLine("      Build the executables of a config file, then run them side by side; Ctrl+C stops them all.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine("        --exclude <names>       Executables not to run, e.g. the one you debug from your IDE");
        Console.WriteLine();

        Console.WriteLine("  help");
        Console.WriteLine("      Show this help message.");
        Console.WriteLine();

        Console.WriteLine("Example:");
        Console.WriteLine($"  {CommandName} build --root ./repos --deps-of WebApp");
        Console.WriteLine($"  {CommandName} visualize --open");
        Console.WriteLine($"  {CommandName} pack --patch --suffix dev       (review repack.config.json, then:)");
        Console.WriteLine($"  {CommandName} go");
        Console.WriteLine($"  {CommandName} pack --patch --suffix dev --go  (both steps at once)");
        Console.WriteLine($"  {CommandName} run");
    }
}
