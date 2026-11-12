namespace LocalRun.Core.Model;

public class RepackConfig : IConfigFile
{
    public int SchemaVersion { get; init; } = IConfigFile.CurrentSchemaVersion;

    public required string LocalNugetPath { get; init; }

    public required List<RepackTree> RepackTrees { get; init; }

    public required bool Clean { get; init; }
}