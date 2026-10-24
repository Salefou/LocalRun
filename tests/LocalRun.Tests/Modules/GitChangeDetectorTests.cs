namespace LocalRun.Tests.Modules;

using LibGit2Sharp;
using LocalRun.Core.Modules;

public class GitChangeDetectorTests
{
    /// <summary>
    /// A throwaway repository holding projects A and B, plus a Directory.Build.props at its root. "Pushing"
    /// moves refs/remotes/origin/main, which is all the detector looks at (it never fetches).
    /// </summary>
    private sealed class TempRepo : IDisposable
    {
        private static readonly Signature Author = new("test", "test@example.com", DateTimeOffset.Now);

        public string Root { get; } = Directory.CreateTempSubdirectory("localrun-git-").FullName;

        public Repository Repo { get; }

        public TempRepo()
        {
            Repository.Init(Root);
            Repo = new Repository(Root);
            Write("A/A.csproj", "<Project />");
            Write("B/B.csproj", "<Project />");
            Write("Directory.Build.props", "<Project />");
            Commit("initial");
        }

        public IEnumerable<(string Name, string CsprojPath)> Projects =>
            [("A", Path.Combine(Root, "A", "A.csproj")), ("B", Path.Combine(Root, "B", "B.csproj"))];

        public void Write(string relativePath, string content)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public Commit Commit(string message)
        {
            Commands.Stage(Repo, "*");
            return Repo.Commit(message, Author, Author);
        }

        public void Push(string branch = "main")
        {
            Repo.Refs.Add($"refs/remotes/origin/{branch}", Repo.Head.Tip.Id, allowOverwrite: true);
            Repo.Refs.Add("refs/remotes/origin/HEAD", $"refs/remotes/origin/{branch}", allowOverwrite: true);
        }

        public void Dispose()
        {
            Repo.Dispose();
            // git marks its object files read-only, which Directory.Delete refuses on Windows
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void CleanRepo_OnOrigin_HasNoChange()
    {
        using var repo = new TempRepo();
        repo.Push();

        Assert.Empty(GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void UntrackedFile_MarksItsProject()
    {
        using var repo = new TempRepo();
        repo.Push();
        repo.Write("A/New.cs", "class New {}");

        Assert.Equal(["A"], GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void CommitNotOnOrigin_MarksItsProject_EvenOnceCommitted()
    {
        using var repo = new TempRepo();
        repo.Push();
        Commands.Checkout(repo.Repo, repo.Repo.CreateBranch("feature"));
        repo.Write("B/Change.cs", "class Change {}");
        repo.Commit("change B");

        Assert.Equal(["B"], GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void ChangeOutsideProjectFolders_MarksTheWholeRepo()
    {
        using var repo = new TempRepo();
        repo.Push();
        repo.Write("Directory.Build.props", "<Project><PropertyGroup /></Project>");

        Assert.Equal(["A", "B"], GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void NoOriginBranch_MarksTheWholeRepo()
    {
        using var repo = new TempRepo();

        Assert.Equal(["A", "B"], GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void OriginMasterIsUsed_WhenThereIsNoOriginHead()
    {
        using var repo = new TempRepo();
        repo.Repo.Refs.Add("refs/remotes/origin/master", repo.Repo.Head.Tip.Id);

        Assert.Empty(GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void BeingBehindOrigin_IsNoChange()
    {
        using var repo = new TempRepo();
        var old = repo.Repo.Head.Tip;
        repo.Write("A/Upstream.cs", "class Upstream {}");
        repo.Commit("someone else's change, merged");
        repo.Push();
        repo.Repo.Reset(ResetMode.Hard, old);

        Assert.Empty(GitChangeDetector.FindChangedProjects(repo.Projects));
    }

    [Fact]
    public void ProjectOutsideAnyRepository_CountsAsChanged()
    {
        string folder = Directory.CreateTempSubdirectory("localrun-nogit-").FullName;
        try
        {
            Assert.Equal(["Loose"], GitChangeDetector.FindChangedProjects([("Loose", Path.Combine(folder, "Loose.csproj"))]));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MapToProjects_DeepestFolderWins_AndARootProjectOwnsEverything()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");
        var (changed, outside) = GitChangeDetector.MapToProjects(root,
            [("Lib", Path.Combine(root, "src", "Lib", "Lib.csproj")), ("Lib.Tests", Path.Combine(root, "src", "Lib", "Tests", "Lib.Tests.csproj"))],
            ["src/Lib/Tests/LibTests.cs", @"src\Lib\Lib.cs", "global.json", "src/LibOther/x.cs"]);

        Assert.Equal(["Lib", "Lib.Tests"], changed);
        Assert.Equal(["global.json", "src/LibOther/x.cs"], outside);

        var (rootOwned, none) = GitChangeDetector.MapToProjects(root, [("App", Path.Combine(root, "App.csproj"))], ["global.json"]);
        Assert.Equal(["App"], rootOwned);
        Assert.Empty(none);
    }
}
