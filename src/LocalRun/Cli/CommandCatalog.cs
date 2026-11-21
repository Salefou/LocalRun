namespace LocalRun.Cli;

using LocalRun.Logger;
using LocalRun.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <param name="Kind"><c>flag</c> (no value), <c>value</c> (exactly one) or <c>values</c> (one or more, up to the next option).</param>
/// <param name="Value">How the value is shown in the help (<c>&lt;dir&gt;</c>), or null for a flag.</param>
internal sealed record OptionInfo(string Name, string Kind, string? Value, string Description)
{
    public static OptionInfo Flag(string name, string description) => new(name, "flag", null, description);

    public static OptionInfo One(string name, string value, string description) => new(name, "value", value, description);

    public static OptionInfo Many(string name, string value, string description) => new(name, "values", value, description);
}

/// <param name="Values">The values it accepts.</param>
internal sealed record ArgumentInfo(string Name, IReadOnlyList<string> Values, string Description);

/// <param name="Aliases">Other names the command answers to (<c>--version</c>).</param>
internal sealed record CommandInfo(string Name, string Summary, IReadOnlyList<ArgumentInfo> Arguments, IReadOnlyList<OptionInfo> Options,
    IReadOnlyList<string> Aliases);

internal sealed record ExitCodeInfo(int Code, string Meaning);

/// <summary>
/// What <c>lrun</c> accepts, described once: the text help is rendered from it and <c>help --json</c> returns it, so an agent
/// learns the commands without the README. A test checks it against what each command actually reads.
/// </summary>
internal static class CommandCatalog
{
    private const int Width = 120;
    private const int DescriptionColumn = 32;

    public static IReadOnlyList<OptionInfo> GlobalOptions { get; } =
    [
        OptionInfo.Flag(OutputMode.JsonFlag, "Write one JSON document on stdout (result, warnings and errors, files written), even when it " +
            "fails; the logs go to stderr, plain."),
        OptionInfo.Flag("--strict", "Fail (exit code 5) when the result left something out or may have a wrong version: name collisions, " +
            "cycles, MSBuild or NuGet source failures... The files are written all the same."),
        OptionInfo.One(Log.LevelOption, "<level>", $"Least important logs to print: {string.Join(", ", Log.LevelNames)} (default: your " +
            "settings, else Information). Debug adds timings and the stack trace of a failure."),
    ];

    public static IReadOnlyList<ExitCodeInfo> ExitCodes { get; } =
    [
        new(LocalRunCli.ExitSuccess, "Success, also when there was nothing to do."),
        new(LocalRunCli.ExitFailure, "The command failed (anything the codes below don't cover)."),
        new(LocalRunCli.ExitUsage, "Invalid command line."),
        new(LocalRunCli.ExitInput, "An input is missing or invalid: a config file, the root to scan, the settings."),
        new(LocalRunCli.ExitDotnetFailed, "A dotnet command failed: fix the code or the project, retrying won't help."),
        new(LocalRunCli.ExitStrict, "Refused by --strict."),
        new(LocalRunCli.ExitVerifyFailed, "verify found a package missing or stale."),
    ];

    private static readonly OptionInfo ReadConfig =
        OptionInfo.One("--config", "<file>", $"Config file to read (default: {LocalRunCli.DefaultConfigPath}).");

    private static readonly OptionInfo[] Filters =
    [
        OptionInfo.Many("--users-of", "<names>", "Keep the given projects and all that depend on them, transitively."),
        OptionInfo.Many("--deps-of", "<names>", "Keep the given projects and all they depend on, transitively. With --users-of too: " +
            "only the projects between them (AND)."),
        OptionInfo.Flag("--git-changes", "Add the projects with uncommitted changes or commits not on origin's default branch to " +
            "--users-of (compares with your last fetch)."),
        OptionInfo.Flag("--union", "With --users-of and --deps-of: users of one OR deps of the other."),
        OptionInfo.Flag("--with-services", "Also keep the executables using any kept project, with the libraries in between."),
        OptionInfo.Flag("--include-external", "Keep dependencies on projects that no solution references."),
        OptionInfo.Flag("--tests", "Keep the test projects (left out by default)."),
        OptionInfo.Flag("--csproj", "Read the csproj files as they are: don't ask MSBuild for versions and package ids set elsewhere " +
            "(props files, $(...)), and don't query your NuGet sources and local feed for the latest versions (pack then starts " +
            "from the csproj versions)."),
    ];

    public static IReadOnlyList<CommandInfo> Commands { get; } =
    [
        Command("build", "Scan a directory tree for projects and write their dependency graph to a config file.",
        [
            OptionInfo.One("--root", "<dir>", "Directory to scan (default: your settings, else current directory)."),
            OptionInfo.One("--local-nuget", "<dir>", "Local NuGet feed to repack into (default: your settings, else ~/.localnuget)."),
            OptionInfo.One("--config", "<file>", $"Config file to write (default: {LocalRunCli.DefaultConfigPath})."),
            .. Filters,
            OptionInfo.Flag("--plot", "Then plot the graph, as 'plot' does with its defaults, and open it (only on a terminal: with " +
                "output redirected, the page is just written)."),
            OptionInfo.Flag("--verify", "Then check the local feed against the config just written, as 'verify' does."),
        ]),
        Command("list", "Print the projects 'build' would keep, with their depth, type, version and solution, without writing " +
            "anything: 'list --users-of X' answers \"what uses X?\". Doesn't query your NuGet sources.",
        [
            OptionInfo.One("--root", "<dir>", "Directory to scan, as for build (default: your settings, else current directory)."),
            .. Filters,
        ]),
        Command("verify", "Check that the local feed holds an up-to-date package of every library of a config: none is missing, none " +
            "older than its csproj version or than a file of its folder. Exit code 6 if not.",
        [
            ReadConfig,
        ]),
        Command("status", "Print what a config holds, from the files alone (no scan, no NuGet source, no git): when and how build " +
            "made it (flags, lrun version, each repository's commit), its projects by solution and type with their versions, what " +
            "was left out and why, the repack plan if any, and the up running with it. Warns when the plan is older than the config.",
        [
            ReadConfig,
            OptionInfo.One("--repack-config", "<file>", $"Repack config file to read, if there is one (default: {LocalRunCli.DefaultRepackConfigPath})."),
        ]),
        Command("plot", "Render the dependency graph of a config file as an interactive HTML page: projects framed by solution, " +
            "collapsible into the solution graph, hover or click for users-of / deps-of.",
        [
            ReadConfig,
            OptionInfo.One("--output", "<path>", $"Output path without extension (default: {LocalRunCli.DefaultVisualsFolder}/dependencies-<date-time>)."),
            OptionInfo.Flag("--open", "Open the page with the default browser."),
        ]),
        Command("pack", "Plan what will be repacked and to which versions, write the plan to a repack config, then pack in " +
            "dependency order into the local NuGet feed, as 'go' does.",
        [
            ReadConfig,
            OptionInfo.One("--repack-config", "<file>", $"Repack config file to write (default: {LocalRunCli.DefaultRepackConfigPath})."),
            OptionInfo.Flag("--major", "Increment major version (resets minor/patch)."),
            OptionInfo.Flag("--minor", "Increment minor version (resets patch)."),
            OptionInfo.Flag("--patch", "Increment patch version."),
            OptionInfo.Flag("--next", "Continue a prerelease (1.5.0-alpha.9 -> 1.5.0-alpha.10), or patch a release."),
            OptionInfo.Flag("--alpha", "Shorthand for --next --suffix alpha."),
            OptionInfo.One("--suffix", "<text>", "Set the prerelease label, without the dash (default: your settings, else local; e.g., " +
                "'dev', 'beta.2'). An iteration number follows it: 1.4.3-dev.1, then .2 when packed again."),
            OptionInfo.Flag("--clean", "Clean projects before restoring them."),
            OptionInfo.Flag("--dry-run", "Only write the plan and print what would run; 'go' runs it later."),
        ]),
        Command("go", "Pack projects in dependency order into the local NuGet feed, following a repack config (from " +
            "'pack --dry-run', possibly edited by hand).",
        [
            OptionInfo.One("--repack-config", "<file>", $"Repack config file to read (default: {LocalRunCli.DefaultRepackConfigPath})."),
        ]),
        Command("up", "Build the executables of a config file, then run them side by side; Ctrl+C (or 'down' from another shell) " +
            "stops them all, killing what still runs after a grace period (--stop-timeout). Each one's output also goes to " +
            "<logs>/<name>.log. One up per config folder: a second one is refused (up-running). With --json, stdout gets one line " +
            "per event (building, started, running, stopping, exited), then the result on one line.",
        [
            ReadConfig,
            OptionInfo.Many("--exclude", "<names>", "Executables not to run, e.g. the one you debug from your IDE."),
            OptionInfo.Many("--only", "<names>", "Executables to run, the config's others left out (combines with --exclude)."),
            OptionInfo.One("--logs", "<dir>", $"Folder of the services' log files, overwritten each run (default: {LocalRunCli.DefaultLogsFolder})."),
            OptionInfo.One("--stop-timeout", "<s>", "Seconds to wait after Ctrl+C before killing what still runs, 0 or more (default: " +
                "your settings, else 10)."),
        ]),
        Command("down", "Stop the up started with a config, from another shell or process, as Ctrl+C in its console would " +
            "(grace period, then kills), and wait until it has exited. Without one running, there is nothing to stop. Windows only.",
        [
            OptionInfo.One("--config", "<file>", $"Config the up was started with: it records itself next to it (default: {LocalRunCli.DefaultConfigPath})."),
        ]),
        Command("settings", "Create your settings file if missing (all commented out), check it (warning settings-invalid), " +
            "then open it. It holds what differs from " +
            "one user to the next: the local NuGet feed, the root to scan, the default suffix, how long up waits after Ctrl+C " +
            "before killing the processes, the log level. An option given on the command line always wins over it.",
        [
            OptionInfo.Flag("--path", $"Only print where the file is, and check it (default: ~/.lrun/settings.json, or ${UserSettings.PathVariable})."),
        ]),
        Command("doctor", "Check the setup before running anything: dotnet SDK, settings file, root, local feed (writable), every " +
            "NuGet source the projects restore from (reachable), and in each git repository origin's default branch and the last " +
            "fetch (--git-changes compares with it). Exit code 1 if a check fails.",
        [
            OptionInfo.One("--root", "<dir>", "Directory to check, as for build (default: settings, else current directory)."),
            OptionInfo.One("--local-nuget", "<dir>", "Local feed to check, as for build (default: settings, else ~/.localnuget)."),
        ]),
        new("schema", "Print the JSON Schema of a file LocalRun reads or writes, to validate or generate it: the config written " +
            "by build, the plan written by pack, the --json document, or the settings.",
            [new("name", SchemaNames, "Which schema: config, repack, result or settings.")], [], []),
        Command("changelog", "Print what changed in each release of lrun, newest first, from the changelog shipped with it (no " +
            "network). A local build shows its unreleased changes too. With --json, each release's entries by kind, breaking ones marked.",
        [
            OptionInfo.One("--since", "<version>", "Only the releases after this one, e.g. the version your scripts or an agent's " +
                "instructions were written for."),
        ]),
        new("help", "Show this help message. With --json, the commands, options, exit codes, error codes and diagnostic codes as " +
            "data, and the agent instructions.",
            [], [OptionInfo.Flag("--agent", "Print instructions to paste into an agent's (AGENTS.md, CLAUDE.md...): how to drive lrun, " +
                "stamped with this version, and how to update them after an upgrade.")], ["--help", "-h"]),
        new("version", "Show the installed version. Please include it when reporting an issue.", [], [], ["--version"]),
    ];

    private static IReadOnlyList<string> SchemaNames => Schemas.Names;

    private static CommandInfo Command(string name, string summary, IReadOnlyList<OptionInfo> options) => new(name, summary, [], options, []);

    private static readonly string[] Examples =
    [
        $"{LocalRunCli.CommandName} settings                            (once per machine: your local feed, your repos folder...)",
        $"{LocalRunCli.CommandName} doctor                              (when something doesn't work: what's wrong with the setup?)",
        $"{LocalRunCli.CommandName} list --root ./repos --users-of Shared.Pricing --with-services (what would a change touch?)",
        $"{LocalRunCli.CommandName} build --root ./repos --deps-of WebApp --plot",
        $"{LocalRunCli.CommandName} plot --open                         (the same graph, opened again)",
        $"{LocalRunCli.CommandName} pack --patch --suffix dev",
        $"{LocalRunCli.CommandName} pack --patch --suffix dev --dry-run (review .lrun/repack.config.json, then:)",
        $"{LocalRunCli.CommandName} go",
        $"{LocalRunCli.CommandName} status                              (what the config holds, how it was built, the plan)",
        $"{LocalRunCli.CommandName} verify                              (exit code 6: something to pack before 'up')",
        $"{LocalRunCli.CommandName} up",
        $"{LocalRunCli.CommandName} down                                (from another shell: stops that up as Ctrl+C would)",
        $"{LocalRunCli.CommandName} help --json                         (all of this as data, for a program or an agent)",
        $"{LocalRunCli.CommandName} changelog --since 0.5.1             (what changed since the version you knew)",
    ];

    public static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("LocalRun — rebuild and run only what matters locally: scan repositories,");
        writer.WriteLine("track dependency graphs and repack components to a local NuGet source.");
        writer.WriteLine();
        writer.WriteLine($"Usage: {LocalRunCli.CommandName} <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Relative paths are resolved against the current directory.");
        WriteWrapped(writer, "Exit codes: " + string.Join("; ", ExitCodes.Select(exit => $"{exit.Code} {Uncapitalized(exit.Meaning.TrimEnd('.'))}")) + ".", 0);
        writer.WriteLine();
        writer.WriteLine("Every command also takes:");
        foreach (var option in GlobalOptions)
            WriteOption(writer, option, 2);
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine();

        foreach (var command in Commands)
        {
            string arguments = string.Concat(command.Arguments.Select(argument => $" <{argument.Name}>"));
            string aliases = command.Aliases.Count == 0 ? "" : $" (also {string.Join(", ", command.Aliases)})";
            writer.WriteLine($"  {command.Name}{arguments}{(command.Options.Count == 0 ? "" : " [options]")}{aliases}");
            WriteWrapped(writer, command.Summary, 6);
            foreach (var argument in command.Arguments)
                WriteWrapped(writer, $"<{argument.Name}>: {string.Join(" | ", argument.Values)}", 6);
            if (command.Options.Count != 0)
            {
                writer.WriteLine("      Options:");
                foreach (var option in command.Options)
                    WriteOption(writer, option, 8);
            }
            writer.WriteLine();
        }

        writer.WriteLine("Example:");
        foreach (var example in Examples)
            writer.WriteLine($"  {example}");
    }

    private static void WriteOption(TextWriter writer, OptionInfo option, int indent)
    {
        string usage = option.Value == null ? option.Name : $"{option.Name} {option.Value}";
        var lines = Wrap(option.Description, Width - DescriptionColumn);
        string head = new string(' ', indent) + usage;
        // A usage too long for its column gets the description on the next lines
        if (head.Length >= DescriptionColumn - 1)
        {
            writer.WriteLine(head);
            head = "";
        }
        writer.WriteLine(head.PadRight(DescriptionColumn) + lines[0]);
        foreach (var line in lines.Skip(1))
            writer.WriteLine(new string(' ', DescriptionColumn) + line);
    }

    private static void WriteWrapped(TextWriter writer, string text, int indent)
    {
        foreach (var line in Wrap(text, Width - indent))
            writer.WriteLine(new string(' ', indent) + line);
    }

    /// <summary>Words of <paramref name="text"/> in lines of at most <paramref name="width"/> characters (a longer word gets its own).</summary>
    internal static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        string line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length != 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = "";
            }
            line = line.Length == 0 ? word : $"{line} {word}";
        }
        lines.Add(line);
        return lines;
    }

    private static string Uncapitalized(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;
}
