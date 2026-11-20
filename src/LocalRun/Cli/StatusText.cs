namespace LocalRun.Cli;

using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>
/// How <c>status</c> prints its result: the config and how it was built, its projects by solution and type, what was
/// left out, the plan. The listing is the result, so it is printed in full, one line per project.
/// </summary>
internal static class StatusText
{
    private static readonly ILogger logger = Log.For(typeof(StatusText).FullName!);

    /// <summary>Plan entries shown per depth, as <c>go</c> shows names.</summary>
    private const int ShownPerDepth = 6;

    public static void Print(StatusResult status)
    {
        var now = DateTimeOffset.Now;
        var build = status.Build;
        if (build != null)
        {
            logger.LogInformation("Config: {Path}, built {Time} ({Age}) by {Command} {Version}", status.Config, Stamp(build.Time),
                Text.Age(now - build.Time), LocalRunCli.CommandName, build.LrunVersion);
        }
        else
        {
            logger.LogInformation("Config: {Path}, written {Time} ({Age})", status.Config, Stamp(status.Written), Text.Age(now - status.Written));
        }
        logger.LogInformation("Root: {Root}", status.Root);
        logger.LogInformation("Feed: {Feed}", status.LocalNuget);
        if (build == null)
        {
            logger.LogInformation("Built by an older {Command}, which didn't record how: '{Command} build' again records its flags, " +
                "what it left out and each repository's commit.", LocalRunCli.CommandName, LocalRunCli.CommandName);
        }
        else
        {
            logger.LogInformation("Built with: {Flags}", Flags(build) ?? "no filter");
            if (build.ChangedProjects != null)
                logger.LogInformation("Changed (--git-changes): {Projects}", build.ChangedProjects.Count == 0 ? "none" : Text.Abbreviate(build.ChangedProjects));
            if (build.Repositories.Count != 0)
                logger.LogInformation("Repositories at build: {Repositories}", Text.Abbreviate(build.Repositories.Select(Describe)));
        }

        PrintProjects(status);
        if (build != null)
            PrintLeftOut(build);
        PrintPlan(status.Plan, now);
    }

    private static void PrintProjects(StatusResult status)
    {
        int tests = status.Projects.Count(proj => proj.Test);
        if (status.Projects.Count == 0)
        {
            logger.LogInformation("Projects: none");
            return;
        }
        logger.LogInformation("Projects: {Total}, {Libraries}, {Executables}{Tests}", status.Counts.Total,
            Text.Count(status.Counts.Libraries - tests, "library", "libraries"), Text.Count(status.Counts.Executables, "executable"),
            tests == 0 ? "" : $", {Text.Count(tests, "test project")}");

        int nameWidth = status.Projects.Max(proj => proj.Name.Length);
        int versionWidth = status.Projects.Max(proj => proj.Version.Length);
        var solutions = status.Projects
            .GroupBy(proj => proj.Solution ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderBy(solution => solution.Key == "")
            .ThenBy(solution => solution.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var solution in solutions)
        {
            logger.LogInformation("{Solution}", solution.Key == "" ? "(no solution)" : Path.GetRelativePath(status.Root, solution.Key));
            foreach (var (kind, projects) in Kinds(solution))
            {
                logger.LogInformation("  {Kind}", kind);
                foreach (var proj in projects)
                {
                    string version = proj.LatestVersion == null ? proj.Version : $"{proj.Version.PadRight(versionWidth)}  latest {proj.LatestVersion}";
                    logger.LogInformation("    {Project}  d{Depth}  {Version}", proj.Name.PadRight(nameWidth), proj.Depth, version);
                }
            }
        }
    }

    private static IEnumerable<(string Kind, List<ListedProject> Projects)> Kinds(IEnumerable<ListedProject> projects)
    {
        var all = projects.ToList();
        (string, List<ListedProject>)[] kinds =
        [
            ("libraries", [.. all.Where(proj => !proj.Test && proj.Type != "executable")]),
            ("executables", [.. all.Where(proj => !proj.Test && proj.Type == "executable")]),
            ("tests", [.. all.Where(proj => proj.Test)]),
        ];
        return kinds.Where(kind => kind.Item2.Count != 0);
    }

    private static void PrintLeftOut(BuildRecord build)
    {
        foreach (var leftOut in build.LeftOut)
        {
            string paths = leftOut.Paths is { Count: > 0 } found ? $" ({string.Join(", ", found)})" : "";
            string dependents = leftOut.Dependents is { Count: > 0 } kept ? $"; kept without it: {Text.Abbreviate(kept)}" : "";
            logger.LogInformation("Left out ({Reason}): {Projects}{Paths}{Dependents}", leftOut.Reason, Text.Abbreviate(leftOut.Projects),
                paths, dependents);
        }
    }

    private static void PrintPlan(StatusPlan? plan, DateTimeOffset now)
    {
        if (plan == null)
        {
            logger.LogInformation("Plan: none ('{Command} pack --dry-run' writes one)", LocalRunCli.CommandName);
            return;
        }
        logger.LogInformation("Plan: {Path}, written {Time} ({Age}), {Packages}{Clean}", plan.Path, Stamp(plan.Written),
            Text.Age(now - plan.Written), Text.Count(plan.Packages.Count, "package"), plan.Clean ? ", cleaning first" : "");
        if (plan.OlderThanConfig)
        {
            logger.LogWarning(DiagnosticEvents.PlanOlderThanConfig, "The plan is older than the config: it was made from an earlier " +
                "one, and go would pack that. '{Command} pack' plans again (with --dry-run to review it first).", LocalRunCli.CommandName);
        }
        var depths = plan.Packages.GroupBy(package => package.Depth).OrderBy(depth => depth.Key).ToList();
        for (int i = 0; i < depths.Count; i++)
        {
            logger.LogInformation("  Depth {Depth}/{Depths}: {Packages}", i + 1, depths.Count,
                Text.Abbreviate(depths[i].Select(package => $"{package.Project} {package.From} {Terminal.Arrow} {package.To}"), ShownPerDepth));
        }
    }

    /// <summary>Every flag the build was given, filters first, as typed; null without any.</summary>
    internal static string? Flags(BuildRecord build)
    {
        List<string> flags = [];
        if (build.Filters != null) flags.Add(build.Filters);
        if (build.IncludeExternal) flags.Add("--include-external");
        if (build.Tests) flags.Add("--tests");
        if (build.Csproj) flags.Add("--csproj");
        return flags.Count == 0 ? null : string.Join(", ", flags);
    }

    /// <summary><c>shop main@1a2b3c4</c>, <c>. (detached) 9f8e7d6</c>.</summary>
    private static string Describe(RepositoryHead repository)
    {
        string head = repository.Head == null ? "no commit" : repository.Head[..Math.Min(7, repository.Head.Length)];
        return repository.Branch == null ? $"{repository.Path} (detached) {head}" : $"{repository.Path} {repository.Branch}@{head}";
    }

    private static string Stamp(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
