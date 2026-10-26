namespace LocalRun.Core.Model;

public class ProjectRepack
{
    public required string PathToCsproj { get; init; }

    public required string Name { get; init; }

    /// <summary>The solution of the project, whose <c>Solution*</c> properties its commands get (<c>$(SolutionDir)</c>).</summary>
    public string? SolutionPath { get; init; }

    public required int Depth { get; init; }

    public required ProjectVersion FromVersion { get; init; }

    public required ProjectVersion ToVersion { get; init; }


    public override string ToString()
    {
        return $"{Path.GetFileNameWithoutExtension(PathToCsproj)}[{Depth}]: {FromVersion} -> {ToVersion}";
    }
}