namespace LocalRun.Core.Model;

public class RepackConfig
{
    public required string LocalNugetPath { get; init; }

    public required List<ProjectRepack> Projects { get; init; }
}