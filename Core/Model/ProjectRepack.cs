namespace LocalRun.Core.Model;

public class ProjectRepack
{
    public required string PathToCsproj { get; init; }

    public required ProjectVersion FromVersion { get; init; }

    public required ProjectVersion ToVersion { get; init; }
}