namespace LocalRun.Core.Modules;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public static partial class SolutionScanner
{
    private static readonly Regex ProjectLine = ProjectLineRegex();

    /// <summary>
    /// Maps each project name to the first solution referencing it, reading both the classic .sln format
    /// and the XML .slnx format (the default of <c>dotnet new sln</c> since the .NET 10 SDK).
    /// </summary>
    public static Dictionary<string, string> BuildProjectToSolutionMap(string rootPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var solutions = Directory.EnumerateFiles(rootPath, "*.sln*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var solutionPath in solutions)
        {
            var projectPaths = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                ? ReadSlnxProjects(solutionPath)
                : ReadSlnProjects(solutionPath);
            foreach (var relativeProjPath in projectPaths)
            {
                string projectName = Path.GetFileNameWithoutExtension(relativeProjPath);
                map.TryAdd(projectName, solutionPath);
            }
        }

        return map;
    }

    private static IEnumerable<string> ReadSlnProjects(string slnPath)
    {
        foreach (var line in File.ReadLines(slnPath))
        {
            var match = ProjectLine.Match(line);
            if (match.Success)
                yield return match.Groups[1].Value;
        }
    }

    // <Solution><Project Path="src/A/A.csproj" /><Folder Name="/tests/"><Project Path="..." /></Folder></Solution>
    private static IEnumerable<string> ReadSlnxProjects(string slnxPath)
    {
        return XDocument.Load(slnxPath).Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value)
            .Where(path => path != null && IsProjectFile(path))
            .Cast<string>();
    }

    private static bool IsProjectFile(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.(csproj|esproj))""", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ProjectLineRegex();
}
