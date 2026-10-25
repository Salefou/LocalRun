namespace LocalRun.Options;

public enum BumpType
{
    None = 0,
    Patch = 1,
    Minor = 2,
    Major = 3,
    /// <summary>Continue a prerelease line: a prerelease keeps its version, a release is patch-bumped to start one.</summary>
    Next = 4,
}