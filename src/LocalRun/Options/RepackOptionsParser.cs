namespace LocalRun.Options;

using System.Text.RegularExpressions;

internal partial class RepackOptionsParser
{
    internal const string DefaultSuffix = "local";

    public static RepackOptions Parse(CommandLineArgs args)
    {
        BumpType bumpType = BumpType.None;
        string? suffix = args.Value("--suffix");
        if (suffix != null && !PrereleaseLabelRegex().IsMatch(suffix))
            throw new UsageException($"Invalid '--suffix' value '{suffix}': use letters, digits and '-', optionally in dot-separated parts (e.g. 'dev', 'beta.2'), without a leading dash.");

        List<string> bumpFlags = [];
        if (args.Flag("--patch"))
        {
            bumpType = BumpType.Patch;
            bumpFlags.Add("--patch");
        }
        if (args.Flag("--minor"))
        {
            bumpType = BumpType.Minor;
            bumpFlags.Add("--minor");
        }
        if (args.Flag("--major"))
        {
            bumpType = BumpType.Major;
            bumpFlags.Add("--major");
        }
        if (args.Flag("--alpha"))
        {
            // convenience
            if (suffix != null)
                throw new UsageException("'--alpha' cannot be combined with '--suffix'.");
            bumpType = BumpType.Patch;
            suffix = "alpha";
            bumpFlags.Add("--alpha");
        }
        if (bumpFlags.Count > 1)
            throw new UsageException($"Only one version bump can be given, got {string.Join(", ", bumpFlags)}.");

        // Never plan stable versions for the local feed: a local 1.4.3 would sit in ~/.nuget/packages and
        // shadow the official 1.4.3 once it is published, since NuGet never downloads a cached version again
        suffix ??= DefaultSuffix;

        return new RepackOptions()
        {
            BumpOptions = new BumpOptions()
            {
                BumpType = bumpType,
                Suffix = suffix,
            },
            UseRemoteAsVersionReference = args.Flag("--remote"),
            CleanBeforeRestore = args.Flag("--clean"),
        };
    }

    // SemVer 2 prerelease identifiers: non-empty [0-9A-Za-z-] parts separated by dots
    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z-]*(\.[0-9A-Za-z-]+)*$")]
    private static partial Regex PrereleaseLabelRegex();
}

