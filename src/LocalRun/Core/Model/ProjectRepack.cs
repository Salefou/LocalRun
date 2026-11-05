namespace LocalRun.Core.Model;

using System.Text.Json.Serialization;

public class ProjectRepack
{
    public required string PathToCsproj { get; init; }

    public required string Name { get; init; }

    /// <summary>The id the project is packed under, when it isn't <see cref="Name"/> (absent from plans written before 0.4.1).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackageId { get; init; }

    [JsonIgnore]
    public string PackageIdOrName => PackageId ?? Name;

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