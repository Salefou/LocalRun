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

    /// <summary>A test project, kept with <c>build --tests</c> (written only when true).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Test { get; init; }

    /// <summary>
    /// Whether the project produces a package, when it isn't the default (written only then): false for a library with
    /// <c>IsPackable</c> false, true for a test project with <c>IsPackable</c> set to true in its csproj.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Packable { get; init; }

    /// <summary>
    /// What <c>pack</c> plans, <c>build</c> looks up on the sources and <c>verify</c> expects in the feed: a library, unless
    /// <see cref="Packable"/> says otherwise; a test project only when <see cref="Packable"/> is true. Never an executable.
    /// </summary>
    [JsonIgnore]
    public bool ProducesPackage => Type != "executable" && (Packable ?? !Test);

    public required ProjectVersion Version { get; init; }

    /// <summary>Highest version on the project's NuGet sources (unless <c>build --csproj</c>); <c>pack</c> starts from it when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectVersion? LatestVersion { get; set; }

    public required List<string> Dependencies { get; init; }

    public override string ToString()
    {
        return $"{Name}({Type})->[{string.Join(", ", Dependencies)}]";
    }
}
