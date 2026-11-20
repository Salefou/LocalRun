namespace LocalRun.Cli;

using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <param name="Text">The entry as markdown, its wrapped lines joined (a nested list keeps its own lines).</param>
/// <param name="Breaking">Whether it starts with <c>**Breaking:**</c>: something that worked may now fail or behave differently.</param>
internal sealed record ChangelogEntry(string Text, bool Breaking);

/// <param name="Kind">The heading as written: <c>Added</c>, <c>Changed</c>, <c>Fixed</c>, <c>Removed</c>...</param>
internal sealed record ChangelogSection(string Kind, IReadOnlyList<ChangelogEntry> Entries);

/// <param name="Version">The release, or for <c>[Unreleased]</c> the version it will ship as (the running one).</param>
/// <param name="Date">The release date (<c>yyyy-MM-dd</c>), null when unreleased.</param>
/// <param name="Unreleased">The <c>[Unreleased]</c> section, in a build made after the last release.</param>
/// <param name="Summary">The paragraph a release may open with, or null.</param>
/// <param name="Markdown">The section as written, for the text output.</param>
internal sealed record ChangelogRelease(string Version, string? Date, bool Unreleased, string? Summary,
    IReadOnlyList<ChangelogSection> Sections, [property: JsonIgnore] string Markdown);

/// <param name="Since">The version given to <c>--since</c>, or null for the whole changelog.</param>
/// <param name="Releases">Newest first.</param>
internal sealed record ChangelogResult(string? Since, IReadOnlyList<ChangelogRelease> Releases);

/// <summary>
/// The repository's <c>CHANGELOG.md</c>, embedded in the tool and read by <c>lrun changelog</c>, so an agent can see what
/// changed since the version its instructions were written for, offline. Expects the Keep a Changelog layout:
/// <c>## [version] - date</c>, <c>### Kind</c>, <c>- </c> entries, link references at the end.
/// </summary>
internal static partial class Changelog
{
    private const string UnreleasedHeading = "Unreleased";
    private const string BreakingMark = "**Breaking:**";

    public static string Text()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("LocalRun.CHANGELOG.md")
            ?? throw new InvalidOperationException("CHANGELOG.md is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The releases of <paramref name="text"/>, newest first. <c>[Unreleased]</c> is kept only when it has entries and
    /// <paramref name="running"/> isn't released yet (a local build), as that version.
    /// </summary>
    public static List<ChangelogRelease> Parse(string text, string running)
    {
        List<ChangelogRelease> releases = [];
        Builder? current = null;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (LinkReference().IsMatch(line))
                break;
            var heading = ReleaseHeading().Match(line);
            if (heading.Success)
            {
                if (current != null)
                    releases.Add(current.Build());
                bool unreleased = heading.Groups["version"].Value.Equals(UnreleasedHeading, StringComparison.OrdinalIgnoreCase);
                current = new Builder(unreleased ? running : heading.Groups["version"].Value,
                    heading.Groups["date"].Success ? heading.Groups["date"].Value : null, unreleased);
            }
            current?.Add(line);
        }
        if (current != null)
            releases.Add(current.Build());

        return [.. releases.Where(release => !release.Unreleased
            || (release.Sections.Any(section => section.Entries.Count != 0) && !releases.Any(other => !other.Unreleased && other.Version == running)))];
    }

    /// <summary>The releases newer than <paramref name="since"/> (all of them when null), in SemVer order.</summary>
    public static List<ChangelogRelease> Since(IEnumerable<ChangelogRelease> releases, NuGetVersion? since) =>
        [.. releases.Where(release => since == null || (NuGetVersion.TryParse(release.Version, out var version) && version > since))];

    private sealed class Builder(string version, string? date, bool unreleased)
    {
        private readonly List<string> lines = [];
        private readonly List<string> summary = [];
        private readonly List<(string Kind, List<string> Entries)> sections = [];
        private bool inEntry;

        public void Add(string line)
        {
            lines.Add(line);
            if (lines.Count == 1)
                return;
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                sections.Add((line[4..].Trim(), []));
                inEntry = false;
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                inEntry = false;
            }
            else if (sections.Count == 0)
            {
                summary.Add(line.Trim());
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || !inEntry)
            {
                sections[^1].Entries.Add(line.StartsWith("- ", StringComparison.Ordinal) ? line[2..].Trim() : line.Trim());
                inEntry = true;
            }
            else
            {
                // A wrapped line joins its entry; a nested list item keeps its own line
                string trimmed = line.Trim();
                var entries = sections[^1].Entries;
                entries[^1] += trimmed.StartsWith("- ", StringComparison.Ordinal) ? $"\n  {trimmed}" : $" {trimmed}";
            }
        }

        public ChangelogRelease Build() => new(version, date, unreleased, summary.Count == 0 ? null : string.Join(' ', summary),
            [.. sections.Select(section => new ChangelogSection(section.Kind,
                [.. section.Entries.Select(entry => new ChangelogEntry(entry, entry.StartsWith(BreakingMark, StringComparison.Ordinal)))]))],
            string.Join('\n', lines).TrimEnd());
    }

    [GeneratedRegex(@"^## \[(?<version>[^\]]+)\](?: - (?<date>\d{4}-\d{2}-\d{2}))?\s*$")]
    private static partial Regex ReleaseHeading();

    [GeneratedRegex(@"^\[[^\]]+\]: \S")]
    private static partial Regex LinkReference();
}
