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

    /// <summary>The planned version; after <c>go</c>, the version actually packed when its build set another one.</summary>
    public required ProjectVersion ToVersion { get; set; }

    /// <summary>
    /// Set by <c>go</c> when <c>dotnet pack</c> produced no package for it (written only when true): <c>verify</c> no longer
    /// expects it. Still packed by the next <c>go</c>, which warns again until the csproj says <c>IsPackable</c> false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NotPacked { get; set; }

    public override string ToString()
    {
        return $"{Path.GetFileNameWithoutExtension(PathToCsproj)}[{Depth}]: {FromVersion} -> {ToVersion}";
    }
}