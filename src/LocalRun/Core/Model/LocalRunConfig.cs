namespace LocalRun.Core.Model;

using System.Collections.Generic;
using System.Text.Json.Serialization;

public class LocalRunConfig : IConfigFile
{
    public int SchemaVersion { get; set; } = IConfigFile.CurrentSchemaVersion;

    public string RootPath { get; set; } = "";

    public string LocalNugetPath { get; set; } = "";

    public List<ProjectNode> Projects { get; set; } = new();

    /// <summary>How <c>build</c> made it; null in configs written before 0.6.0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BuildRecord? Build { get; set; }
}