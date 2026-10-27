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
/// browser with elkjs, inlined into the page so it works offline and nothing native is needed.
/// </summary>
public static class GraphPage
{
    private static readonly ILogger logger = Log.For(typeof(GraphPage).FullName!);

    private const string ElkPlaceholder = "{{ELKJS}}";
    private const string DataPlaceholder = "{{GRAPH_DATA}}";

    // Camel case for the page's script; the default encoder escapes '<', so the JSON can't close its <script> tag
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <param name="outputFilePath">Output path without extension: '.html' is appended.</param>
    /// <returns>The path of the generated HTML file.</returns>
    public static async Task<string> GenerateAsync(LocalRunConfig config, string outputFilePath)
    {
        string htmlPath = $"{Path.GetFullPath(outputFilePath)}.html";
        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
        await File.WriteAllTextAsync(htmlPath, ToHtml(config));
        // As a file:// URI, which terminals (Windows Terminal, VS Code, ...) turn into a link: Ctrl+click opens the page
        logger.LogInformation("[🖼️] Graph generated: {Uri}", new Uri(htmlPath).AbsoluteUri);
        return htmlPath;
    }

    internal static string ToHtml(LocalRunConfig config)
    {
        // elkjs first: the data, filled in last, can't then be mistaken for a placeholder
        return ReadResource("GraphPage.html")
            .Replace(ElkPlaceholder, ReadResource("elk.bundled.js"))
            .Replace(DataPlaceholder, JsonSerializer.Serialize(ToData(config), JsonOptions));
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(GraphPage).Assembly.GetManifestResourceStream($"LocalRun.{name}")
            ?? throw new InvalidOperationException($"{name} is missing from the assembly resources");
        return new StreamReader(stream).ReadToEnd();
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
