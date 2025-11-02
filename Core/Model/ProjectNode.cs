namespace LocalRun.Core.Model;

using System.Collections.Generic;

public class ProjectNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public string? SolutionPath { get; init; }

    public required string Type { get; init; }

    public required ProjectVersion Version { get; init; }

    public required List<string> Dependencies { get; init; }

    public bool SkipRepack { get; init; }

    public bool SkipRun { get; init; }

    public override string ToString()
    {
        return $"{Name}({Type})->[{string.Join(", ", Dependencies)}]";
    }
}
