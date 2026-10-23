namespace LocalRun.Tests.Modules;

using LocalRun.Core.Modules;

public sealed class SolutionScannerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("localrun-sln-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Write(string relativePath, string content)
    {
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ClassicSln_ProjectsAreMapped()
    {
        string sln = Write("repo/Repo.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Lib\", \"src\\Lib\\Lib.csproj\", \"{1}\"\nEndProject\n" +
            "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"tools\", \"tools\", \"{2}\"\nEndProject\n");

        var map = SolutionScanner.BuildProjectToSolutionMap(root);

        Assert.Equal(sln, Assert.Single(map, kv => kv.Key == "Lib").Value);
        Assert.Single(map);
    }

    [Fact]
    public void Slnx_ProjectsAreMapped_IncludingInsideFolders()
    {
        string slnx = Write("modern/Modern.slnx",
            "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Folder Name=\"/tests/\">\n    <Project Path=\"tests\\App.Tests\\App.Tests.csproj\" />\n  </Folder>\n  <Folder Name=\"/docs/\">\n    <File Path=\"README.md\" />\n  </Folder>\n</Solution>\n");

        var map = SolutionScanner.BuildProjectToSolutionMap(root);

        Assert.Equal(["App", "App.Tests"], map.Keys.Order());
        Assert.All(map.Values, value => Assert.Equal(slnx, value));
    }

    [Fact]
    public void BothFormats_InTheSameTree_AreRead()
    {
        Write("a/A.sln", "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{1}\"\nEndProject\n");
        Write("b/B.slnx", "<Solution><Project Path=\"B/B.csproj\" /></Solution>");

        Assert.Equal(["A", "B"], SolutionScanner.BuildProjectToSolutionMap(root).Keys.Order());
    }
}
