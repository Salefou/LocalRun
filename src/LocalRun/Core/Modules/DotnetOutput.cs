namespace LocalRun.Core.Modules;

using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>An error line of a <c>dotnet</c> command, as data: what to fix and where.</summary>
/// <param name="Code">The error code (<c>CS1002</c>, <c>NU1101</c>, <c>MSB4019</c>), or null when the line has none.</param>
/// <param name="File">The file as MSBuild printed it, or null when the origin is a tool (<c>MSBUILD</c>, <c>CSC</c>) or absent.</param>
/// <param name="Line">The line in <paramref name="File"/>, or null.</param>
/// <param name="Column">The column in <paramref name="File"/>, or null.</param>
/// <param name="Project">The project being built when it failed, from the trailing <c>[….csproj]</c> without its target
/// framework; may be a project the failing one references. Null when the line doesn't say.</param>
public sealed record BuildError(string? Code, string Message, string? File, int? Line, int? Column, string? Project);

/// <summary>A failed <c>dotnet</c> command of <c>pack</c>, <c>go</c> or <c>up</c>, in their results.</summary>
/// <param name="Step">The failing command: <c>clean</c>, <c>restore</c> or <c>pack</c> (<c>pack</c>, <c>go</c>), <c>build</c> (<c>up</c>).</param>
/// <param name="Output">Its error lines, or its last lines when it printed none (at most <see cref="OutputLines"/>).</param>
/// <param name="Errors">Its error lines in MSBuild's format, as data, each once (at most <see cref="MaxErrors"/>).</param>
/// <param name="ErrorCount">How many distinct errors there were, before that cap.</param>
public sealed record DotnetFailure(string Step, int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<BuildError> Errors, int ErrorCount)
{
    /// <summary>Lines of a failing command kept in <see cref="Output"/>.</summary>
    internal const int OutputLines = 20;

    /// <summary>Parsed errors kept in <see cref="Errors"/>: enough to fix a build, few enough to read.</summary>
    internal const int MaxErrors = 50;

    /// <param name="output">Every line the command printed, in order.</param>
    public static DotnetFailure Of(string step, int exitCode, IReadOnlyCollection<string> output)
    {
        var errorLines = output.Where(line => DotnetOutput.DiagnosticLevel(line) == LogLevel.Error).ToList();
        var parsed = DotnetOutput.Errors(errorLines);
        return new(step, exitCode, [.. (errorLines.Count == 0 ? output : errorLines).TakeLast(OutputLines)],
            [.. parsed.Take(MaxErrors)], parsed.Count);
    }
}

/// <summary>What `go` keeps of a `dotnet` command's output: its warnings and errors, verbatim.</summary>
internal static partial class DotnetOutput
{
    // MSBuild's canonical format, "origin : [subcategory] category [code] : text", e.g.
    // "C:\a\B.cs(3,5): warning CS0168: ...", "C:\a\B.csproj : warning NU1603: ...", "MSBUILD : error MSB1009: ..."
    [GeneratedRegex(@"(?:^|[\s:])(?<level>error|warning)(?:\s+[A-Za-z]+\d+)?\s*:\s")]
    private static partial Regex Diagnostic();

    // The same format, taken apart: origin (a file, maybe with "(line,col)" or "(line)" or a range), code, message,
    // and the "[project]" MSBuild appends, possibly with "::TargetFramework=net8.0"
    [GeneratedRegex(@"^\s*(?:(?<origin>.+?)(?:\((?<line>\d+)(?:,(?<column>\d+))?(?:[,-]\d+)*\))?\s*:\s*)?error(?:\s+(?<code>[A-Za-z]+\d+))?\s*:\s*" +
        @"(?<message>.*?)(?:\s+\[(?<project>[^\[\]]+?\.\w*proj)(?:::[^\]]*)?\])?\s*$")]
    private static partial Regex Error();

    /// <summary>The level of a warning or error line, or null for anything else (progress, "Build succeeded", ...).</summary>
    public static LogLevel? DiagnosticLevel(string line)
    {
        var match = Diagnostic().Match(line);
        if (!match.Success) return null;
        return match.Groups["level"].Value == "error" ? LogLevel.Error : LogLevel.Warning;
    }

    /// <summary>An error line taken apart, or null when it isn't in MSBuild's format.</summary>
    public static BuildError? ParseError(string line)
    {
        var match = Error().Match(line);
        if (!match.Success) return null;
        string? origin = match.Groups["origin"].Success ? match.Groups["origin"].Value.Trim() : null;
        // A tool name (MSBUILD, CSC, EXEC) has no path separator nor extension
        string? file = origin != null && (origin.Contains('\\') || origin.Contains('/') || origin.Contains('.')) ? origin : null;
        return new BuildError(
            match.Groups["code"].Success ? match.Groups["code"].Value : null,
            match.Groups["message"].Value,
            file,
            file != null && match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value) : null,
            file != null && match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value) : null,
            match.Groups["project"].Success ? match.Groups["project"].Value : null);
    }

    /// <summary>
    /// The errors of <paramref name="lines"/>, each once: MSBuild repeats them in its summary, and a multi-targeted project
    /// reports one per target framework (the same fix). The same code at another place, or in another project, stays.
    /// </summary>
    public static List<BuildError> Errors(IEnumerable<string> lines) =>
        [.. lines.Select(ParseError).OfType<BuildError>().Distinct()];
}
