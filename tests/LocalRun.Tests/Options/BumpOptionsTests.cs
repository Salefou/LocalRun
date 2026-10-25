namespace LocalRun.Tests.Options;

using LocalRun.Core.Model;
using LocalRun.Options;

public class BumpOptionsTests
{
    private static string Bump(string version, BumpType bumpType, string? suffix = null)
    {
        var options = new BumpOptions { BumpType = bumpType, Suffix = suffix };
        return options.Bump(ProjectVersion.Parse(version)).AsVersionString();
    }

    [Theory]
    // No bump: the version is kept, a suffix replaces the prerelease label
    [InlineData("1.4.2", BumpType.None, null, "1.4.2")]
    [InlineData("1.4.2-beta", BumpType.None, null, "1.4.2-beta")]
    [InlineData("1.4.2-beta.1", BumpType.None, null, "1.4.2-beta.1")]
    [InlineData("1.4.2.7", BumpType.None, null, "1.4.2.7")]
    [InlineData("1.4.*", BumpType.None, null, "1.4.0")]
    [InlineData("1.4.2", BumpType.None, "dev", "1.4.2-dev")]
    [InlineData("1.4.2-beta", BumpType.None, "dev", "1.4.2-dev")]
    [InlineData("1.4.2.7-beta", BumpType.None, "dev", "1.4.2.7-dev")]
    // Patch
    [InlineData("1.4.2", BumpType.Patch, null, "1.4.3")]
    [InlineData("1.4.2-beta", BumpType.Patch, null, "1.4.3")]
    [InlineData("1.4.2", BumpType.Patch, "dev", "1.4.3-dev")]
    [InlineData("1.4.2.7", BumpType.Patch, null, "1.4.3")]
    [InlineData("1.4.*", BumpType.Patch, null, "1.4.1")]
    // Minor and major reset the lower components
    [InlineData("1.4.2", BumpType.Minor, null, "1.5.0")]
    [InlineData("1.4.2-beta", BumpType.Minor, "dev", "1.5.0-dev")]
    [InlineData("1.4.2", BumpType.Major, null, "2.0.0")]
    [InlineData("1.4.2-beta", BumpType.Major, "dev", "2.0.0-dev")]
    // Next: a prerelease keeps its version when the new label continues it or sorts above it, else patch bump
    [InlineData("1.5.0-alpha.9", BumpType.Next, "alpha", "1.5.0-alpha")]
    [InlineData("1.5.0-alpha", BumpType.Next, "alpha", "1.5.0-alpha")]
    [InlineData("1.5.0-alpha.9", BumpType.Next, "beta", "1.5.0-beta")]
    [InlineData("2.3.1-beta", BumpType.Next, "alpha", "2.3.2-alpha")]
    [InlineData("0.9.0-rc.1", BumpType.Next, "alpha", "0.9.1-alpha")]
    [InlineData("1.5.0-alpha.x", BumpType.Next, "alpha", "1.5.1-alpha")]
    [InlineData("1.5.0-alpha.9", BumpType.Next, null, "1.5.0-alpha.9")]
    [InlineData("1.4.2.7-dev.3", BumpType.Next, "dev", "1.4.2.7-dev")]
    [InlineData("1.4.2", BumpType.Next, "alpha", "1.4.3-alpha")]
    [InlineData("1.4.2+sha", BumpType.Next, "alpha", "1.4.3-alpha")]
    [InlineData("1.4.2", BumpType.Next, null, "1.4.3")]
    public void Bump_ProducesTheExpectedVersion(string version, BumpType bumpType, string? suffix, string expected)
    {
        Assert.Equal(expected, Bump(version, bumpType, suffix));
    }

    [Theory]
    [InlineData("1.4.x")]
    [InlineData("1.4.-beta")]
    public void UnsupportedPatch_IsRejected(string version)
    {
        Assert.Throws<InvalidOperationException>(() => Bump(version, BumpType.Patch));
    }
}
