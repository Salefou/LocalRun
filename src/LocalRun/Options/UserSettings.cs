namespace LocalRun.Options;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// What differs from one user (or machine) to the next, and rarely from one session to the next: defaults for options.
/// Read from <see cref="DefaultPath"/> (<c>lrun settings</c> creates and opens it). A command-line option always wins.
/// </summary>
/// <param name="LocalNuget">Default of <c>build --local-nuget</c>. Absolute, or starting with <c>~</c>.</param>
/// <param name="Root">Default of <c>build --root</c>. Absolute, or starting with <c>~</c>.</param>
/// <param name="Suffix">Default of <c>pack --suffix</c>.</param>
public sealed record UserSettings(string? LocalNuget = null, string? Root = null, string? Suffix = null)
{
    /// <summary>Overrides <see cref="DefaultPath"/>; also how tests and hand testing keep clear of the real file.</summary>
    internal const string PathVariable = "LOCALRUN_SETTINGS";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // A typo must fail, not be ignored (same rule as the command line)
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DefaultPath =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(HomeFolder, ".lrun", "settings.json");

    /// <summary>Every setting commented out: the file only holds what the user overrides.</summary>
    internal const string Template = """
        {
          // Per-user settings of lrun: what differs from one machine to the next.
          // Uncomment what you need. An option given on the command line always wins over this file.
          // Paths are absolute, or start with ~ for your home folder.

          // Local NuGet feed that 'go' repacks into (default: ~/.localnuget)
          // "localNuget": "~/.localnuget",

          // Directory 'build' scans for projects (default: the current directory)
          // "root": "~/source/repos",

          // Prerelease label 'pack' gives the planned versions (default: local)
          // "suffix": "local"
        }
        """;

    /// <summary>The settings of <paramref name="path"/> (default: <see cref="DefaultPath"/>); none when the file does not exist.</summary>
    public static UserSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new UserSettings();

        UserSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), ReadOptions) ?? new UserSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Cannot read the settings in '{path}': {ex.Message} Fix it with '{Cli.LocalRunCli.CommandName} settings'.");
        }

        try
        {
            if (settings.Suffix != null)
                RepackOptionsParser.EnsureValidSuffix(settings.Suffix, "suffix");
            return settings with
            {
                LocalNuget = ResolvePath(settings.LocalNuget, "localNuget"),
                Root = ResolvePath(settings.Root, "root"),
            };
        }
        catch (UsageException ex)
        {
            throw new InvalidDataException($"Invalid settings in '{path}': {ex.Message} Fix it with '{Cli.LocalRunCli.CommandName} settings'.");
        }
    }

    /// <summary>Writes the commented <see cref="Template"/> unless the file exists; returns whether it was written.</summary>
    public static bool CreateIfMissing(string path)
    {
        if (File.Exists(path))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Template + Environment.NewLine);
        return true;
    }

    private static string? ResolvePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value == "~" || value.StartsWith("~/") || value.StartsWith("~\\"))
            return Path.GetFullPath(Path.Combine(HomeFolder, value[1..].TrimStart('/', '\\')));
        if (!Path.IsPathRooted(value))
            throw new UsageException($"'{name}' must be an absolute path, or start with ~ (got '{value}'): a relative one would depend on where you run {Cli.LocalRunCli.CommandName} from.");
        return Path.GetFullPath(value);
    }
}
