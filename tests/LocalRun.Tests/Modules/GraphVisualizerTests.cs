namespace LocalRun.Tests.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;

public class GraphVisualizerTests
{
    private static ProjectNode Project(string name, string type, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = type,
        Version = ProjectVersion.Parse("1.0.0"),
        Dependencies = [.. dependencies],
    };

    private static string[] Dot(params ProjectNode[] projects)
        => GraphVisualizer.ToDot(DependencyGraph.FromConfig(new LocalRunConfig { Projects = [.. projects] }))
            .Split('\n', StringSplitOptions.TrimEntries);

    [Fact]
    public void DependencyOutsideTheConfig_IsDashedAndUnfilled_WithDashedEdges()
    {
        // Primitives was left out (by a filter, say), but Models and App still use it
        var dot = Dot(
            Project("Models", "library", "Primitives"),
            Project("App", "executable", "Models", "Primitives"));

        Assert.Single(dot, line => line.StartsWith("\"Primitives\" ["));
        Assert.Contains("\"Primitives\" [style=\"dashed, rounded\", color=\"#999999\", fontcolor=\"#777777\"];", dot);
        Assert.Contains("\"Primitives\" -> \"Models\" [style=dashed];", dot);
        Assert.Contains("\"Primitives\" -> \"App\" [style=dashed];", dot);
        Assert.Contains("\"Models\" -> \"App\";", dot);
    }

    [Fact]
    public void ProjectsOfTheConfig_AreDeclaredBeforeAnyEdge()
    {
        // Graphviz creates a node with the default style when an edge names it first
        var dot = Dot(
            Project("Models", "library", "Primitives"),
            Project("App", "executable", "Models"));

        int lastNode = Array.FindLastIndex(dot, line => line.EndsWith("];") && !line.Contains("->") && line.StartsWith('"'));
        int firstEdge = Array.FindIndex(dot, line => line.Contains("->"));
        Assert.True(lastNode < firstEdge);
    }

    [Fact]
    public void LatestVersion_IsShownOnlyWhenItDiffers()
    {
        var behind = Project("Behind", "library");
        behind.LatestVersion = ProjectVersion.Parse("1.2.0");
        var upToDate = Project("UpToDate", "library");
        upToDate.LatestVersion = ProjectVersion.Parse("1.0.0");

        var dot = Dot(behind, upToDate);

        Assert.Contains(dot, line => line.StartsWith("\"Behind\" [label=\"Behind\\n1.0.0\\nlatest 1.2.0\\n\""));
        Assert.Contains(dot, line => line.StartsWith("\"UpToDate\" [label=\"UpToDate\\n1.0.0\\n\""));
    }

    [Fact]
    public void Executables_AreGreen_LibrariesBlue()
    {
        var dot = Dot(Project("Lib", "library"), Project("Tool", "executable"));

        Assert.Contains(dot, line => line.StartsWith("\"Lib\" [") && line.Contains("fillcolor=\"#5BA0E5\""));
        Assert.Contains(dot, line => line.StartsWith("\"Tool\" [") && line.Contains("fillcolor=\"#5BE55B\""));
    }
}
