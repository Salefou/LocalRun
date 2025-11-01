namespace LocalRun;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Core.Resources;
using LocalRun.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

public class Program
{
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
                    if (readRepackConfig != null)
                    {
                        await repacker.RepackAndRebuildAsync(readRepackConfig);
                    }
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
        Console.WriteLine(" Welcome to LocalRun — your local .NET build and deploy manager");
        Console.WriteLine("──────────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("LocalRun helps you rebuild and run only what matters in your local environment.");
        Console.WriteLine("It analyzes your repository tree, detects Git changes, and maps dependency");
        Console.WriteLine("relationships between libraries and executables.");
        Console.WriteLine();
        Console.WriteLine("From there, you can:");
        Console.WriteLine(" • Build a dependency graph and save it as a config file.");
        Console.WriteLine(" • Filter projects by type of change or dependency.");
        Console.WriteLine(" • Repack changed libraries to your LocalNuGet cache.");
        Console.WriteLine(" • Rebuild and start executables in proper dependency order.");
        Console.WriteLine();
        Console.WriteLine("Type 'help' to list all available commands.");
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
        Console.WriteLine("      Analyze the source tree and build a dependency graph.");
        Console.WriteLine("      Produces a JSON config file that can be edited before repack.");
        Console.WriteLine("      Options:");
        Console.WriteLine("        --retain-depends-on <names>               Space-separated list of libs/exes such that only what depends of them is retained");
        Console.WriteLine("        --retain-is-dependency-of <names>         Space-separated list of libs/exes such that only what is a dependency of them is retained");
        Console.WriteLine("        --root <path>                             Override root search path (default: current dir)");
        Console.WriteLine("      Example:");
        Console.WriteLine("        build-config --retain-depends-on TweetToTradeCore Assembly-Seb --retain-is-dependency-of DLS.Dev AlgoSample");
        Console.WriteLine();
        Console.WriteLine("  visualize");
        Console.WriteLine("      Puts a visual representation of the graph in the config fie into a svg.");
        Console.WriteLine();
        Console.WriteLine("  repack");
        Console.WriteLine("      Repack all solutions in the current config file.");
        Console.WriteLine("      Packages are created in dependency order and pushed to LocalNuGet.");
        Console.WriteLine("      Options: ");
        Console.WriteLine("        --remote               Includes full set of checked sources in the nuget manager, local and remote");
        Console.WriteLine("  run");
        Console.WriteLine("      Launch executables defined in the config file, in dependency order.");
        Console.WriteLine();
        Console.WriteLine("  help");
        Console.WriteLine("      Print this help message.");
        Console.WriteLine();
        Console.WriteLine("  exit");
        Console.WriteLine("      Quit LocalRun interactive mode.");
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────");
        Console.WriteLine("Usage Examples:");
        Console.WriteLine("──────────────────────────────");
        Console.WriteLine("  LocalRun> build-config --include-external");
        Console.WriteLine("  LocalRun> visualize");
        Console.WriteLine("  LocalRun> build-config --retain-depends-on TweetToTradeCore Assembly-Seb");
        Console.WriteLine("  LocalRun> build-config --retain-is-dependency-of AlgoSample DLS.Dev AlgorithmsTest");
        Console.WriteLine("  LocalRun> visualize");
        Console.WriteLine("  LocalRun> repack --remote");
        Console.WriteLine("  LocalRun> run");
        Console.WriteLine("──────────────────────────────");
    }
}