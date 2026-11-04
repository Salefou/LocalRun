namespace LocalRun.Core.Modules;

using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

/// <summary>What `go` keeps of a `dotnet` command's output: its warnings and errors, verbatim.</summary>
internal static partial class DotnetOutput
{
    // MSBuild's canonical format, "origin : [subcategory] category [code] : text", e.g.
    // "C:\a\B.cs(3,5): warning CS0168: ...", "C:\a\B.csproj : warning NU1603: ...", "MSBUILD : error MSB1009: ..."
    [GeneratedRegex(@"(?:^|[\s:])(?<level>error|warning)(?:\s+[A-Za-z]+\d+)?\s*:\s")]
    private static partial Regex Diagnostic();

    /// <summary>The level of a warning or error line, or null for anything else (progress, "Build succeeded", ...).</summary>
    public static LogLevel? DiagnosticLevel(string line)
    {
        var match = Diagnostic().Match(line);
        if (!match.Success) return null;
        return match.Groups["level"].Value == "error" ? LogLevel.Error : LogLevel.Warning;
    }
}
