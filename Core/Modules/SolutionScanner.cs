namespace LocalRun.Core.Modules;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static partial class SolutionScanner
{
    private static readonly Regex ProjectLine = ProjectLineRegex();

    public static Dictionary<string, string> BuildProjectToSolutionMap(string rootPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var slnPath in Directory.GetFiles(rootPath, "*.sln", SearchOption.AllDirectories))
        {
            var slnDir = Path.GetDirectoryName(slnPath)!;
            foreach (var line in File.ReadLines(slnPath))
            {
                var match = ProjectLine.Match(line);
                if (!match.Success) continue;

                var relativeProjPath = match.Groups[1].Value;
                string nameWithExtension = relativeProjPath.Split("\\").Last();
                var projectName = nameWithExtension[..nameWithExtension.IndexOf(".csproj")];

                if (!map.ContainsKey(projectName))
                    map[projectName] = slnPath;
            }
        }

        return map;
    }

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ProjectLineRegex();
}

