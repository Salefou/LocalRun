namespace LocalRun.Core.Model;

using System.Collections.Generic;
using System.Text.Json.Serialization;

public class ProjectNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public string? SolutionPath { get; init; }

    public required string Type { get; init; }

    public required ProjectVersion Version { get; init; }

    /// <summary>Highest version on the project's NuGet sources, set by <c>build --remote</c>; <c>pack</c> starts from it when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectVersion? RemoteVersion { get; set; }

    public required List<string> Dependencies { get; init; }

    public override string ToString()
    {
        return $"{Name}({Type})->[{string.Join(", ", Dependencies)}]";
    }
}
