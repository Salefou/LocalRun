namespace LocalRun.Core.Model;

using System.Collections.Generic;

public class LocalRunConfig
{
    public string RootPath { get; set; } = "";

    public string LocalNugetPath { get; set; } = "";

    public List<ProjectNode> Projects { get; set; } = new();
}