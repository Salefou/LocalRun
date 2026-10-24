namespace LocalRun.Options;

public static class BuildConfigOptionsParser
{
    public static BuildConfigOptions Parse(CommandLineArgs args)
    {
        return new BuildConfigOptions
        {
            RetainDirtyGitRepos = args.Flag("--git-changes"),
            RetainDependsOn = args.Values("--users-of"),
            RetainIsDependencyOf = args.Values("--deps-of"),
            IncludeExternal = args.Flag("--include-external"),
        };
    }
}
