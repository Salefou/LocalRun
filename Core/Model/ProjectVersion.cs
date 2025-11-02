namespace LocalRun.Core.Model;

public readonly record struct ProjectVersion
{
    public required int Major { get; init; }

    public required int Minor { get; init; }

    public required string PatchAndSuffix { get; init; }

    internal static ProjectVersion Parse(string? value)
    {
        if (value == null) return new() { Major = 1, Minor = 0, PatchAndSuffix = "0" };
        string[] versionNumbers = value!.Split(".", 3);

        if (!int.TryParse(versionNumbers[0], out var majorInt))
        {
            throw new InvalidOperationException($"Invalid version string '{value}'");
        }
        if (!int.TryParse(versionNumbers[1], out var minorInt))
        {
            throw new InvalidOperationException($"Invalid version string '{value}'");
        }

        return new() { Major = majorInt, Minor = minorInt, PatchAndSuffix = versionNumbers[2] };
    }

    public string AsVersionString()
    {
        return $"{Major}.{Minor}.{PatchAndSuffix}";
    }

    public override string ToString()
    {
        return AsVersionString();
    }
}

