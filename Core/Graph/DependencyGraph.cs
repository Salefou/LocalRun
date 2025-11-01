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
                CollectDependentsIterative(target, toKeep);
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
                CollectDependenciesIterative(target, toKeep);
            }
        }
        Prune(toKeep);
    }

    private void CollectDependenciesIterative(Node node, HashSet<Node> visited)
    {
        var toVisit = new Queue<Node>();

        toVisit.Enqueue(node);
        while (toVisit.Count != 0)
        {
            Node next = toVisit.Dequeue();
            if (visited.Add(next))
            {
                foreach (var neighbour in next.DependsOn)
                {
                    toVisit.Enqueue(neighbour);
                }
            }
        }
    }

    private void CollectDependentsIterative(Node node, HashSet<Node> visited)
    {
        var toVisit = new Queue<Node>();

        toVisit.Enqueue(node);
        while (toVisit.Count != 0)
        {
            Node next = toVisit.Dequeue();
            if (visited.Add(next))
            {
                foreach (var neighbour in next.Dependents)
                {
                    toVisit.Enqueue(neighbour);
                }
            }
        }
    }

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

    public IEnumerable<IEnumerable<IEnumerable<ProjectNode>>> TopologicalClassOrderPerSubtree()
    {
        return RootsNodes.Select(rootNode =>
            TopologicalClassOrder(rootNode).Select(depth =>
                depth.Select(node => node.Project)));
    }


    private List<List<Node>> TopologicalClassOrder(Node subTreeRoot)
    {
        List<List<Node>> topologicalClassOrder = [[]];
        HashSet<Node> visited = new();

        var toVisitThisLevel = new Queue<Node>();
        var toVisitNextLevel = new Queue<Node>();

        toVisitThisLevel.Enqueue(subTreeRoot);
        while (true)
        {
            while (toVisitThisLevel.Count != 0)
            {
                Node next = toVisitThisLevel.Dequeue();
                topologicalClassOrder.Last().Add(next);
                if (visited.Add(next))
                {
                    foreach (var neighbour in next.Dependents)
                    {
                        toVisitNextLevel.Enqueue(neighbour);
                    }
                }
            }
            if (toVisitNextLevel.Count == 0)
            {
                break;
            }
            (toVisitNextLevel, toVisitThisLevel) = (toVisitThisLevel, toVisitNextLevel);
            toVisitNextLevel.Clear();
            topologicalClassOrder.Add([]);
        }

        return topologicalClassOrder;
    }

    public IEnumerable<ProjectNode> TopologicalOrder()
    {
        var visited = new HashSet<Node>();
        var result = new List<Node>();

        void Visit(Node n)
        {
            if (!visited.Add(n)) return;
            foreach (var dep in n.Dependents)
                Visit(dep);
            result.Add(n);
        }

        foreach (var root in _nodesPerName.Values.Where(n => n.DependsOn.Count == 0))
            Visit(root);

        return result.Select(n => n.Project);
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

