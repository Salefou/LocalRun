namespace LocalRun.Core.Modules;

using LocalRun.Core.Model;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A plan's versions, told once: the iteration is shared by the whole plan, so most projects change the same way
/// (<c>local.60 → local.61</c>) and only the ones that differ are worth a line.
/// </summary>
internal static class VersionChanges
{
    /// <param name="arrow">The change symbol, which depends on the terminal.</param>
    /// <returns>
    /// The most common change, as a version-independent description, the projects making it, and the others with their
    /// full versions (in plan order). No common change when no two projects share one.
    /// </returns>
    public static (string? Common, int CommonCount, List<(string Name, string Change)> Others) Summarize(
        IReadOnlyList<(string Name, ProjectVersion From, ProjectVersion To)> plan, string arrow)
    {
        var described = plan.Select(proj => (proj.Name, Common: Describe(proj.From, proj.To, arrow),
            Full: $"{proj.From} {arrow} {proj.To}")).ToList();
        // The first of the largest groups, so equal groups keep the plan order
        var common = described.Where(proj => proj.Common != null)
            .GroupBy(proj => proj.Common)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault(group => group.Count() > 1);
        var others = described.Where(proj => common == null || proj.Common != common.Key)
            .Select(proj => (proj.Name, proj.Full)).ToList();
        return (common?.Key, common?.Count() ?? 0, others);
    }

    /// <summary>
    /// The change without the version numbers, so it can be shared: <c>local.60 → local.61</c>, <c>patch bump, release →
    /// local.1</c>. Null when it can't be told apart from the numbers (unparseable, or numbers changed otherwise than by a bump).
    /// </summary>
    internal static string? Describe(ProjectVersion from, ProjectVersion to, string arrow)
    {
        if (!NuGetVersion.TryParse(from.AsVersionString(), out var before) || !NuGetVersion.TryParse(to.AsVersionString(), out var after))
            return null;
        string labels = $"{Label(before)} {arrow} {Label(after)}";
        if (before.Version == after.Version)
            return before.Release == after.Release ? "unchanged" : labels;
        string? bump = (after.Major, after.Minor, after.Patch) switch
        {
            var (major, minor, patch) when major == before.Major + 1 && minor == 0 && patch == 0 => "major",
            var (major, minor, patch) when major == before.Major && minor == before.Minor + 1 && patch == 0 => "minor",
            var (major, minor, patch) when major == before.Major && minor == before.Minor && patch == before.Patch + 1 => "patch",
            _ => null,
        };
        return bump == null || after.Revision != 0 ? null : $"{bump} bump, {labels}";
    }

    private static string Label(NuGetVersion version) => version.IsPrerelease ? version.Release : "release";
}
