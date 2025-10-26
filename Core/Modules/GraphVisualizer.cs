namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class GraphVisualizer
{
    /// <summary>
    /// Generates both .dot and .svg dependency graph visualizations.
    /// </summary>
    public static async Task GenerateAsync(DependencyGraph graph, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string dotPath = Path.Combine(outputDirectory, "dependencies.dot");
        string svgPath = Path.Combine(outputDirectory, "dependencies.svg");

        Console.WriteLine("[🧩] Generating dependency graph...");

        // Write .dot file
        string dot = ToDot(graph);
        await File.WriteAllTextAsync(dotPath, dot);
        Console.WriteLine($"[📄] DOT file generated: {dotPath}");

        // Try generating SVG
        try
        {
            await RunDotAsync(dotPath, svgPath);
            Console.WriteLine($"[🖼️] SVG graph generated: {svgPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[⚠️] Could not generate SVG: {ex.Message}");
            Console.WriteLine("      Did you install Graphviz (https://graphviz.org/download/) ?");
        }
    }

    private static string ToDot(DependencyGraph graph)
    {
        //var sb = new StringBuilder();
        //sb.AppendLine("digraph Dependencies {");
        //sb.AppendLine("  rankdir=LR;");
        //sb.AppendLine("  node [shape=box, style=filled, fontname=\"Consolas\", fontsize=10];");
        //sb.AppendLine("  edge [color=gray50];");

        var sb = new StringBuilder();
        sb.AppendLine("digraph Dependencies {");
        sb.AppendLine("  rankdir=LR;");
        //sb.AppendLine("  bgcolor=\"#1e1e1e\";");
        sb.AppendLine("  node [shape=box, style=\"filled, rounded\", color=\"#333333\", fontname=\"Consolas\", fontcolor=\"white\", fontsize=10];");
        sb.AppendLine("  edge [color=\"#999999\", arrowsize=0.7];");
        //sb.AppendLine("}");

        //var nodes = graph.AllProjects.ToDictionary(p => p.Name, p => p);

        // Compute depth for each node (max distance from a root)
        var depths = ComputeDepths(graph);

        var projs = graph.TopologicalOrder().ToList();

        List<string> solutions = projs.Select(proj => proj.SolutionPath)
             .Where(solution => solution is not null)
             .Cast<string>()
             .ToList();

        var solutionToProjects = projs.GroupBy(proj => proj.SolutionPath ?? String.Empty)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var proj in projs)
        {
            //depths[proj.Name];
            string label = $"{proj.Name}\\n{proj.Version}\\n";
            string color = DepthColor(proj.SolutionPath == null ? 0 : solutions.IndexOf(proj.SolutionPath!));
            sb.AppendLine($"  \"{proj.Name}\" [label=\"{label}\", fillcolor=\"{color}\"];");

            foreach (var dep in proj.Dependencies)
            {
                sb.AppendLine($"  \"{dep}\" -> \"{proj.Name}\";");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Compute depth for each node: root = 0, children = root + 1, etc.
    private static Dictionary<string, int> ComputeDepths(DependencyGraph graph)
    {
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>();

        void Visit(string name, int depth)
        {
            if (depths.TryGetValue(name, out var existing))
            {
                if (depth <= existing) return; // already visited with deeper or same depth
                depths[name] = depth;
            }
            else
            {
                depths[name] = depth;
            }
            if (graph.TryGetFromName(name, out var node))
            {
                foreach (var dep in node?.Dependencies ?? [])
                    Visit(dep, depth + 1);
            }
        }

        foreach (var root in graph.Roots)
            Visit(root.Name, 0);

        return depths;
    }

    // Compute color based on depth (HSV -> RGB)
    private static string DepthColor(int depth)
    {
        // Base hue for libraries: 210° blue, executables: 120° green
        int hue = 210;
        double saturation = 0.6;
        double value = Math.Max(0.3, 0.9 - depth * 0.1); // darken with depth
        return HsvToHex(hue, saturation, value);
    }

    // Convert HSV to hex string for Graphviz
    private static string HsvToHex(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        int R = (int)((r + m) * 255);
        int G = (int)((g + m) * 255);
        int B = (int)((b + m) * 255);
        return $"#{R:X2}{G:X2}{B:X2}";
    }

    private static async Task RunDotAsync(string dotPath, string svgPath)
    {
        var dotExePath = Path.Combine("C:", "Program Files", "Graphviz", "bin", "dot.exe");
        var psi = new ProcessStartInfo(dotExePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            Arguments = $"-Tsvg \"{dotPath}\" -o \"{svgPath}\"",
        };

        var proc = Process.Start(psi)!;
        string? error = await proc.StandardError.ReadToEndAsync();
        string? output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Graphviz failed: {error}");
    }
}
