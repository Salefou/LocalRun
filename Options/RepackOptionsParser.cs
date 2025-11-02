namespace LocalRun.Options;
internal class RepackOptionsParser
{
    public static RepackOptions Parse(string[] args)
    {
        BumpType bumpType = BumpType.None;
        string? suffix = null;
        bool remoteAsRef = false;
        bool clean = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--remote":
                    remoteAsRef = true;
                    break;
                case "--clean":
                    clean = true;
                    break;

                case "--suffix":
                    suffix = args[i + 1];
                    break;
                case "--patch":
                    bumpType = BumpType.Patch;
                    break;
                case "--minor":
                    bumpType = BumpType.Minor;
                    break;
                case "--major":
                    bumpType = BumpType.Major;
                    break;
                case "--alpha":
                    // convenience
                    bumpType = BumpType.Patch;
                    suffix = "alpha";
                    break;
            }
        }

        return new RepackOptions()
        {
            BumpOptions = new BumpOptions()
            {
                BumpType = bumpType,
                Suffix = suffix,
            },
            UseRemoteAsVersionReference = remoteAsRef,
            CleanBeforeRestore = clean,
        };
    }
}

