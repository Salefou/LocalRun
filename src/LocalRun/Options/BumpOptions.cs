namespace LocalRun.Options;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using NuGet.Versioning;
using System.Text.RegularExpressions;

public partial class BumpOptions
{
    public BumpType BumpType { get; init; }

    /// <summary>Prerelease label without the leading dash, e.g. "dev" for 1.4.3-dev.</summary>
    public string? Suffix { get; init; }

    /// <summary>
    /// A bump resets the lower components and replaces the prerelease label with <see cref="Suffix"/>
    /// (or drops it). Without a bump, the version is kept and only a given suffix replaces its label.
    /// <see cref="BumpType.Next"/> is no bump on a prerelease it continues or sorts above, a patch bump otherwise.
    /// </summary>
    internal ProjectVersion Bump(ProjectVersion version)
    {
        // PatchAndSuffix is everything after "Major.Minor.": "2", "*", "2-beta", "2.1" (revision), "2-beta+sha"
        var match = PatchAndRestRegex().Match(version.PatchAndSuffix);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Cannot handle version format of {version.PatchAndSuffix}");
        }
        // A floating patch ('*') is packed as 0
        int patch = match.Groups["patch"].Value == "*" ? 0 : int.Parse(match.Groups["patch"].Value);
        string revision = match.Groups["revision"].Value;
        string label = match.Groups["label"].Value;
        string newLabel = Suffix == null ? string.Empty : $"-{Suffix}";

        var kept = version with { PatchAndSuffix = $"{patch}{revision}{(Suffix == null ? label : newLabel)}" };

        // Next continues a prerelease (1.5.0-alpha.9 stays 1.5.0, the iteration then goes up) and starts one after a
        // release, which a prerelease of the same version would sort below (1.4.2-alpha < 1.4.2). Same for a
        // prerelease whose label sorts above the new one (2.3.1-beta → 2.3.2-alpha, not 2.3.1-alpha)
        var bumpType = BumpType;
        if (BumpType == BumpType.Next)
            bumpType = label.StartsWith('-') && (Suffix == null || ContinuesOrSortsAbove(version, kept)) ? BumpType.None : BumpType.Patch;

        return bumpType switch
        {
            BumpType.Major => new ProjectVersion { Major = version.Major + 1, Minor = 0, PatchAndSuffix = $"0{newLabel}" },
            BumpType.Minor => version with { Minor = version.Minor + 1, PatchAndSuffix = $"0{newLabel}" },
            BumpType.Patch => version with { PatchAndSuffix = $"{patch + 1}{newLabel}" },
            BumpType.None => kept,
            _ => throw new InvalidOperationException($"Unsupported bumptype: {BumpType}"),
        };
    }

    /// <summary>Whether <paramref name="kept"/>, once given its iteration, is on the line of the prerelease <paramref name="version"/> or above it.</summary>
    private static bool ContinuesOrSortsAbove(ProjectVersion version, ProjectVersion kept)
    {
        if (!NuGetVersion.TryParse(version.AsVersionString(), out var current) || !NuGetVersion.TryParse($"{kept.AsVersionString()}.1", out var first))
            return true;
        var keptVersion = NuGetVersion.Parse(kept.AsVersionString());
        return keptVersion == current || PackageIterations.IterationOf(current, keptVersion) != null || first > current;
    }

    [GeneratedRegex(@"^(?<patch>\d+|\*)(?<revision>\.\d+)?(?<label>[-+].*)?$")]
    private static partial Regex PatchAndRestRegex();
}
