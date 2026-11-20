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
    /// </summary>
    public static List<RepositoryHead> Find(string rootPath, IEnumerable<string> csprojs)
    {
        var gitDirs = csprojs
            .Select(csproj => Path.GetDirectoryName(csproj)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Repository.Discover)
            .Where(gitDir => gitDir != null)
            .Distinct(StringComparer.OrdinalIgnoreCase);

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
