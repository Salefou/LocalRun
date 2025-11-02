namespace LocalRun.Core.Model;

public class RepackConfig
{
    public required string LocalNugetPath { get; init; }

    public required List<RepackTree> RepackTrees { get; init; }
}