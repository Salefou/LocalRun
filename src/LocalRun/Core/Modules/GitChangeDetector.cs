namespace LocalRun.Core.Modules;

using LibGit2Sharp;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

/// <summary>
/// Finds the projects with pending git changes: uncommitted or untracked files, or commits on HEAD that
/// are not on origin's default branch (pushed or not; being behind it doesn't count). When unsure, it
/// errs on the side of "changed": an extra project is easy to spot and delete from the config, a
/// missing one is not.
/// </summary>
public static class GitChangeDetector
{
    private static readonly ILogger logger = Log.For(typeof(GitChangeDetector).FullName!);

    private static readonly string[] DefaultBranchRefs =
        ["refs/remotes/origin/HEAD", "refs/remotes/origin/main", "refs/remotes/origin/master"];

    /// <summary>
    /// The names of the changed projects, sorted. A changed file belongs to the project with the deepest
    /// folder containing it; a changed file outside every project folder (Directory.Build.props, global.json, …)
    /// marks every project of that repository. Compares with the last fetch: nothing is fetched.
    /// </summary>
    /// <param name="projects">Every scanned project, as its name and the full path of its .csproj.</param>
    public static List<string> FindChangedProjects(IEnumerable<(string Name, string CsprojPath)> projects)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notInGit = new List<string>();

        var byRepository = projects.GroupBy(project => Repository.Discover(Path.GetDirectoryName(project.CsprojPath)));
        foreach (var repoProjects in byRepository)
        {
            if (repoProjects.Key == null)
            {
                notInGit.AddRange(repoProjects.Select(project => project.Name));
                continue;
            }
            changed.UnionWith(FindChangedInRepository(repoProjects.Key, [.. repoProjects]));
        }

        if (notInGit.Count != 0)
        {
            logger.LogWarning("Not in a git repository, counted as changed: {Projects}",
                string.Join(", ", notInGit.Order(StringComparer.OrdinalIgnoreCase)));
            changed.UnionWith(notInGit);
        }
        return [.. changed.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> FindChangedInRepository(string gitDir, List<(string Name, string CsprojPath)> projects)
    {
        List<string> all = [.. projects.Select(project => project.Name).Order(StringComparer.OrdinalIgnoreCase)];
        string repoLabel = gitDir;
        try
        {
            using var repo = new Repository(gitDir);
            string workDir = repo.Info.WorkingDirectory;
            repoLabel = Path.TrimEndingDirectorySeparator(workDir);

            var uncommitted = repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true })
                .Where(entry => entry.State != FileStatus.Unaltered && !entry.State.HasFlag(FileStatus.Ignored))
                .SelectMany(entry => new[] { entry.FilePath, entry.HeadToIndexRenameDetails?.OldFilePath, entry.IndexToWorkDirRenameDetails?.OldFilePath })
                .OfType<string>()
                .ToList();

            var reasons = new List<string>();
            if (uncommitted.Count != 0)
                reasons.Add($"{uncommitted.Count} uncommitted file(s)");

            var paths = new List<string>(uncommitted);
            if (repo.Head.Tip is Commit head)
            {
                var (baseName, baseCommit) = FindDefaultBranch(repo);
                if (baseCommit == null)
                {
                    logger.LogWarning("[🔀] {Repo}: no origin/HEAD, origin/main or origin/master to compare with, whole repo counted as changed",
                        repoLabel);
                    return all;
                }
                var mergeBase = repo.ObjectDatabase.FindMergeBase(head, baseCommit);
                if (mergeBase == null)
                {
                    logger.LogWarning("[🔀] {Repo}: HEAD shares no history with {Base}, whole repo counted as changed", repoLabel, baseName);
                    return all;
                }
                if (mergeBase.Sha != head.Sha)
                {
                    int ahead = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = head, ExcludeReachableFrom = baseCommit }).Count();
                    reasons.Add($"{ahead} commit(s) on {repo.Head.FriendlyName} not on {baseName}");
                    foreach (var change in repo.Diff.Compare<TreeChanges>(mergeBase.Tree, head.Tree))
                    {
                        paths.Add(change.Path);
                        paths.Add(change.OldPath);
                    }
                }
            }

            if (paths.Count == 0)
            {
                logger.LogDebug("[🔀] {Repo}: no pending changes", repoLabel);
                return [];
            }

            var (changed, outside) = MapToProjects(workDir, projects, paths);
            if (outside.Count != 0)
            {
                logger.LogInformation("[🔀] {Repo}: {Reasons} -> whole repo (changes outside project folders: {Files})",
                    repoLabel, string.Join(", ", reasons), Abbreviate(outside));
                return all;
            }
            logger.LogInformation("[🔀] {Repo}: {Reasons} -> {Projects}", repoLabel, string.Join(", ", reasons), string.Join(", ", changed));
            return changed;
        }
        catch (LibGit2SharpException ex)
        {
            logger.LogWarning("[🔀] {Repo}: could not read the repository ({Error}), whole repo counted as changed", repoLabel, ex.Message);
            return all;
        }
    }

    private static (string Name, Commit? Commit) FindDefaultBranch(Repository repo)
    {
        foreach (string refName in DefaultBranchRefs)
        {
            var reference = repo.Refs[refName]?.ResolveToDirectReference();
            if (reference?.Target is Commit commit)
                return (reference.CanonicalName.Replace("refs/remotes/", ""), commit);
        }
        return ("", null);
    }

    /// <summary>
    /// The projects owning the given repository-relative paths (deepest project folder wins), and the paths
    /// under no project folder.
    /// </summary>
    internal static (List<string> Changed, List<string> Outside) MapToProjects(
        string workDir, IEnumerable<(string Name, string CsprojPath)> projects, IEnumerable<string> paths)
    {
        var folders = projects
            .Select(project => (project.Name, Folder: FolderPrefix(Path.GetRelativePath(workDir, Path.GetDirectoryName(project.CsprojPath)!))))
            .OrderByDescending(project => project.Folder.Length)
            .ToList();

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outside = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Select(path => path.Replace('\\', '/')).Distinct())
        {
            var owner = folders.FirstOrDefault(project => path.StartsWith(project.Folder, StringComparison.OrdinalIgnoreCase));
            if (owner.Name != null)
                changed.Add(owner.Name);
            else
                outside.Add(path);
        }
        return ([.. changed.Order(StringComparer.OrdinalIgnoreCase)], [.. outside]);
    }

    /// <summary>"src/Lib/" for a project folder, "" for one at the repository root (it owns every file).</summary>
    private static string FolderPrefix(string relativeFolder)
    {
        string folder = relativeFolder.Replace('\\', '/');
        return folder == "." ? "" : folder.TrimEnd('/') + "/";
    }

    private static string Abbreviate(IReadOnlyCollection<string> files)
        => files.Count <= 3 ? string.Join(", ", files) : $"{string.Join(", ", files.Take(3))} and {files.Count - 3} more";
}
