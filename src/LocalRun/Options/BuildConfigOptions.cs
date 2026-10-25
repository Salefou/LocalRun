namespace LocalRun.Options;

public class BuildConfigOptions
{
    /// <summary>Add the projects with pending git changes to the --users-of names.</summary>
    public bool RetainGitChanges { get; internal set; }

    public List<string>? RetainDependsOn { get; internal set; }

    public List<string>? RetainIsDependencyOf { get; internal set; }

    public bool IncludeExternal { get; internal set; }

    /// <summary>Keep users of --users-of OR dependencies of --deps-of, instead of the projects between them.</summary>
    public bool Union { get; internal set; }

    /// <summary>Also keep the executables affected by the kept projects, with the libraries in between.</summary>
    public bool WithServices { get; internal set; }

    /// <summary>Skip querying the NuGet sources: <c>pack</c> then starts from the csproj versions.</summary>
    public bool CsprojVersionsOnly { get; internal set; }
}
