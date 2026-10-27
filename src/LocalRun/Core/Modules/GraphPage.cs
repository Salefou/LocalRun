namespace LocalRun.Core.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Model;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Writes the dependency graph as a single interactive HTML page: projects framed by solution (or top folder),
/// groups collapsible into one node, hover to see a project's users-of / deps-of cones. The layout runs in the
/// browser (elkjs), so nothing native is needed.
/// </summary>
public static class GraphPage
{
    private static readonly ILogger logger = Log.For(typeof(GraphPage).FullName!);

    private const string Placeholder = "{{GRAPH_DATA}}";

    // Camel case for the page's script; the default encoder escapes '<', so the JSON can't close its <script> tag
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <param name="outputFilePath">Output path without extension: '.html' is appended.</param>
    /// <returns>The path of the generated HTML file.</returns>
    public static async Task<string> GenerateAsync(LocalRunConfig config, string outputFilePath)
    {
        string htmlPath = $"{Path.GetFullPath(outputFilePath)}.html";
        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
        await File.WriteAllTextAsync(htmlPath, ToHtml(config));
        logger.LogInformation("[🖼️] Interactive graph generated: {Uri}", new Uri(htmlPath).AbsoluteUri);
        return htmlPath;
    }

    internal static string ToHtml(LocalRunConfig config)
    {
        using var template = typeof(GraphPage).Assembly.GetManifestResourceStream($"{typeof(GraphPage).Namespace}.GraphPage.html")
            ?? throw new InvalidOperationException("GraphPage.html is missing from the assembly resources");
        string html = new StreamReader(template).ReadToEnd();
        return html.Replace(Placeholder, JsonSerializer.Serialize(ToData(config), JsonOptions));
    }

    internal sealed record PageData(string RootPath, PageProject[] Projects);

    internal sealed record PageProject(
        string Name, string Type, string Version, string? Latest, string Path, string? Solution, string Folder,
        string[] Dependencies, int Depth);

    internal static PageData ToData(LocalRunConfig config)
    {
        var graph = DependencyGraph.FromConfig(config);
        var nameToDepth = graph.ComputeNameToDepth();
        var projects = graph.TopologicalOrder()
            .Select(proj => new PageProject(
                proj.Name,
                proj.Type,
                proj.Version.ToString(),
                proj.LatestVersion?.ToString(),
                ForwardSlashes(proj.Path),
                proj.SolutionPath is { } solution ? ForwardSlashes(Path.GetRelativePath(config.RootPath, solution)) : null,
                TopFolder(proj.Path),
                [.. proj.Dependencies],
                nameToDepth[proj.Name]))
            .ToArray();
        return new PageData(config.RootPath, projects);
    }

    // The first folder under the root: the repository, when the root holds one folder per repository
    private static string TopFolder(string projectPath)
    {
        var parts = ForwardSlashes(projectPath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[0] : "(root)";
    }

    private static string ForwardSlashes(string path) => path.Replace('\\', '/');
}
