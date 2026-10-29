namespace LocalRun.Tests.Modules;

using LocalRun.Core.Graph;
using LocalRun.Core.Modules;
using System.Xml.Linq;

public class MsBuildEvaluatorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("localrun-msbuild-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Csproj(string folder, string name, string body = "")
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{name}.csproj");
        File.WriteAllText(path, $"""<Project Sdk="Microsoft.NET.Sdk">{body}</Project>""");
        return path;
    }

    private void Props(string folder, string file, string body)
    {
        Directory.CreateDirectory(Path.Combine(root, folder));
        File.WriteAllText(Path.Combine(root, folder, file), $"<Project>{body}</Project>");
    }

    private static bool Needs(string csproj) =>
        MsBuildEvaluator.NeedsEvaluation(XDocument.Load(csproj), csproj, []);

    [Fact]
    public void AProjectTellingEverythingItself_NeedsNothing()
    {
        Assert.False(Needs(Csproj("Lib", "Lib", "<PropertyGroup><Version>1.2.3</Version></PropertyGroup>")));
    }

    [Theory]
    [InlineData("<PropertyGroup><Version>$(Major).0.0</Version></PropertyGroup>")]
    [InlineData("<PropertyGroup><PackageId>$(Prefix).Lib</PackageId></PropertyGroup>")]
    [InlineData("""<Import Project="..\common.props" />""")]
    public void AUnresolvedValueOrAnImport_NeedsMsBuild(string body)
    {
        Assert.True(Needs(Csproj("Lib", "Lib", body)));
    }

    [Theory]
    [InlineData("Directory.Build.props", "<PropertyGroup><VersionPrefix>9.0.0</VersionPrefix></PropertyGroup>", true)]
    [InlineData("Directory.Build.targets", "<PropertyGroup><AssemblyName>Acme.$(MSBuildProjectName)</AssemblyName></PropertyGroup>", true)]
    [InlineData("Directory.Build.props", """<Import Project="common.props" />""", true)]
    [InlineData("Directory.Build.props", "<PropertyGroup><LangVersion>latest</LangVersion></PropertyGroup>", false)]
    public void ADirectoryBuildFileAbove_MattersOnlyWhenItSetsIdentityOrImports(string file, string body, bool expected)
    {
        Props("", file, body);

        Assert.Equal(expected, Needs(Csproj(Path.Combine("src", "Lib"), "Lib")));
    }

    [Fact]
    public void AnUnreadableDirectoryBuildFile_LeavesTheDecisionToMsBuild()
    {
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "not xml");

        Assert.True(Needs(Csproj("Lib", "Lib")));
    }

    [Fact]
    public void Parse_ReadsTheJsonOfSeveralProperties()
    {
        var parsed = MsBuildEvaluator.Parse("""
            {
              "Properties": {
                "Version": "10.0.0-preview.1",
                "PackageId": "Acme.Lib"
              }
            }
            """);

        Assert.Equal(new EvaluatedProject("10.0.0-preview.1", "Acme.Lib"), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.")]
    [InlineData("""{ "Other": 1 }""")]
    public void Parse_GivesNull_ForAnythingElse(string output)
    {
        Assert.Null(MsBuildEvaluator.Parse(output));
    }

    [Fact]
    public void FromSource_UsesWhatMsBuildSays_OnlyForTheProjectsThatNeedIt()
    {
        Csproj("Plain", "Plain", "<PropertyGroup><Version>1.2.3</Version></PropertyGroup>");
        Csproj("Shared", "Shared", "<PropertyGroup><PackageId>$(Prefix).Shared</PackageId></PropertyGroup>");
        string[] csprojs = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        var asked = new List<string>();

        var graph = DependencyGraph.FromSource(root, csprojs, includeProjectsWithoutSolutions: true, evaluate: csproj =>
        {
            lock (asked) asked.Add(Path.GetFileNameWithoutExtension(csproj));
            return new EvaluatedProject("5.1.0", "Acme.Shared");
        });

        Assert.Equal(["Shared"], asked);
        var shared = graph.AllProjects.Single(p => p.Name == "Shared");
        Assert.Equal("5.1.0", shared.Version.AsVersionString());
        Assert.Equal("Acme.Shared", shared.PackageId);
        Assert.Equal("1.2.3", graph.AllProjects.Single(p => p.Name == "Plain").Version.AsVersionString());
    }

    [Fact]
    public void FromSource_WithoutAnEvaluator_ReadsTheCsprojOnly()
    {
        Csproj("Shared", "Shared", "<PropertyGroup><PackageId>$(Prefix).Shared</PackageId><Version>$(V)</Version></PropertyGroup>");
        string[] csprojs = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);

        var graph = DependencyGraph.FromSource(root, csprojs, includeProjectsWithoutSolutions: true);

        var shared = graph.AllProjects.Single();
        Assert.Null(shared.PackageId);
        Assert.Equal("1.0.0", shared.Version.AsVersionString());
    }

    [Fact]
    public void FromSource_FallsBackToTheCsproj_WhenMsBuildCannotEvaluate()
    {
        Csproj("Shared", "Shared", "<PropertyGroup><Version>$(V)</Version><AssemblyName>Acme.Shared</AssemblyName></PropertyGroup>");
        string[] csprojs = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);

        var graph = DependencyGraph.FromSource(root, csprojs, includeProjectsWithoutSolutions: true, evaluate: _ => null);

        var shared = graph.AllProjects.Single();
        Assert.Equal("Acme.Shared", shared.PackageId);
        Assert.Equal("1.0.0", shared.Version.AsVersionString());
    }

    [Fact]
    public void Evaluate_AsksTheRealMsBuild()
    {
        Props("", "Directory.Build.props", "<PropertyGroup><VersionPrefix>7.8.9</VersionPrefix></PropertyGroup>");
        string csproj = Csproj("Lib", "Lib", "<PropertyGroup><AssemblyName>Acme.Lib</AssemblyName></PropertyGroup>");

        Assert.True(Needs(csproj));
        Assert.Equal(new EvaluatedProject("7.8.9", "Acme.Lib"), MsBuildEvaluator.Evaluate(csproj));
    }
}
