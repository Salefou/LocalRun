namespace LocalRun.Options;

public static class BuildConfigOptionsParser
{
    public static BuildConfigOptions Parse(CommandLineArgs args)
    {
        var options = new BuildConfigOptions
        {
            RetainDirtyGitRepos = args.Flag("--git-changes"),
            RetainDependsOn = args.Values("--users-of"),
            RetainIsDependencyOf = args.Values("--deps-of"),
            IncludeExternal = args.Flag("--include-external"),
            Union = args.Flag("--union"),
        };
        if (options.Union && (options.RetainDependsOn == null || options.RetainIsDependencyOf == null))
            throw new UsageException("'--union' combines '--users-of' and '--deps-of': give both.");
        return options;
    }
}
