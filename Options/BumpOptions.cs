namespace LocalRun.Options;

using LocalRun.Core.Model;
using System.Text.RegularExpressions;

public partial class BumpOptions
{
    public BumpType BumpType { get; init; }

    /// <summary>Prerelease label without the leading dash, e.g. "dev" for 1.4.3-dev.</summary>
    public string? Suffix { get; init; }

    /// <summary>
    /// A bump resets the lower components and replaces the prerelease label with <see cref="Suffix"/>
    /// (or drops it). Without a bump, the version is kept and only a given suffix replaces its label.
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

        return BumpType switch
        {
            BumpType.Major => new ProjectVersion { Major = version.Major + 1, Minor = 0, PatchAndSuffix = $"0{newLabel}" },
            BumpType.Minor => version with { Minor = version.Minor + 1, PatchAndSuffix = $"0{newLabel}" },
            BumpType.Patch => version with { PatchAndSuffix = $"{patch + 1}{newLabel}" },
            BumpType.None => version with { PatchAndSuffix = $"{patch}{revision}{(Suffix == null ? label : newLabel)}" },
            _ => throw new InvalidOperationException($"Unsupported bumptype: {BumpType}"),
        };
    }

    [GeneratedRegex(@"^(?<patch>\d+|\*)(?<revision>\.\d+)?(?<label>[-+].*)?$")]
    private static partial Regex PatchAndRestRegex();
}
