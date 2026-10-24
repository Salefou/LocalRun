namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using Rubjerg.Graphviz;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class GraphVisualizer
{
    private static readonly ILogger logger = Log.For(typeof(GraphVisualizer).FullName!);

    public class Config
    {
        public required DependencyGraph Graph { get; init; }

        /// <summary>Output path without extension: '.dot' and '.svg' files are written next to each other.</summary>
        public required string OutputFilePath { get; init; }
    }

    /// <returns>The path of the generated SVG file.</returns>
    public static async Task<string> GenerateAsync(Config config)
    {
        string outputFilePath = Path.GetFullPath(config.OutputFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
        string dotPath = $"{outputFilePath}.dot";
        string svgPath = $"{outputFilePath}.svg";

        logger.LogInformation("[🧩] Generating dependency graph...");

        string dot = ToDot(config.Graph);
        await File.WriteAllTextAsync(dotPath, dot);
        logger.LogInformation("[📄] DOT file generated: {Path}", dotPath);

        RenderSvg(dotPath, svgPath);
        logger.LogInformation("[🖼️] SVG graph generated: {Path}", svgPath);
        return svgPath;
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
        var nameToDepth = graph.ComputeNameToDepth();

        var projs = graph.TopologicalOrder().ToList();

        List<string> solutions = [.. projs.Select(proj => proj.SolutionPath)
             .Where(solution => solution is not null)
             .Distinct()
             .Cast<string>()];

        //var solutionToProjects = projs.GroupBy(proj => proj.SolutionPath ?? String.Empty)
        //    .ToDictionary(group => group.Key, group => group.ToList());
        var nameToProj = projs.ToDictionary(proj => proj.Name);
        //string dummy = "Dummy";

        foreach (var proj in projs)
        {
            string label = $"{proj.Name}\\n{proj.Version}\\n";
            string color = DepthColor(nameToDepth[proj.Name]);
            sb.AppendLine($"  \"{proj.Name}\" [label=\"{label}\", fillcolor=\"{color}\"];");
            //sb.AppendLine($"  \"{dummy}\" -> \"{proj.Name}\";");

            foreach (var dep in proj.Dependencies)
            {
                sb.AppendLine($"  \"{dep}\" -> \"{proj.Name}\";");
                //if (!nameToProj.TryGetValue(dep, out var projNode))/* && !string.IsNullOrEmpty(projNode.SolutionPath)*/
                //{
                //    sb.AppendLine($"  \"{dep}\" -> \"{dummy}\";");
                //}
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
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

    // Graphviz native binaries ship with the Rubjerg.Graphviz package, no local install needed
    private static void RenderSvg(string dotPath, string svgPath)
    {
        RootGraph root = RootGraph.FromDotFile(dotPath);
        root.ToSvgFile(svgPath);
    }
}
