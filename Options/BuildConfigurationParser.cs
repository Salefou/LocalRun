namespace LocalRun.Options;

public static class BuildConfigOptionsParser
{
    public static BuildConfigOptions Parse(CommandLineArgs args)
    {
        return new BuildConfigOptions
        {
            RetainDirtyGitRepos = args.Flag("--git-changes"),
            RetainDependsOn = args.Values("--retain-depends-on"),
            RetainIsDependencyOf = args.Values("--retain-is-dependency-of"),
            IncludeExternal = args.Flag("--include-external"),
        };
    }
}
