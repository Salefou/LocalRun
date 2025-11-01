namespace LocalRun;

using LocalRun.Contracts;
using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Core.Resources;
using LocalRun.Options;
using System;
using System.IO;
using System.Text.Json;

public class Program
{
    private static readonly string UserDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string localNugetPath = Path.Combine(UserDir, ".localnuget");
    private static readonly string ReposRootPath = Path.Combine(UserDir, "source", "repos");
    private static readonly string PathToConfig = $"{Path.GetFullPath("..\\..\\..\\")}{"Output\\localrun.config.json"}";
    private static readonly string PathToVisuals = $"{Path.GetFullPath("..\\..\\..\\")}{"Visuals\\"}";

    public static async Task Main()
    {
        Console.Title = "LocalRun";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintWelcome();

        IConfigBuilder configBuilder = new ConfigBuilder();
        IRepacker repacker = new RepackExecutor();
        IRunProcessor runProcessor = new RunProcessor();

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

                case "repack":
                    RepackOptions repackOptions = RepackOptionsParser.Parse(args);
                    LocalRunConfig? currentConfig = await ReadConfigFromFile();
                    if (currentConfig == null)
                    {
                        Console.WriteLine("No config found. Run 'build-config' first.");
                        continue;
                    }
                    await repacker.RepackAndRebuildAsync(currentConfig, repackOptions);
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
                            OutputDirectory = PathToVisuals,
                        };
                        await GraphVisualizer.GenerateAsync(config);
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

    private static async Task<LocalRunConfig?> ReadConfigFromFile()
    {
        if (!File.Exists(PathToConfig))
            return null;
        return JsonSerializer.Deserialize<LocalRunConfig>(await File.ReadAllTextAsync(PathToConfig));
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