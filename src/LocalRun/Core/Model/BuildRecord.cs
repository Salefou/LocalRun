namespace LocalRun.Core.Model;

using LocalRun.Core.Modules;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// How <c>build</c> made a config: when, with which <c>lrun</c> and flags, from which commits, and what it left out.
/// <c>status</c> shows it; absent from configs written before it existed.
/// </summary>
public class BuildRecord
{
    public required DateTimeOffset Time { get; init; }

    /// <summary>The <c>lrun</c> that wrote the config, as <c>lrun version</c> prints it.</summary>
    public required string LrunVersion { get; init; }

    /// <summary>The filters given, as typed (<c>--deps-of WebApp, --with-services</c>), or null without any.</summary>
    public string? Filters { get; init; }

    public List<string>? UsersOf { get; init; }

    public List<string>? DepsOf { get; init; }

    public bool GitChanges { get; init; }

    public bool Union { get; init; }

    public bool WithServices { get; init; }

    /// <summary>The names given to <c>--exclude</c>; absent without it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Exclude { get; init; }

    public bool IncludeExternal { get; init; }

    public bool Tests { get; init; }

    public bool Csproj { get; init; }

    /// <summary>With <c>--git-changes</c>, the projects found changed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ChangedProjects { get; init; }

    /// <summary>Projects found, test projects and name collisions excluded.</summary>
    public int Scanned { get; init; }

    public List<LeftOutProjects> LeftOut { get; init; } = [];

    /// <summary>The git repositories holding the scanned projects, with their HEAD at build time.</summary>
    public List<RepositoryHead> Repositories { get; init; } = [];
}

/// <param name="Path">The repository's working directory, relative to the root (<c>.</c> for the root itself).</param>
/// <param name="Head">The commit checked out, or null in a repository without commits.</param>
/// <param name="Branch">The branch checked out, or null when HEAD is detached.</param>
public sealed record RepositoryHead(string Path, string? Head, string? Branch);
