namespace LocalRun.Options;

public class BuildConfigOptions
{
    public bool RetainDirtyGitRepos { get; internal set; }

    public List<string>? RetainDependsOn { get; internal set; }

    public List<string>? RetainIsDependencyOf { get; internal set; }

    public bool IncludeExternal { get; internal set; }
}
