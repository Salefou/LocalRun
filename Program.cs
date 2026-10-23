namespace LocalRun;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Core.Resources;
using LocalRun.Logger;
using LocalRun.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

public class Program
{
    private static readonly ILoggerShishe<Program> logger = LoggerFactory.Create<Program>();

    private static readonly string UserDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string localNugetPath = Path.Combine(UserDir, ".localnuget");
    private static readonly string ReposRootPath = Path.Combine(UserDir, "source", "repos");
    private static readonly string PathToConfig = $"{Path.GetFullPath("..\\..\\..\\")}{"Output\\localrun.config.json"}";
    private static readonly string PathToRepackConfig = $"{Path.GetFullPath("..\\..\\..\\")}{"Output\\repack.config.json"}";
    private static readonly string PathToVisuals = $"{Path.GetFullPath("..\\..\\..\\")}{"Visuals\\dependencies"}";

    public static async Task Main()
    {
        Console.Title = "LocalRun";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintWelcome();

        var configBuilder = new ConfigBuilder();
        var repacker = new RepackExecutor();
        var runProcessor = new RunProcessor();
        var repackConfigBuilder = new RepackConfigBuilder();

        while (true)
        {
            Console.Write("LocalRun> ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            var parts = input.Split(' ', 2);
            var cmd = parts[0].ToLowerInvariant();
            var args = parts.Length > 1 ? parts[1].Split(' ') : [];

            try
            {
                switch (cmd)
                {
                    case "build-config":
                        BuildConfigOptions configOptions = BuildConfigOptionsParser.Parse(args);
                        LocalRunConfig localRunConfig = configBuilder.BuildConfig(ReposRootPath, configOptions, localNugetPath);
                        await File.WriteAllTextAsync(PathToConfig, JsonSerializer.Serialize(localRunConfig, new JsonSerializerOptions { WriteIndented = true }));
                        Console.WriteLine($"[✔ ] Configuration written to {PathToConfig}");
                        break;

                    case "repack-preview":
                        RepackOptions repackOptions = RepackOptionsParser.Parse(args);
                        LocalRunConfig? currentConfig = await ReadConfigFromFile();
                        if (currentConfig == null)
                        {
                            Console.WriteLine("No config found. Run 'build-config' first.");
                            continue;
                        }
                        RepackConfig writeRepackConfig = await repackConfigBuilder.BuildRepackConfig(currentConfig, repackOptions);
                        await File.WriteAllTextAsync(PathToRepackConfig, JsonSerializer.Serialize(writeRepackConfig, new JsonSerializerOptions { WriteIndented = true }));
                        break;

                    case "repack-go":
                        RepackConfig? readRepackConfig = await ReadRepackConfigFromFile();
                        if (readRepackConfig == null)
                        {
                            Console.WriteLine("No repack config found. Run 'repack-preview' first.");
                            continue;
                        }
                        await repacker.RepackAndRebuildAsync(readRepackConfig);
                        break;

                    case "run":
                        LocalRunConfig? runConfig = await ReadConfigFromFile();
                        if (runConfig != null)
                            await runProcessor.RunAsync(runConfig);
                        else
                            Console.WriteLine("No config found. Run 'build-config' first.");
                        break;
                    case "visualize":
                        LocalRunConfig? visualsConfig = await ReadConfigFromFile();
                        if (visualsConfig != null)
                        {
                            DependencyGraph graph = DependencyGraph.FromConfig(visualsConfig);
                            GraphVisualizer.Config config = new()
                            {
                                Graph = graph,
                                OutputFilePath = PathToVisuals,
                            };
                            await GraphVisualizer.GenerateAsync(config);
                            OpenWithDefaultApp($"{PathToVisuals}.svg");
                        }

                        break;
                    case "help":
                        PrintHelp();
                        break;

                    case "exit":
                        return;

                    default:
                        Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Command '{cmd}' failed: {ex.Message}", ex);
            }
        }
    }

    private static async Task<T?> ReadFromFile<T>(string pathToFile)
        where T : class
    {
        if (!File.Exists(pathToFile))
            return null;
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(pathToFile));
    }

    private static async Task<LocalRunConfig?> ReadConfigFromFile()
    {
        return await ReadFromFile<LocalRunConfig>(PathToConfig);
    }

    private static async Task<RepackConfig?> ReadRepackConfigFromFile()
    {
        return await ReadFromFile<RepackConfig>(PathToRepackConfig);
    }

    public static void OpenWithDefaultApp(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[⚠] File not found: {filePath}");
                return;
            }

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
                Console.WriteLine($"[⚠] Unsupported platform: cannot open {filePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[⚠] Could not open SVG: {ex.Message}");
        }
    }

    private static void PrintWelcome()
    {
        Console.WriteLine(Art.LocalRunAsciiFlowerPower);
        Console.WriteLine("──────────────────────────────────────────────────────────────");
        Console.WriteLine(" Welcome to LocalRun — your local .NET dependency orchestrator");
        Console.WriteLine("──────────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("LocalRun lets you rebuild and run only what matters locally —");
        Console.WriteLine("analyzing repositories, tracking dependency graphs, and");
        Console.WriteLine("selectively repacking components to a local NuGet source.");
        Console.WriteLine();
        Console.WriteLine("Use it to:");
        Console.WriteLine(" • Scan repositories and detect changed or relevant projects.");
        Console.WriteLine(" • Build a dependency graph and save it as a config file.");
        Console.WriteLine(" • Preview version bumps before repackaging.");
        Console.WriteLine(" • Repack projects to your LocalNuGet cache.");
        Console.WriteLine(" • Rebuild or run executables in dependency order.");
        Console.WriteLine();
        Console.WriteLine("Type 'help' for a list of available commands.");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("──────────────────────────────");
        Console.WriteLine(" LocalRun Command Reference");
        Console.WriteLine("──────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine();

        Console.WriteLine("  build-config [options]");
        Console.WriteLine("      Analyze the repository tree and build a dependency graph.");
        Console.WriteLine("      Produces 'localrun.config.json' under /Output.");
        Console.WriteLine("      Options:");
        Console.WriteLine("        --retain-depends-on <names>          Keep projects that depend (directly or indirectly) on given libs/exes");
        Console.WriteLine("        --retain-is-dependency-of <names>    Keep projects that are dependencies (direct or indirect) of given executables");
        Console.WriteLine("        --root <path>                        Override default root search path (default: ~/source/repos)");
        Console.WriteLine("      Example:");
        Console.WriteLine("        build-config --retain-depends-on Shared.Core Utils --retain-is-dependency-of WebApp DataProcessor");
        Console.WriteLine();

        Console.WriteLine("  visualize");
        Console.WriteLine("      Generate a visual graph of the dependencies in the current config.");
        Console.WriteLine("      Outputs an SVG to /Visuals/dependencies.svg and opens it automatically.");
        Console.WriteLine();

        Console.WriteLine("  repack-preview [options]");
        Console.WriteLine("      Create 'repack.config.json' previewing what will be repacked and to which versions.");
        Console.WriteLine("      Options:");
        Console.WriteLine("        --major                 Increment major version (resets minor/patch)");
        Console.WriteLine("        --minor                 Increment minor version (resets patch)");
        Console.WriteLine("        --patch                 Increment patch version");
        Console.WriteLine("        --suffix <text>         Add or replace version suffix (e.g., '-local', '-beta')");
        Console.WriteLine("        --remote                Query both local and remote NuGet sources for version info");
        Console.WriteLine("      Example:");
        Console.WriteLine("        repack-preview --patch --suffix dev");
        Console.WriteLine();

        Console.WriteLine("  repack-go");
        Console.WriteLine("      Execute the repack based on 'repack.config.json'.");
        Console.WriteLine("      Packages are created in dependency order and pushed to your LocalNuGet feed.");
        Console.WriteLine();

        Console.WriteLine("  run");
        Console.WriteLine("      Launch executables listed in the config, in dependency order.");
        Console.WriteLine("      Ideal for partial local deployment of a microservice ecosystem.");
        Console.WriteLine();

        Console.WriteLine("  help");
        Console.WriteLine("      Show this help message.");
        Console.WriteLine();

        Console.WriteLine("  exit");
        Console.WriteLine("      Quit LocalRun interactive mode.");
        Console.WriteLine();

        Console.WriteLine("──────────────────────────────");
        Console.WriteLine(" Example session:");
        Console.WriteLine("──────────────────────────────");
        Console.WriteLine("  LocalRun> build-config --retain-depends-on TweetToTradeCore Assembly-Seb");
        Console.WriteLine("  LocalRun> build-config --retain-is-dependency-of AlgoSample DLS.Dev AlgorithmsTest");
        Console.WriteLine("  LocalRun> visualize");
        Console.WriteLine("  LocalRun> repack-preview --patch --suffix dev");
        Console.WriteLine("  LocalRun> repack-preview --remote --alpha");
        Console.WriteLine("  LocalRun> repack-go");
        Console.WriteLine("  LocalRun> run");
        Console.WriteLine("──────────────────────────────");
    }
}