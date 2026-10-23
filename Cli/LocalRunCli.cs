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
/// Shared by the <c>localrun</c> executable and the interactive REPL.
/// </summary>
public static class LocalRunCli
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    private const string DefaultConfigPath = "localrun.config.json";
    private const string DefaultRepackConfigPath = "repack.config.json";
    private const string DefaultVisualsPath = "dependencies";

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
                case "build-config":
                    await BuildConfigAsync(options);
                    break;
                case "visualize":
                    await VisualizeAsync(options);
                    break;
                case "repack-preview":
                    await RepackPreviewAsync(options);
                    break;
                case "repack-go":
                    await RepackGoAsync(options);
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
            logger.LogError("{Error} Run 'help' for usage.", ex.Message);
            return ExitUsage;
        }
        catch (Exception ex)
        {
            logger.LogError("Command '{Command}' failed: {Error}", cmd, ex.Message);
            logger.LogDebug(ex, "Command '{Command}' failure details", cmd);
            return ExitFailure;
        }
    }

    private static async Task BuildConfigAsync(CommandLineArgs args)
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
        string outputPath = args.Value("--output") ?? DefaultVisualsPath;
        bool open = args.Flag("--open");
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build-config");
        string svgPath = await GraphVisualizer.GenerateAsync(new GraphVisualizer.Config
        {
            Graph = DependencyGraph.FromConfig(config),
            OutputFilePath = outputPath,
        });
        if (open)
            OpenWithDefaultApp(svgPath);
    }

    private static async Task RepackPreviewAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        string repackConfigPath = Path.GetFullPath(args.Value("--repack-config") ?? DefaultRepackConfigPath);
        RepackOptions repackOptions = RepackOptionsParser.Parse(args);
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build-config");
        RepackConfig repackConfig = await new RepackConfigBuilder().BuildRepackConfig(config, repackOptions);
        await WriteToFile(repackConfigPath, repackConfig);
        logger.LogInformation("[✔ ] Repack configuration written to {Path}", repackConfigPath);
    }

    private static async Task RepackGoAsync(CommandLineArgs args)
    {
        string repackConfigPath = args.Value("--repack-config") ?? DefaultRepackConfigPath;
        args.EnsureAllConsumed();

        RepackConfig repackConfig = await ReadFromFile<RepackConfig>(repackConfigPath, "repack-preview");
        await new RepackExecutor().RepackAndRebuildAsync(repackConfig);
    }

    private static async Task RunExecutablesAsync(CommandLineArgs args)
    {
        string configPath = args.Value("--config") ?? DefaultConfigPath;
        args.EnsureAllConsumed();

        LocalRunConfig config = await ReadFromFile<LocalRunConfig>(configPath, "build-config");
        await new RunProcessor().RunAsync(config);
    }

    private static async Task<T> ReadFromFile<T>(string pathToFile, string producingCommand)
        where T : class
    {
        string fullPath = Path.GetFullPath(pathToFile);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"'{fullPath}' not found. Run '{producingCommand}' first.");
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
        Console.WriteLine("Usage: localrun <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Relative paths are resolved against the current directory.");
        Console.WriteLine("Exit codes: 0 success, 1 command failed, 2 invalid command line.");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine();

        Console.WriteLine("  build-config [options]");
        Console.WriteLine("      Scan a directory tree for projects and write their dependency graph to a config file.");
        Console.WriteLine("      Options:");
        Console.WriteLine("        --root <dir>                         Directory to scan (default: current directory)");
        Console.WriteLine("        --local-nuget <dir>                  Local NuGet feed to repack into (default: ~/.localnuget)");
        Console.WriteLine($"        --config <file>                      Config file to write (default: {DefaultConfigPath})");
        Console.WriteLine("        --retain-depends-on <names>          Keep projects that depend (directly or indirectly) on given libs/exes");
        Console.WriteLine("        --retain-is-dependency-of <names>    Keep projects that are dependencies (direct or indirect) of given executables");
        Console.WriteLine("        --include-external                   Keep dependencies on projects that no solution references");
        Console.WriteLine();

        Console.WriteLine("  visualize [options]");
        Console.WriteLine("      Render the dependency graph of a config file as DOT and SVG.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --output <path>         Output path without extension (default: {DefaultVisualsPath})");
        Console.WriteLine("        --open                  Open the SVG with the default application");
        Console.WriteLine();

        Console.WriteLine("  repack-preview [options]");
        Console.WriteLine("      Write a repack config previewing what will be repacked and to which versions.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine($"        --repack-config <file>  Repack config file to write (default: {DefaultRepackConfigPath})");
        Console.WriteLine("        --major                 Increment major version (resets minor/patch)");
        Console.WriteLine("        --minor                 Increment minor version (resets patch)");
        Console.WriteLine("        --patch                 Increment patch version");
        Console.WriteLine("        --alpha                 Shorthand for --patch --suffix alpha");
        Console.WriteLine("        --suffix <text>         Add or replace version suffix (e.g., 'local', 'beta')");
        Console.WriteLine("        --remote                Query the configured NuGet sources for version info");
        Console.WriteLine("        --clean                 Clean projects before restoring them");
        Console.WriteLine();

        Console.WriteLine("  repack-go [options]");
        Console.WriteLine("      Pack projects in dependency order into the local NuGet feed, following a repack config.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --repack-config <file>  Repack config file to read (default: {DefaultRepackConfigPath})");
        Console.WriteLine();

        Console.WriteLine("  run [options]");
        Console.WriteLine("      Launch the executables of a config file, in dependency order.");
        Console.WriteLine("      Options:");
        Console.WriteLine($"        --config <file>         Config file to read (default: {DefaultConfigPath})");
        Console.WriteLine();

        Console.WriteLine("  help");
        Console.WriteLine("      Show this help message.");
        Console.WriteLine();

        Console.WriteLine("Example:");
        Console.WriteLine("  localrun build-config --root ./repos --retain-is-dependency-of WebApp");
        Console.WriteLine("  localrun visualize --open");
        Console.WriteLine("  localrun repack-preview --patch --suffix dev");
        Console.WriteLine("  localrun repack-go");
        Console.WriteLine("  localrun run");
    }
}
