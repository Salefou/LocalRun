namespace LocalRun.Repl;

using LocalRun.Cli;
using LocalRun.Logger;
using System;
using System.IO;

/// <summary>
/// Interactive shell over <see cref="LocalRunCli"/>: every line is run as a <c>localrun</c> command,
/// in-process, plus a few shell built-ins (cd, pwd, exit).
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            // Optional argument: the working directory to start in
            if (args.Length > 0)
                Directory.SetCurrentDirectory(ExpandHome(args[0]));
            await RunReplAsync();
        }
        finally
        {
            Log.Shutdown();
        }
    }

    private static async Task RunReplAsync()
    {
        if (!Console.IsOutputRedirected)
            Console.Title = "LocalRun";

        PrintWelcome();

        while (true)
        {
            Console.Write("LocalRun> ");
            string? line = Console.ReadLine();
            if (line == null) return; // end of input

            string[] args;
            try
            {
                args = ReplTokenizer.Split(line);
            }
            catch (FormatException ex)
            {
                Console.WriteLine(ex.Message);
                continue;
            }
            if (args.Length == 0) continue;

            switch (args[0].ToLowerInvariant())
            {
                case "exit" or "quit":
                    return;
                case "pwd":
                    Console.WriteLine(Directory.GetCurrentDirectory());
                    break;
                case "cd":
                    ChangeDirectory(args);
                    break;
                case "help":
                    LocalRunCli.PrintHelp();
                    PrintReplHelp();
                    break;
                default:
                    await LocalRunCli.RunAsync(args);
                    break;
            }
        }
    }

    private static void ChangeDirectory(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: cd <dir>");
            return;
        }
        try
        {
            Directory.SetCurrentDirectory(ExpandHome(args[1]));
            Console.WriteLine(Directory.GetCurrentDirectory());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"cd: {ex.Message}");
        }
    }

    private static string ExpandHome(string path)
    {
        if (path != "~" && !path.StartsWith("~/") && !path.StartsWith("~\\"))
            return path;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home + path[1..];
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
        Console.WriteLine($"Working directory: {Directory.GetCurrentDirectory()}");
        Console.WriteLine("Type 'help' for a list of available commands.");
        Console.WriteLine();
    }

    private static void PrintReplHelp()
    {
        Console.WriteLine();
        Console.WriteLine("REPL:");
        Console.WriteLine("  Type commands without the 'localrun' prefix. Quote values containing spaces.");
        Console.WriteLine("  Config files are read and written relative to the working directory.");
        Console.WriteLine();
        Console.WriteLine("  cd <dir>    Change the working directory");
        Console.WriteLine("  pwd         Print the working directory");
        Console.WriteLine("  exit        Quit (also: quit, end of input)");
        Console.WriteLine();
        Console.WriteLine("Example session:");
        Console.WriteLine("  LocalRun> cd ~/work");
        Console.WriteLine("  LocalRun> build-config --root ./repos --retain-is-dependency-of WebApp");
        Console.WriteLine("  LocalRun> visualize --open");
        Console.WriteLine("  LocalRun> repack-preview --patch --suffix dev");
        Console.WriteLine("  LocalRun> repack-go");
        Console.WriteLine("  LocalRun> run");
    }
}
