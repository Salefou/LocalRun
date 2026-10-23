namespace LocalRun.Core.Graph;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using System.Collections.Generic;
using System.Xml.Linq;

public class DependencyGraph
{
    private class Node
    {
        public ProjectNode Project { get; }

        public HashSet<Node> DependsOn { get; } = new();

        public HashSet<Node> Dependents { get; } = new();

        public Node(ProjectNode project) => Project = project;

        public override string ToString()
        {
            return Project.ToString();
        }
    }

    private readonly Dictionary<string, Node> _nodesPerName = new(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<Node> RootsNodes => _nodesPerName.Values.Where(n => n.DependsOn.Count == 0);

    public IEnumerable<ProjectNode> Roots => [.. RootsNodes.Select(n => n.Project)];

    private DependencyGraph(List<ProjectNode> projects)
    {
        foreach (var proj in projects)
        {
            Node projNode = new(proj);
            _nodesPerName[proj.Name] = projNode;
        }

        foreach (var node in _nodesPerName.Values)
        {
            foreach (var depName in node.Project.Dependencies)
            {
                if (_nodesPerName.TryGetValue(depName, out var depNode))
                {
                    node.DependsOn.Add(depNode);
                    depNode.Dependents.Add(node);
                }
            }
        }
    }

    public static DependencyGraph FromSource(string rootPath, string[] csprojs, bool includeProjectsWithoutSolutions)
    {
        var projectToSolution = SolutionScanner.BuildProjectToSolutionMap(rootPath);
        var projects = new List<ProjectNode>();
        foreach (var csproj in csprojs.Order())
        {
            var doc = XDocument.Load(csproj);
            var ns = doc.Root!.Name.Namespace;

            string name = Path.GetFileNameWithoutExtension(csproj);
            string type = doc.Descendants(ns + "OutputType")
                .Select(desc => desc.Value)
                .Any(desc => desc == "Exe") ? "executable" : "library";


            var versions = doc.Descendants(ns + "Version")
                .ToList();
            var versionElem = versions.FirstOrDefault();
            var version = ProjectVersion.Parse(versionElem?.Value);

            if (!projectToSolution.TryGetValue(name, out var solutionPath))
            {
                Console.BackgroundColor = ConsoleColor.DarkYellow;
                Console.Write("[WARN]");
                Console.ResetColor();
                Console.WriteLine($": Did not find any solution referencing project {csproj}");
            }

            var allDependencies = doc.Descendants(ns + "ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value))
                .Concat(doc.Descendants(ns + "PackageReference")
                .Select(e => e.Attribute("Include")!.Value))
                .ToList();
            var deps = allDependencies
                .Where(dep => includeProjectsWithoutSolutions || projectToSolution.TryGetValue(dep, out _))
                .ToList();

            projects.Add(new ProjectNode
            {
                Name = name,
                Path = Path.GetRelativePath(rootPath, csproj),
                SolutionPath = solutionPath,
                Type = type,
                Version = version,
                Dependencies = deps
            });
        }
        return new DependencyGraph(projects);
    }

    public static DependencyGraph FromConfig(LocalRunConfig config)
    {
        return new DependencyGraph(config.Projects);
    }

    public void RetainDependsOn(IEnumerable<string> names)
    {
        var toKeep = new HashSet<Node>();
        foreach (var name in names)
        {
            if (_nodesPerName.TryGetValue(name, out var target))
            {
                CollectDependents(target, toKeep);
            }
        }
        Prune(toKeep);
    }

    public void RetainIsDependencyOf(IEnumerable<string> names)
    {
        var toKeep = new HashSet<Node>();
        foreach (var name in names)
        {
            if (_nodesPerName.TryGetValue(name, out var target))
            {
                CollectDependencies(target, toKeep);
            }
        }
        Prune(toKeep);
    }

    private void Traverse(Node start, Func<Node, IEnumerable<Node>> neighborSelector, HashSet<Node> visited)
    {
        var queue = new Queue<Node>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var neighbor in neighborSelector(current))
            {
                queue.Enqueue(neighbor);
            }
        }
    }

    private void CollectDependencies(Node node, HashSet<Node> visited)
        => Traverse(node, n => n.DependsOn, visited);

    private void CollectDependents(Node node, HashSet<Node> visited)
        => Traverse(node, n => n.Dependents, visited);

    private void Prune(HashSet<Node> keep)
    {
        var all = _nodesPerName.Values.ToList();
        foreach (var node in all)
        {
            if (!keep.Contains(node))
            {
                _nodesPerName.Remove(node.Project.Name);
            }
            else
            {
                node.DependsOn.RemoveWhere(d => !keep.Contains(d));
                node.Dependents.RemoveWhere(d => !keep.Contains(d));
            }
        }
    }

    public Dictionary<string, int> ComputeNameToDepth()
    {
        Dictionary<string, int> nameToDepth = new();
        int depth = 0;
        List<List<Node>> projectsClasses = TopologicalClassOrder();
        foreach (var projectsClass in projectsClasses)
        {
            foreach (var proj in projectsClass)
            {
                nameToDepth[proj.Project.Name] = depth;
            }
            depth++;
        }

        return nameToDepth;
    }

    public IEnumerable<IEnumerable<ProjectNode>> ConnectedComponents => GetConnectedComponents().Select(component => component.Select(node => node.Project));

    private List<HashSet<Node>> GetConnectedComponents()
    {
        var components = new List<HashSet<Node>>();
        var visited = new HashSet<Node>();

        foreach (var node in _nodesPerName.Values)
        {
            if (visited.Contains(node))
                continue;

            var component = new HashSet<Node>();
            var queue = new Queue<Node>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                component.Add(current);

                foreach (var neighbor in current.Dependents.Concat(current.DependsOn))
                {
                    if (!visited.Contains(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// Kahn's algorithm, layered: a node lands in the layer right after its deepest dependency,
    /// so its depth is the longest path from a root and every dependency sits in a strictly lower layer.
    /// Layers are sorted by name to keep the output deterministic.
    /// </summary>
    private List<List<Node>> TopologicalClassOrder()
    {
        var remainingDependencies = _nodesPerName.Values.ToDictionary(node => node, node => node.DependsOn.Count);
        List<List<Node>> topologicalClassOrder = [];

        List<Node> currentClass = [.. RootsNodes];
        while (currentClass.Count != 0)
        {
            currentClass.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Project.Name, b.Project.Name));
            topologicalClassOrder.Add(currentClass);

            List<Node> nextClass = [];
            foreach (var node in currentClass)
            {
                foreach (var dependent in node.Dependents)
                {
                    if (--remainingDependencies[dependent] == 0)
                    {
                        nextClass.Add(dependent);
                    }
                }
            }
            currentClass = nextClass;
        }

        var unordered = remainingDependencies.Where(kv => kv.Value > 0).Select(kv => kv.Key.Project.Name).Order().ToList();
        if (unordered.Count != 0)
        {
            throw new InvalidOperationException($"Dependency cycle detected, cannot order: {string.Join(", ", unordered)}");
        }

        return topologicalClassOrder;
    }

    public IEnumerable<ProjectNode> TopologicalOrder()
    {
        return TopologicalClassOrder().SelectMany(level => level.Select(node => node.Project));
    }

    public IEnumerable<ProjectNode> AllProjects => _nodesPerName.Values.Select(n => n.Project);

    public override string ToString()
    {
        return $"Size={_nodesPerName.Count}, Nodes=[{string.Join(", ", _nodesPerName.Select(_node => _node.Key))}]";
    }

    internal bool TryGetFromName(string name, out ProjectNode? projectNode)
    {
        if (!_nodesPerName.TryGetValue(name, out var node))
        {
            projectNode = null;
            return false;
        }
        projectNode = node.Project;
        return true;
    }
}

