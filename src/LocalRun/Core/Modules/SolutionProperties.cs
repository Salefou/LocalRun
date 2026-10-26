namespace LocalRun.Core.Modules;

/// <summary>
/// The <c>Solution*</c> properties MSBuild defines when it builds a solution (Visual Studio, <c>dotnet build x.sln</c>).
/// LocalRun builds each csproj on its own, where they are undefined: an <c>OutputPath</c> of
/// <c>$(SolutionDir)\bin\App</c> would then become <c>\bin\App</c>, at the root of the drive.
/// </summary>
public static class SolutionProperties
{
    /// <summary>The <c>-p:</c> arguments defining them for a dotnet command, with a leading space; empty without a solution.</summary>
    public static string Arguments(string? solutionPath)
    {
        if (string.IsNullOrEmpty(solutionPath))
            return string.Empty;
        string fullPath = Path.GetFullPath(solutionPath);
        // With its trailing separator, as Visual Studio defines it, doubled so it doesn't escape the closing quote
        string dir = Path.GetDirectoryName(fullPath)! + Path.DirectorySeparatorChar;
        string escapedDir = dir.EndsWith('\\') ? dir + '\\' : dir;
        return $" -p:SolutionDir=\"{escapedDir}\" -p:SolutionPath=\"{fullPath}\""
            + $" -p:SolutionName=\"{Path.GetFileNameWithoutExtension(fullPath)}\""
            + $" -p:SolutionFileName=\"{Path.GetFileName(fullPath)}\" -p:SolutionExt=\"{Path.GetExtension(fullPath)}\"";
    }
}
