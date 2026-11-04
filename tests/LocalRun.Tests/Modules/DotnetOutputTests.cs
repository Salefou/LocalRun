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
