namespace LocalRun.Tests.Graph;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;

public class DependencyGraphTests
{
    private static ProjectNode Project(string name, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = "library",
        Version = new ProjectVersion { Major = 1, Minor = 0, PatchAndSuffix = "0" },
        Dependencies = [.. dependencies],
    };

    private static DependencyGraph Graph(params ProjectNode[] projects)
        => DependencyGraph.FromConfig(new LocalRunConfig { Projects = [.. projects] });

    [Fact]
    public void Diamond_DependentIsDeeperThanAllItsDependencies()
    {
        // A <- B <- C, plus the shortcut A <- C: C must come after B, not alongside it.
        var graph = Graph(
            Project("A"),
            Project("B", "A"),
            Project("C", "A", "B"));

        var depth = graph.ComputeNameToDepth();

        Assert.Equal(0, depth["A"]);
        Assert.Equal(1, depth["B"]);
        Assert.Equal(2, depth["C"]);
        Assert.Equal(["A", "B", "C"], graph.TopologicalOrder().Select(p => p.Name));
    }

    [Fact]
    public void Depth_IsLongestPathFromARoot()
    {
        // D is one hop from A directly, but three hops via B and C.
        var graph = Graph(
            Project("D", "A", "C"),
            Project("C", "B"),
            Project("B", "A"),
            Project("A"));

        Assert.Equal(3, graph.ComputeNameToDepth()["D"]);
    }

    [Fact]
    public void TopologicalOrder_EveryDependencyComesBeforeItsDependents()
    {
        var projects = new[]
        {
            Project("App", "Service", "Core", "Utils"),
            Project("Service", "Core", "Data"),
            Project("Data", "Core", "Utils"),
            Project("Core", "Utils"),
            Project("Utils"),
            Project("Tool", "Utils"),
        };
        var graph = Graph(projects);

        var order = graph.TopologicalOrder().Select(p => p.Name).ToList();
        var depth = graph.ComputeNameToDepth();

        Assert.Equal(projects.Length, order.Count);
        foreach (var project in projects)
        {
            foreach (var dependency in project.Dependencies)
            {
                Assert.True(order.IndexOf(dependency) < order.IndexOf(project.Name), $"{dependency} should precede {project.Name}");
                Assert.True(depth[dependency] < depth[project.Name], $"{dependency} should be shallower than {project.Name}");
            }
        }
    }

    [Fact]
    public void TopologicalOrder_IsSortedByNameWithinALayer()
    {
        var graph = Graph(
            Project("Zeta"),
            Project("alpha"),
            Project("Mid", "Zeta"),
            Project("Beta", "alpha"));

        Assert.Equal(["alpha", "Zeta", "Beta", "Mid"], graph.TopologicalOrder().Select(p => p.Name));
    }

    [Fact]
    public void DependenciesOutsideTheGraph_AreIgnored()
    {
        var graph = Graph(
            Project("A", "Newtonsoft.Json"),
            Project("B", "A", "Serilog"));

        Assert.Equal(["A", "B"], graph.TopologicalOrder().Select(p => p.Name));
    }

    [Fact]
    public void Cycle_IsReportedInsteadOfDroppingProjects()
    {
        var graph = Graph(
            Project("Root"),
            Project("A", "Root", "B"),
            Project("B", "A"),
            Project("Downstream", "B"));

        var ex = Assert.Throws<InvalidOperationException>(() => graph.TopologicalOrder().ToList());
        Assert.Contains("A", ex.Message);
        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void SelfReference_IsACycle()
    {
        var graph = Graph(Project("A", "A"));

        Assert.Throws<InvalidOperationException>(() => graph.ComputeNameToDepth());
    }

    [Fact]
    public void ConnectedComponents_SplitsIndependentTrees()
    {
        var graph = Graph(
            Project("A"),
            Project("B", "A"),
            Project("X"),
            Project("Y", "X"));

        var components = graph.ConnectedComponents
            .Select(component => component.Select(p => p.Name).Order().ToList())
            .OrderBy(names => names[0])
            .ToList();

        Assert.Equal(2, components.Count);
        Assert.Equal(["A", "B"], components[0]);
        Assert.Equal(["X", "Y"], components[1]);
    }

    [Fact]
    public void RetainIsDependencyOf_KeepsTransitiveDependenciesAndRecomputesDepth()
    {
        var graph = Graph(
            Project("A"),
            Project("B", "A"),
            Project("C", "B"),
            Project("Other", "A"));

        graph.RetainIsDependencyOf(["B"]);

        Assert.Equal(["A", "B"], graph.TopologicalOrder().Select(p => p.Name));
        Assert.Equal(1, graph.ComputeNameToDepth()["B"]);
    }

    [Fact]
    public void RetainDependsOn_KeepsTransitiveDependentsAsNewRoots()
    {
        var graph = Graph(
            Project("A"),
            Project("B", "A"),
            Project("C", "B"),
            Project("Other", "A"));

        graph.RetainDependsOn(["B"]);

        Assert.Equal(["B", "C"], graph.TopologicalOrder().Select(p => p.Name));
        Assert.Equal(0, graph.ComputeNameToDepth()["B"]);
    }
}
