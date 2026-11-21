namespace LocalRun.Core.Modules;

using LibGit2Sharp;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

/// <summary>The commit each repository of a tree has checked out, as <c>build</c> records it for <c>status</c>.</summary>
public static class RepositoryHeads
{
    private static readonly ILogger logger = Log.For(typeof(RepositoryHeads).FullName!);

    /// <summary>
    /// The repositories holding <paramref name="csprojs"/>, sorted by path; projects outside git add none. Reads only:
    /// nothing is fetched. A repository that can't be read is left out (logged at debug level), being only informative.
    /// When git itself can't be loaded (its native library, e.g. under a path longer than Windows allows), none is
    /// recorded, with one <c>git-unavailable</c> warning: <c>build</c> goes on.
    /// </summary>
    public static List<RepositoryHead> Find(string rootPath, IEnumerable<string> csprojs) =>
        Find(rootPath, csprojs, Repository.Discover);

    /// <param name="discover">Finds the git folder holding a folder (<see cref="Repository.Discover"/>); a test seam.</param>
    internal static List<RepositoryHead> Find(string rootPath, IEnumerable<string> csprojs, Func<string, string?> discover)
    {
        List<string> gitDirs = [];
        foreach (var folder in csprojs.Select(csproj => Path.GetDirectoryName(csproj)!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (discover(folder) is { } gitDir && !gitDirs.Contains(gitDir, StringComparer.OrdinalIgnoreCase))
                    gitDirs.Add(gitDir);
            }
            catch (Exception ex) when (ex is TypeInitializationException or DllNotFoundException)
            {
                string reason = (ex as TypeInitializationException)?.InnerException?.Message ?? ex.Message;
                logger.LogWarning(DiagnosticEvents.GitUnavailable, "Could not load git, so the repositories' HEADs are not recorded for status: {Error}", reason);
                logger.LogDebug(ex, "Loading git failed");
                return [];
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not look for a repository holding {Folder}", folder);
            }
        }

        List<RepositoryHead> heads = [];
        foreach (var gitDir in gitDirs)
        {
            try
            {
                using var repo = new Repository(gitDir);
                string workDir = repo.Info.WorkingDirectory ?? gitDir;
                string path = Path.GetRelativePath(rootPath, workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                heads.Add(new(path, repo.Head.Tip?.Sha, repo.Info.IsHeadDetached ? null : repo.Head.FriendlyName));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read the HEAD of {Repository}", gitDir);
            }
        }
        return [.. heads.OrderBy(head => head.Path, StringComparer.OrdinalIgnoreCase)];
    }
}
