namespace LocalRun.Options;

using LocalRun.Core.Model;

public class BumpOptions
{
    public BumpType BumpType { get; init; }

    public string? Suffix { get; init; }

    internal ProjectVersion Bump(ProjectVersion version)
    {
        if (BumpType == BumpType.Major)
        {
            return new ProjectVersion()
            {
                Major = version.Major + 1,
                Minor = 0,
                PatchAndSuffix = $"0-{Suffix}",
            };
        }

        if (BumpType == BumpType.Minor)
        {
            return version with
            {
                Minor = version.Minor + 1,
                PatchAndSuffix = $"0-{Suffix}",
            };
        }

        string[] patchAndSuffix = version.PatchAndSuffix.Split(".", 2);
        string patchAsString = patchAndSuffix[0];
        if (!int.TryParse(patchAsString, out var patch))
        {
            if (patchAsString != "*")
            {
                throw new InvalidOperationException($"Cannot handle version format of {version.PatchAndSuffix}");
            }
            patch = 0;
        }

        if (BumpType == BumpType.Patch)
        {
            string suffixStartingFromPatch = Suffix == null ? string.Empty : $"-{Suffix}";
            return version with { PatchAndSuffix = $"{patch + 1}{suffixStartingFromPatch}" };
        }

        if (BumpType == BumpType.None)
        {
            if (Suffix == null)
            {
                string originalSuffix = patchAndSuffix.Length == 0 ? string.Empty : $".{patchAndSuffix[1]}";
                // patch mapped to 0 if was '*'
                return version with { PatchAndSuffix = $"{patch}{originalSuffix}" };
            }
            return version with { PatchAndSuffix = $"{patch}-{Suffix}" };
        }

        throw new InvalidOperationException($"Unsupported bumptype: {BumpType}");
    }
}

