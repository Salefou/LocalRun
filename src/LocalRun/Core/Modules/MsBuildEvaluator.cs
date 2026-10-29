namespace LocalRun.Core.Modules;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

/// <summary>What MSBuild makes of a project: its version and package id, after every props file and <c>$(…)</c>.</summary>
public sealed record EvaluatedProject(string? Version, string? PackageId);

/// <summary>
/// Asks MSBuild (<c>dotnet msbuild -getProperty</c>, about a second per project) for what the csproj XML alone cannot
/// tell: values set in a <c>Directory.Build.props</c>, an imported file, or built from <c>$(…)</c>.
/// Only used for the projects that need it (<see cref="NeedsEvaluation"/>); <c>build --csproj</c> never calls it.
/// </summary>
public static class MsBuildEvaluator
{
    private static readonly ILogger logger = Log.For(typeof(MsBuildEvaluator).FullName!);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Properties the graph reads from a project; an imported file setting any of them changes the result.</summary>
    private static readonly HashSet<string> IdentityProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Version", "VersionPrefix", "VersionSuffix", "PackageId", "AssemblyName",
    };

    /// <summary>
    /// Whether the csproj XML alone cannot give the project's version and package id: they hold a <c>$(…)</c>, the project
    /// imports other files, or a <c>Directory.Build.props/targets</c> above it sets them or imports other files.
    /// </summary>
    /// <param name="folderCache">Whether the files of a folder (and above) matter, to read each only once; share it across projects.</param>
    public static bool NeedsEvaluation(XDocument doc, string csproj, Dictionary<string, bool> folderCache)
    {
        var ns = doc.Root!.Name.Namespace;
        if (doc.Root.Elements(ns + "Import").Any())
            return true;
        if (doc.Root.Elements(ns + "PropertyGroup").Elements()
            .Any(elem => IdentityProperties.Contains(elem.Name.LocalName) && elem.Value.Contains("$(")))
            return true;
        return ImportedFilesMatter(Path.GetDirectoryName(Path.GetFullPath(csproj))!, folderCache);
    }

    private static bool ImportedFilesMatter(string? folder, Dictionary<string, bool> cache)
    {
        if (folder == null)
            return false;
        if (cache.TryGetValue(folder, out bool known))
            return known;

        bool matters = ImportedFilesMatter(Path.GetDirectoryName(folder), cache)
            || new[] { "Directory.Build.props", "Directory.Build.targets" }
                .Select(file => Path.Combine(folder, file))
                .Where(File.Exists)
                .Any(DefinesIdentityOrImports);
        cache[folder] = matters;
        return matters;
    }

    private static bool DefinesIdentityOrImports(string file)
    {
        try
        {
            return XDocument.Load(file).Descendants()
                .Any(elem => elem.Name.LocalName == "Import" || IdentityProperties.Contains(elem.Name.LocalName));
        }
        catch (Exception ex)
        {
            // Unreadable here, so unknown: let MSBuild decide
            logger.LogDebug("Could not read {File}: {Error}", file, ex.Message);
            return true;
        }
    }

    /// <returns>Null when MSBuild could not evaluate the project (no SDK, a failure, a timeout).</returns>
    public static EvaluatedProject? Evaluate(string csproj)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "msbuild", csproj, "-getProperty:Version,PackageId", "-nologo" })
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                logger.LogDebug("MSBuild took more than {Timeout}s on {Csproj}", Timeout.TotalSeconds, csproj);
                return null;
            }
            if (process.ExitCode != 0)
            {
                logger.LogDebug("MSBuild failed on {Csproj}: {Output}", csproj, stdout.Result + stderr.Result);
                return null;
            }
            return Parse(stdout.Result);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not run MSBuild on {Csproj}", csproj);
            return null;
        }
    }

    /// <summary>The JSON <c>-getProperty</c> prints for several properties; null when it is not that.</summary>
    internal static EvaluatedProject? Parse(string output)
    {
        int start = output.IndexOf('{');
        if (start < 0)
            return null;
        try
        {
            using var json = JsonDocument.Parse(output[start..]);
            var properties = json.RootElement.GetProperty("Properties");
            string? Value(string name) => properties.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
                ? text.Trim()
                : null;
            return new EvaluatedProject(Value("Version"), Value("PackageId"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
