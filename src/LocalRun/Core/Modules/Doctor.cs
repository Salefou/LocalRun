namespace LocalRun.Core.Modules;

using LibGit2Sharp;
using LocalRun.Logger;
using LocalRun.Options;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using Repository = LibGit2Sharp.Repository;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

/// <summary>One finding of <see cref="Doctor"/>.</summary>
/// <param name="Check">What was checked: <c>dotnet</c>, <c>settings</c>, <c>root</c>, <c>local-feed</c>, <c>source</c>,
/// <c>git-base</c> or <c>git-fetch</c>. A name is a contract, as diagnostic codes are.</param>
/// <param name="Subject">What it was checked on: a path, a source's name, a repository.</param>
/// <param name="Status"><c>ok</c>, <c>warn</c> (works, but a result may surprise) or <c>fail</c> (a command will fail).</param>
/// <param name="Hint">What to do about a warning or a failure; null when ok.</param>
public sealed record DoctorCheck(string Check, string Subject, string Status, string Message, string? Hint = null);

/// <summary>
/// The setup checks of <c>lrun doctor</c>: what explains most "why doesn't it work" before running anything. Read-only,
/// apart from a file created and deleted in the local feed to see that it is writable.
/// </summary>
public static class Doctor
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Fail = "fail";

    internal static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Beyond this, <c>--git-changes</c> compares with an old picture of origin.</summary>
    internal static readonly TimeSpan FetchAgeLimit = TimeSpan.FromDays(7);

    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The settings file, and the settings to go on with (none when invalid: the other checks use the defaults).</summary>
    public static (DoctorCheck Check, UserSettings Settings) CheckSettings(string? path = null)
    {
        path ??= UserSettings.DefaultPath;
        try
        {
            var settings = UserSettings.Load(path);
            return (new("settings", path, Ok, File.Exists(path) ? "valid" : "none, the defaults apply"), settings);
        }
        catch (SettingsInvalidException ex)
        {
            // The message says how to fix it
            return (new("settings", path, Fail, Sentence(ex.Message), "The other checks use the defaults."), new UserSettings());
        }
    }

    /// <summary>Every check but the settings, in the order they are reported.</summary>
    public static async Task<List<DoctorCheck>> RunAsync(string rootPath, string localNugetPath)
    {
        bool rootExists = Directory.Exists(rootPath);
        var checks = new List<DoctorCheck> { await CheckDotnetAsync(rootExists ? rootPath : null) };
        if (!rootExists)
        {
            checks.Add(new("root", rootPath, Fail, "not found", "Pass an existing directory with --root, or set 'root' in the settings."));
            checks.Add(CheckLocalFeed(localNugetPath));
            return checks;
        }

        var projectFolders = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .Select(csproj => Path.GetDirectoryName(csproj)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var repositories = projectFolders
            .Select(folder => Repository.Discover(folder))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        checks.Add(new("root", rootPath, projectFolders.Count == 0 ? Warn : Ok,
            $"{Text.Count(projectFolders.Count, "project folder")} in {Text.Count(repositories.Count, "git repository", "git repositories")}",
            projectFolders.Count == 0 ? "No .csproj under it: is it the right directory?" : null));
        checks.Add(CheckLocalFeed(localNugetPath));
        checks.AddRange(await CheckSourcesAsync(projectFolders, localNugetPath));
        foreach (string gitDir in repositories)
            checks.AddRange(CheckRepository(gitDir, DateTime.UtcNow));
        return checks;
    }

    /// <summary><c>dotnet --version</c> from the root, so that a <c>global.json</c> there pins the SDK as for a build.</summary>
    internal static async Task<DoctorCheck> CheckDotnetAsync(string? workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };
        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(DotnetTimeout);
            await process.WaitForExitAsync(timeout.Token);
            string version = (await stdout).Trim();
            if (process.ExitCode == 0)
                return new("dotnet", "SDK", Ok, version);
            // Typically a global.json asking for an SDK that isn't installed: dotnet says which
            string error = Sentence(string.Join(" ", ((await stderr) + "\n" + version).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
            return new("dotnet", "SDK", Fail, error, "Install the SDK it asks for, or change the global.json that pins it.");
        }
        catch (Win32Exception)
        {
            return new("dotnet", "SDK", Fail, "dotnet not found", "Install the .NET SDK, or add it to the PATH.");
        }
        catch (OperationCanceledException)
        {
            return new("dotnet", "SDK", Fail, $"'dotnet --version' did not answer within {DotnetTimeout.TotalSeconds:0} s", null);
        }
    }

    /// <summary>The feed <c>go</c> packs into: missing is fine (the first <c>go</c> creates it), not writable is not.</summary>
    internal static DoctorCheck CheckLocalFeed(string feed)
    {
        if (!Directory.Exists(feed))
            return new("local-feed", feed, Ok, "not created yet, the first go creates it");
        string probe = Path.Combine(feed, $".lrun-doctor-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return new("local-feed", feed, Ok, $"writable, {Text.Count(Directory.GetFiles(feed, "*.nupkg").Length, "package")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("local-feed", feed, Fail, $"not writable: {Sentence(ex.Message)}","Fix its permissions, or point at another feed with --local-nuget or 'localNuget' in the settings.");
        }
    }

    /// <summary>
    /// Every enabled source a restore from a project folder would use, opened once each: <c>build</c> reads the latest
    /// versions there, and a restore fails on a source it can't reach.
    /// </summary>
    internal static async Task<List<DoctorCheck>> CheckSourcesAsync(IEnumerable<string> projectFolders, string localNugetPath)
    {
        var sources = projectFolders
            .SelectMany(folder => NugetVersionChecker.EnabledSources(folder))
            .DistinctBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
            // Listed but not packed into yet: expected, as for build
            .Where(source => Directory.Exists(localNugetPath) || !NugetVersionChecker.IsSameFolder(source.Source, localNugetPath))
            .ToList();
        return [.. await Task.WhenAll(sources.Select(CheckSourceAsync))];
    }

    private static async Task<DoctorCheck> CheckSourceAsync(PackageSource source)
    {
        string subject = source.Name == source.Source ? source.Source : $"{source.Name} ({source.Source})";
        const string hint = "Check the URL, the network or proxy, and the credentials in NuGet.Config; or disable the source there.";
        if (source.IsLocal)
        {
            return Directory.Exists(source.Source)
                ? new("source", subject, Ok, "folder found")
                : new("source", subject, Fail, "the folder doesn't exist", "Create it, fix the path, or disable the source in NuGet.Config.");
        }
        try
        {
            using var timeout = new CancellationTokenSource(SourceTimeout);
            var opening = NuGet.Protocol.Core.Types.Repository.Factory.GetCoreV3(source).GetResourceAsync<PackageMetadataResource>(timeout.Token);
            // The token isn't honoured by every step of NuGet's HTTP stack
            if (await Task.WhenAny(opening, Task.Delay(SourceTimeout)) != opening)
                return new("source", subject, Fail, $"no answer within {SourceTimeout.TotalSeconds:0} s", hint);
            return await opening == null
                ? new("source", subject, Warn, "reachable, but serves no package metadata: build skips it", null)
                : new("source", subject, Ok, "reachable");
        }
        catch (Exception ex)
        {
            return new("source", subject, Fail, $"unreachable: {Sentence(Innermost(ex).Message)}", hint);
        }
    }

    /// <summary>What <c>--git-changes</c> needs from a repository: origin's default branch, fetched not too long ago.</summary>
    internal static List<DoctorCheck> CheckRepository(string gitDir, DateTime utcNow)
    {
        try
        {
            using var repo = new Repository(gitDir);
            string subject = Path.TrimEndingDirectorySeparator(repo.Info.WorkingDirectory ?? gitDir);
            var checks = new List<DoctorCheck>();

            var (baseName, baseCommit) = GitChangeDetector.FindDefaultBranch(repo);
            checks.Add(baseCommit == null
                ? new("git-base", subject, Warn, "no origin/HEAD, origin/main or origin/master: --git-changes counts the whole repo as changed",
                    "Run 'git fetch origin', then 'git remote set-head origin --auto'.")
                : new("git-base", subject, Ok, baseName));

            // A clone that was never fetched again has no FETCH_HEAD: its refs are as old as the clone, unknown here
            string fetchHead = Path.Combine(repo.Info.Path, "FETCH_HEAD");
            if (File.Exists(fetchHead))
            {
                var age = utcNow - File.GetLastWriteTimeUtc(fetchHead);
                checks.Add(age > FetchAgeLimit
                    ? new("git-fetch", subject, Warn, $"last fetched {age.Days} days ago: --git-changes compares with origin as it was then",
                        "Run 'git fetch' (lrun never fetches).")
                    : new("git-fetch", subject, Ok, age.TotalDays < 1 ? "fetched today" : $"fetched {Text.Count(age.Days, "day")} ago"));
            }
            return checks;
        }
        catch (LibGit2SharpException ex)
        {
            return [new("git-base", gitDir, Warn, $"unreadable: {Sentence(ex.Message)}: --git-changes counts the whole repo as changed", null)];
        }
    }

    /// <summary>An outside message as a clause: messages end without a period, the hint follows after one.</summary>
    private static string Sentence(string message) => message.Trim().TrimEnd('.');

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }
}
