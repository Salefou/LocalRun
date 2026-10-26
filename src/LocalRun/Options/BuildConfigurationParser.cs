namespace LocalRun.Options;

public static class BuildConfigOptionsParser
{
    public static BuildConfigOptions Parse(CommandLineArgs args)
    {
        var options = new BuildConfigOptions
        {
            RetainGitChanges = args.Flag("--git-changes"),
            RetainDependsOn = args.Values("--users-of"),
            RetainIsDependencyOf = args.Values("--deps-of"),
            IncludeExternal = args.Flag("--include-external"),
            IncludeTests = args.Flag("--tests"),
            Union = args.Flag("--union"),
            WithServices = args.Flag("--with-services"),
            CsprojVersionsOnly = args.Flag("--csproj"),
        };
        // --git-changes adds to the --users-of side
        bool hasUsersOf = options.RetainDependsOn != null || options.RetainGitChanges;
        if (options.Union && (!hasUsersOf || options.RetainIsDependencyOf == null))
            throw new UsageException("'--union' combines '--users-of' (or '--git-changes') and '--deps-of': give both.");
        if (options.WithServices && !hasUsersOf && options.RetainIsDependencyOf == null)
            throw new UsageException("'--with-services' has nothing to extend: give '--users-of', '--git-changes' and/or '--deps-of'.");
        return options;
    }
}
