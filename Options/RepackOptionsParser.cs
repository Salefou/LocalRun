namespace LocalRun.Options;
internal class RepackOptionsParser
{
    public static RepackOptions Parse(string[] args)
    {
        BumpType bumpType = BumpType.None;
        string? suffix = null;
        bool remoteAsRef = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha":
                    bumpType = BumpType.Patch;
                    suffix = "alpha";
                    break;
                case "--remote":
                    remoteAsRef = true;
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
        };
    }
}

