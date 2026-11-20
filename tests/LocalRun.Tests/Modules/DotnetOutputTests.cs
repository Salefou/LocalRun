namespace LocalRun.Tests.Modules;

using LocalRun.Core.Modules;
using Microsoft.Extensions.Logging;

public class DotnetOutputTests
{
    [Theory]
    [InlineData(@"C:\repos\Lib\A.cs(3,5): warning CS0168: The variable 'x' is declared but never used [C:\repos\Lib\Lib.csproj::TargetFramework=net8.0]")]
    [InlineData(@"C:\repos\Lib\Lib.csproj : warning NU1603: Lib depends on Foo (>= 1.0.0) but Foo 1.0.0 was not found.")]
    [InlineData("  warning : something without a code")]
    public void Warnings_AreKept(string line)
    {
        Assert.Equal(LogLevel.Warning, DotnetOutput.DiagnosticLevel(line));
    }

    [Theory]
    [InlineData(@"C:\repos\Lib\A.cs(3,5): error CS1002: ; expected [C:\repos\Lib\Lib.csproj]")]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.")]
    [InlineData(@"C:\Program Files\dotnet\sdk\8.0.100\Microsoft.Common.targets(10,5): error : Something failed")]
    [InlineData("error NETSDK1004: Assets file 'project.assets.json' not found.")]
    public void Errors_AreKept(string line)
    {
        Assert.Equal(LogLevel.Error, DotnetOutput.DiagnosticLevel(line));
    }

    [Theory]
    [InlineData(@"C:\repos\Lib\A.cs(3,5): error CS1002: ; expected [C:\repos\Lib\Lib.csproj]",
        "CS1002", "; expected", @"C:\repos\Lib\A.cs", 3, 5, @"C:\repos\Lib\Lib.csproj")]
    [InlineData(@"  C:\repos\Lib\A.cs(12,9): error CS0246: The type or namespace name 'Foo' could not be found [C:\repos\Lib\Lib.csproj::TargetFramework=net8.0]",
        "CS0246", "The type or namespace name 'Foo' could not be found", @"C:\repos\Lib\A.cs", 12, 9, @"C:\repos\Lib\Lib.csproj")]
    [InlineData(@"C:\Program Files\dotnet\sdk\8.0.100\Microsoft.Common.targets(10): error MSB4019: Not found. [C:\repos\My App\App.csproj]",
        "MSB4019", "Not found.", @"C:\Program Files\dotnet\sdk\8.0.100\Microsoft.Common.targets", 10, null, @"C:\repos\My App\App.csproj")]
    [InlineData(@"C:\repos\Lib\A.cs(3,5,3,9): error CS1002: ; expected", "CS1002", "; expected", @"C:\repos\Lib\A.cs", 3, 5, null)]
    [InlineData(@"/home/u/repos/Lib/Lib.csproj : error NU1101: Unable to find package Foo. [/home/u/repos/Lib/Lib.csproj]",
        "NU1101", "Unable to find package Foo.", "/home/u/repos/Lib/Lib.csproj", null, null, "/home/u/repos/Lib/Lib.csproj")]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.", "MSB1009", "Project file does not exist.", null, null, null, null)]
    [InlineData("CSC : error CS2001: Source file 'x.cs' could not be found.", "CS2001", "Source file 'x.cs' could not be found.", null, null, null, null)]
    [InlineData("error NETSDK1004: Assets file 'project.assets.json' not found.", "NETSDK1004", "Assets file 'project.assets.json' not found.", null, null, null, null)]
    [InlineData(@"C:\a\B.targets(10,5): error : Something failed [C:\a\B.csproj]", null, "Something failed", @"C:\a\B.targets", 10, 5, @"C:\a\B.csproj")]
    public void AnErrorLine_IsTakenApart(string line, string? code, string message, string? file, int? lineNumber, int? column, string? project)
    {
        Assert.Equal(new BuildError(code, message, file, lineNumber, column, project), DotnetOutput.ParseError(line));
    }

    [Theory]
    [InlineData(@"C:\repos\Lib\A.cs(3,5): warning CS0168: The variable 'x' is declared but never used")]
    [InlineData("  Build FAILED.")]
    [InlineData("    1 Error(s)")]
    public void OtherLines_AreNoErrors(string line)
    {
        Assert.Null(DotnetOutput.ParseError(line));
    }

    [Fact]
    public void Errors_AreKeptOnce_PerPlaceAndProject()
    {
        string[] lines =
        [
            @"C:\r\Lib\A.cs(3,5): error CS0246: 'Foo' not found [C:\r\Lib\Lib.csproj::TargetFramework=net8.0]",
            @"C:\r\Lib\A.cs(3,5): error CS0246: 'Foo' not found [C:\r\Lib\Lib.csproj::TargetFramework=net48]", // another framework
            @"C:\r\Lib\A.cs(9,1): error CS0246: 'Foo' not found [C:\r\Lib\Lib.csproj::TargetFramework=net8.0]", // another place
            @"C:\r\Lib\A.cs(3,5): error CS0246: 'Foo' not found [C:\r\Other\Other.csproj]",                     // another project
            @"C:\r\Lib\A.cs(3,5): error CS0246: 'Foo' not found [C:\r\Lib\Lib.csproj::TargetFramework=net8.0]", // the summary
        ];

        var errors = DotnetOutput.Errors(lines);

        Assert.Equal([(3, @"C:\r\Lib\Lib.csproj"), (9, @"C:\r\Lib\Lib.csproj"), (3, @"C:\r\Other\Other.csproj")],
            errors.Select(error => (error.Line!.Value, error.Project!)));
    }

    [Theory]
    [InlineData("  Determining projects to restore...")]
    [InlineData(@"  Restored C:\repos\Lib\Lib.csproj (in 120 ms).")]
    [InlineData(@"  Lib -> C:\repos\Lib\bin\Debug\net8.0\Lib.dll")]
    [InlineData(@"  Successfully created package 'C:\feed\Lib.1.0.1-local.1.nupkg'.")]
    [InlineData("    0 Warning(s)")]
    [InlineData("    0 Error(s)")]
    [InlineData("Build succeeded.")]
    public void EverythingElse_IsDropped(string line)
    {
        Assert.Null(DotnetOutput.DiagnosticLevel(line));
    }
}
