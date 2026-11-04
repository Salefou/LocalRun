namespace LocalRun.Core.Model;

using System.Collections.Generic;
using System.Text.Json.Serialization;

public class ProjectNode
{
    public required string Name { get; init; }

    /// <summary>The id the project is packed under (<c>PackageId</c>, else <c>AssemblyName</c>), when it isn't <see cref="Name"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackageId { get; init; }

    /// <summary>The id the project is packed under: what the NuGet sources and package folders know it as.</summary>
    [JsonIgnore]
    public string PackageIdOrName => PackageId ?? Name;

    public required string Path { get; init; }

    public string? SolutionPath { get; init; }

    public required string Type { get; init; }

    public required ProjectVersion Version { get; init; }

    /// <summary>Highest version on the project's NuGet sources (unless <c>build --csproj</c>); <c>pack</c> starts from it when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectVersion? LatestVersion { get; set; }

    public required List<string> Dependencies { get; init; }

    /// <summary>
    /// The <see cref="Dependencies"/> that are <c>ProjectReference</c>s, which a restore walks into (the others are packages).
    /// Known while <c>build</c> scans the sources only: not written to the config.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyCollection<string> ProjectReferences { get; init; } = [];

    public override string ToString()
    {
        return $"{Name}({Type})->[{string.Join(", ", Dependencies)}]";
    }
}
