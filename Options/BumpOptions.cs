namespace LocalRun.Options;

using LocalRun.Core.Model;

public class BumpOptions
{
    public bool MajorBump { get; init; }

    public bool MinorBump { get; init; }

    public bool PatchBump { get; init; }

    public string? Suffix { get; init; }

    internal ProjectVersion Bump(ProjectVersion version)
    {
        if (MajorBump)
        {
            if (MinorBump || PatchBump) Console.WriteLine("Bumping major, ignoring other bumps");
            return new ProjectVersion()
            {
                Major = version.Major + 1,
                Minor = 0,
                PatchAndSuffix = $"0-{Suffix}",
            };
        }

        if (MinorBump)
        {
            if (PatchBump) Console.WriteLine("Bumping minor, ignoring patch bump");
            return version with
            {
                Minor = version.Minor + 1,
                PatchAndSuffix = $"0-{Suffix}",
            };
        }

        string[] patchPlusSuffix = [.. version.PatchAndSuffix.Split(".", 2).SelectMany(patchOrSuffix => patchOrSuffix.Split('.', 2))];
        var patch = 0;

        if (patchPlusSuffix[0] != "*" && !int.TryParse(patchPlusSuffix[0], out patch))
        {
            throw new InvalidOperationException($"Cannot handle {string.Join("-", version.PatchAndSuffix)}");
        }

        string suffixStartingFromPatch = Suffix == null ? string.Empty : $"-{Suffix}";
        if (PatchBump)
        {
            return version with { PatchAndSuffix = $"{patch + 1}{suffixStartingFromPatch}" };
        }

        return version with { PatchAndSuffix = $"{patchPlusSuffix[0]}{suffixStartingFromPatch}" };
    }
}

