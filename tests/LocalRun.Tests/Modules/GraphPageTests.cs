namespace LocalRun.Tests.Modules;

using LocalRun.Core.Model;
using LocalRun.Core.Modules;

public class GraphPageTests
{
    private static readonly string Root = Path.GetFullPath("repos");

    private static ProjectNode Project(string path, string? solution, params string[] dependencies) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path),
        Path = path,
        SolutionPath = solution == null ? null : Path.Combine(Root, solution),
        Type = "library",
        Version = ProjectVersion.Parse("1.0.0"),
        Dependencies = [.. dependencies],
    };

    private static LocalRunConfig Config(params ProjectNode[] projects) => new() { RootPath = Root, Projects = [.. projects] };

    [Fact]
    public void Projects_CarryTheirSolutionRelativeToTheRoot_AndTheirTopFolder()
    {
        var data = GraphPage.ToData(Config(
            Project(Path.Combine("core", "Primitives", "Primitives.csproj"), Path.Combine("core", "Core.sln")),
            Project(Path.Combine("pricing", "tools", "Orphan", "Orphan.csproj"), null, "Primitives"),
            Project("Loose.csproj", null)));

        var primitives = data.Projects.Single(p => p.Name == "Primitives");
        Assert.Equal("core/Core.sln", primitives.Solution);
        Assert.Equal("core", primitives.Folder);
        Assert.Equal("core/Primitives/Primitives.csproj", primitives.Path);

        var orphan = data.Projects.Single(p => p.Name == "Orphan");
        Assert.Null(orphan.Solution);
        Assert.Equal("pricing", orphan.Folder);
        Assert.Equal(1, orphan.Depth);

        Assert.Equal("(root)", data.Projects.Single(p => p.Name == "Loose").Folder);
    }

    [Fact]
    public void Html_EmbedsTheDataInTheTemplate_WithoutClosingItsScriptTag()
    {
        var html = GraphPage.ToHtml(Config(Project(Path.Combine("core", "A", "A.csproj"), null, "</script>")));

        Assert.DoesNotContain("{{GRAPH_DATA}}", html);
        Assert.Contains("\"name\":\"A\"", html);
        Assert.Contains("\\u003C/script\\u003E", html);
    }
}
