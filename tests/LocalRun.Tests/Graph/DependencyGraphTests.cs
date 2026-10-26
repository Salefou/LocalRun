namespace LocalRun.Tests.Graph;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using System.Xml.Linq;

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
    public void RemoveCycles_LeavesTheRestOrderable()
    {
        var graph = Graph(
            Project("Root"),
            Project("A", "Root", "B"),
            Project("B", "C"),
            Project("C", "A"),
            Project("Downstream", "B", "Root"));

        var cycle = Assert.Single(graph.RemoveCycles());

        Assert.Equal(["A", "B", "C"], cycle.Projects);
        Assert.Equal(["Downstream"], cycle.Dependents);
        Assert.Equal(["Root", "Downstream"], graph.TopologicalOrder().Select(p => p.Name));
        Assert.Equal(1, graph.ComputeNameToDepth()["Downstream"]);
    }

    [Fact]
    public void RemoveCycles_ReportsEachCycleAndSelfReferenceSeparately()
    {
        var graph = Graph(
            Project("A", "B"),
            Project("B", "A"),
            Project("Self", "Self"),
            Project("X", "Y"),
            Project("Y", "X"),
            Project("Free"));

        var cycles = graph.RemoveCycles();

        Assert.Equal([["A", "B"], ["Self"], ["X", "Y"]], cycles.Select(c => c.Projects));
        Assert.Equal(["Free"], graph.TopologicalOrder().Select(p => p.Name));
    }

    [Fact]
    public void RemoveCycles_WithoutCycles_ChangesNothing()
    {
        var graph = Graph(Project("A"), Project("B", "A"), Project("C", "A", "B"));

        Assert.Empty(graph.RemoveCycles());
        Assert.Equal(["A", "B", "C"], graph.TopologicalOrder().Select(p => p.Name));
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

    // Base <- X <- Mid <- Y, Base <- Other <- Y, X <- Side:
    // Other and Base are dependencies of Y that aren't users of X; Side is a user of X that Y doesn't need
    private static DependencyGraph FilterGraph() => Graph(
        Project("Base"),
        Project("X", "Base"),
        Project("Mid", "X"),
        Project("Other", "Base"),
        Project("Y", "Mid", "Other"),
        Project("Side", "X"));

    private static IEnumerable<string> Names(DependencyGraph graph) => graph.AllProjects.Select(p => p.Name).Order();

    [Fact]
    public void Retain_BothFlags_KeepsOnlyTheProjectsBetweenThem()
    {
        var graph = FilterGraph();

        graph.Retain(usersOf: ["X"], depsOf: ["Y"], union: false);

        Assert.Equal(["Mid", "X", "Y"], Names(graph));
    }

    [Fact]
    public void Retain_BothFlagsWithUnion_KeepsBothCones()
    {
        var graph = FilterGraph();

        graph.Retain(usersOf: ["X"], depsOf: ["Y"], union: true);

        Assert.Equal(["Base", "Mid", "Other", "Side", "X", "Y"], Names(graph));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retain_OneFlag_KeepsItsConeInBothModes(bool union)
    {
        var users = FilterGraph();
        var dependencies = FilterGraph();

        users.Retain(usersOf: ["X"], depsOf: null, union);
        dependencies.Retain(usersOf: null, depsOf: ["Y"], union);

        Assert.Equal(["Mid", "Side", "X", "Y"], Names(users));
        Assert.Equal(["Base", "Mid", "Other", "X", "Y"], Names(dependencies));
    }

    [Fact]
    public void Retain_NamesWithinAFlag_AreOred()
    {
        var graph = FilterGraph();

        graph.Retain(usersOf: ["X", "Other"], depsOf: null, union: false);

        Assert.Equal(["Mid", "Other", "Side", "X", "Y"], Names(graph));
    }

    [Fact]
    public void Retain_DisjointFlags_LeaveNothing()
    {
        var graph = FilterGraph();

        graph.Retain(usersOf: ["Side"], depsOf: ["Y"], union: false);

        Assert.Empty(graph.AllProjects);
    }

    [Fact]
    public void FindUnknown_IgnoresCase()
    {
        Assert.Equal(["Nope"], FilterGraph().FindUnknown(["x", "Nope", "BASE"]));
    }

    [Fact]
    public void SameName_AllAreLeftOut_DependentsKeptWithoutTheEdge()
    {
        // Two "Shared" (one spelt "shared"), Consumer uses the name, Base is unrelated
        var graph = Graph(
            Project("Base"),
            Project("Shared"),
            new ProjectNode
            {
                Name = "shared",
                Path = "repo-two/shared/shared.csproj",
                Type = "library",
                Version = new ProjectVersion { Major = 2, Minor = 0, PatchAndSuffix = "0" },
                Dependencies = [],
            },
            Project("Consumer", "Shared", "Base"));

        var collision = Assert.Single(graph.Collisions);
        Assert.Equal("Shared", collision.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["repo-two/shared/shared.csproj", "Shared/Shared.csproj"], collision.Paths);
        Assert.Equal(["Consumer"], collision.Dependents);

        Assert.Equal(["Base", "Consumer"], graph.TopologicalOrder().Select(p => p.Name));
        Assert.Equal(1, graph.ComputeNameToDepth()["Consumer"]);
        Assert.Equal(["shared"], graph.FindCollided(["shared", "Base", "Nope"]));
    }

    [Fact]
    public void UniqueNames_HaveNoCollision()
    {
        Assert.Empty(FilterGraph().Collisions);
    }

    private static ProjectNode Service(string name, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = "executable",
        Version = new ProjectVersion { Major = 1, Minor = 0, PatchAndSuffix = "0" },
        Dependencies = [.. dependencies],
    };

    // Base <- Lib <- Mid <- Svc (service), Lib <- Api (service), Lib <- DeadEnd (library, no service), Unrelated (service)
    private static DependencyGraph ServicesGraph() => Graph(
        Project("Base"),
        Project("Lib", "Base"),
        Project("Mid", "Lib"),
        Service("Svc", "Mid"),
        Service("Api", "Lib"),
        Project("DeadEnd", "Lib"),
        Service("Unrelated"));

    [Fact]
    public void WithServices_AddsAffectedServicesAndTheLibrariesOnTheWay()
    {
        var graph = ServicesGraph();

        var added = graph.Retain(usersOf: ["Lib"], depsOf: ["Api"], union: false, withServices: true);

        Assert.Equal(["Mid", "Svc"], added);
        Assert.Equal(["Api", "Lib", "Mid", "Svc"], Names(graph));
    }

    [Fact]
    public void WithServices_OnDepsOfAlone_KeepsWhatReachesAService()
    {
        var graph = ServicesGraph();

        var added = graph.Retain(usersOf: null, depsOf: ["Api"], union: false, withServices: true);

        Assert.Equal(["Mid", "Svc"], added);
        Assert.Equal(["Api", "Base", "Lib", "Mid", "Svc"], Names(graph));
    }

    [Fact]
    public void WithServices_WhenTheAffectedServicesAreAlreadyKept_AddsNothing()
    {
        var graph = ServicesGraph();

        var added = graph.Retain(usersOf: ["Mid"], depsOf: ["Api"], union: true, withServices: true);

        Assert.Empty(added);
        Assert.Equal(["Api", "Base", "Lib", "Mid", "Svc"], Names(graph));
    }

    [Fact]
    public void WithoutServices_NothingIsAdded()
    {
        var graph = ServicesGraph();

        var added = graph.Retain(usersOf: ["Lib"], depsOf: ["Api"], union: false);

        Assert.Empty(added);
        Assert.Equal(["Api", "Lib"], Names(graph));
    }

    [Theory]
    [InlineData("<Version>1.4.2</Version>", "1.4.2")]
    [InlineData("<VersionPrefix>2.3.0</VersionPrefix>", "2.3.0")]
    [InlineData("<VersionPrefix>2.3.0</VersionPrefix><VersionSuffix>beta</VersionSuffix>", "2.3.0-beta")]
    [InlineData("<VersionPrefix>2.3.0</VersionPrefix><VersionSuffix>$(BuildNumber)</VersionSuffix>", "2.3.0")]
    [InlineData("<VersionPrefix>2.3.0</VersionPrefix><Version>1.4.2</Version>", "1.4.2")]
    [InlineData("<Version>$(Major).0.0</Version>", "1.0.0")]
    [InlineData("", "1.0.0")]
    public void ReadVersion_FollowsTheSdkDefaults(string properties, string expected)
    {
        var csproj = XDocument.Parse($"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>{properties}</PropertyGroup></Project>""");

        Assert.Equal(expected, DependencyGraph.ReadVersion(csproj, "Lib.csproj").AsVersionString());
    }

    [Fact]
    public void ReadVersion_IgnoresThePackageReferenceVersions()
    {
        var csproj = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Other"><Version>9.9.9</Version></PackageReference></ItemGroup>
              <PropertyGroup><VersionPrefix>2.3.0</VersionPrefix></PropertyGroup>
            </Project>
            """);

        Assert.Equal("2.3.0", DependencyGraph.ReadVersion(csproj, "Lib.csproj").AsVersionString());
    }
}
