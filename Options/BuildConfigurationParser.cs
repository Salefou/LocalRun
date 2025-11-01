namespace LocalRun.Options;

public static class BuildConfigOptionsParser
{
    public static BuildConfigOptions Parse(string[] args)
    {
        var options = new BuildConfigOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--git-changes":
                    options.RetainDirtyGitRepos = true;
                    break;

                case "--retain-depends-on":
                    options.RetainDependsOn = [.. ReadValues(args, ref i)];
                    break;

                case "--retain-is-dependency-of":
                    options.RetainIsDependencyOf = [.. ReadValues(args, ref i)];
                    break;
                case "--include-external":
                    options.IncludeExternal = true;
                    break;
            }
        }

        return options;
    }

    private static List<string> ReadValues(string[] args, ref int i)
    {
        var values = new List<string>();
        for (int j = i + 1; j < args.Length; j++)
        {
            if (args[j].StartsWith("--"))
            {
                i = j - 1;
                break;
            }
            values.Add(args[j]);
            i = j;
        }
        return values;
    }
}
