namespace LocalRun.Core.Modules;

using LocalRun.Core.Model;
using NuGet.Versioning;
using System.Globalization;

/// <summary>
/// The iteration number <c>pack</c> appends to the prerelease label (<c>1.5.0-alpha.3</c>): one more than the highest
/// iteration of the planned versions already in the given folders or as a base, so repacking never reuses a version
/// NuGet has cached. One number for the whole plan, so the projects of an attempt share it.
/// </summary>
internal static class PackageIterations
{
    /// <param name="plan">Each package with its planned version (before the iteration) and the version it starts from,
    /// which may come from a source none of the folders mirrors (1.5.0-alpha.9 as a base → at least .10).</param>
    public static int Next(IEnumerable<(string PackageId, ProjectVersion Target, ProjectVersion Base)> plan, IEnumerable<string> folders)
    {
        var existingFolders = folders.Where(Directory.Exists).ToList();
        int highest = 0;
        foreach (var (packageId, target, baseVersion) in plan)
        {
            if (!NuGetVersion.TryParse(target.AsVersionString(), out var targetVersion))
                continue;
            if (NuGetVersion.TryParse(baseVersion.AsVersionString(), out var parsedBase) && IterationOf(parsedBase, targetVersion) is int baseIteration)
                highest = Math.Max(highest, baseIteration);
            foreach (var folder in existingFolders)
            {
                foreach (var packed in PackedVersions(folder, packageId))
                {
                    if (IterationOf(packed, targetVersion) is int iteration)
                        highest = Math.Max(highest, iteration);
                }
            }
        }
        return highest + 1;
    }

    /// <summary>
    /// The versions of a package in a folder, laid out as a flat feed (<c>id.version.nupkg</c>, what <c>dotnet pack -o</c>
    /// writes) or as the global packages folder (<c>id/version/</c>).
    /// </summary>
    private static IEnumerable<NuGetVersion> PackedVersions(string folder, string packageId)
    {
        string prefix = packageId + ".";
        foreach (var file in Directory.EnumerateFiles(folder, "*.nupkg"))
        {
            // Another package sharing the prefix (Foo.Bar.1.0.0 for Foo) leaves no parsable version
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && NuGetVersion.TryParse(name[prefix.Length..], out var version))
                yield return version;
        }
        string packageFolder = Path.Combine(folder, packageId.ToLowerInvariant());
        if (!Directory.Exists(packageFolder))
            yield break;
        foreach (var versionFolder in Directory.EnumerateDirectories(packageFolder))
        {
            if (NuGetVersion.TryParse(Path.GetFileName(versionFolder), out var version))
                yield return version;
        }
    }

    /// <summary>N when <paramref name="packed"/> is <paramref name="target"/> with one more, numeric, label part (<c>1.5.0-alpha.N</c>).</summary>
    internal static int? IterationOf(NuGetVersion packed, NuGetVersion target)
    {
        var labels = packed.ReleaseLabels.ToList();
        var targetLabels = target.ReleaseLabels.ToList();
        if (packed.Version != target.Version || labels.Count != targetLabels.Count + 1)
            return null;
        if (!labels.Take(targetLabels.Count).SequenceEqual(targetLabels, StringComparer.OrdinalIgnoreCase))
            return null;
        return int.TryParse(labels[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int iteration) ? iteration : null;
    }
}
