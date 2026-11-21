namespace LocalRun.Tests.Modules;

using LibGit2Sharp;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Logger;

public sealed class RepositoryHeadsTests : IDisposable
{
    private static readonly Signature Author = new("test", "test@example.com", DateTimeOffset.Now);

    private readonly string root = Directory.CreateTempSubdirectory("localrun-heads-").FullName;

    public void Dispose()
    {
        // git marks its object files read-only, which Directory.Delete refuses on Windows
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    private string Csproj(string repo, string name)
    {
        string path = Path.Combine(root, repo, name, $"{name}.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
        return path;
    }

    private static Commit CommitAll(Repository repo)
    {
        Commands.Stage(repo, "*");
        return repo.Commit("initial", Author, Author);
    }

    [Fact]
    public void EachRepository_OnceWithItsHead_SortedByPath()
    {
        string a = Csproj("shop", "A");
        string a2 = Csproj("shop", "A2");
        string b = Csproj("billing", "B");
        string c = Csproj("loose", "C");
        Repository.Init(Path.Combine(root, "shop"));
        Repository.Init(Path.Combine(root, "billing"));
        string shopHead, shopBranch, billingHead;
        using (var shop = new Repository(Path.Combine(root, "shop")))
        {
            shopHead = CommitAll(shop).Sha;
            shopBranch = shop.Head.FriendlyName;
        }
        using (var billing = new Repository(Path.Combine(root, "billing")))
        {
            var commit = CommitAll(billing);
            billingHead = commit.Sha;
            Commands.Checkout(billing, commit);
        }

        var heads = RepositoryHeads.Find(root, [a, a2, b, c]);

        Assert.Equal([new RepositoryHead("billing", billingHead, null), new RepositoryHead("shop", shopHead, shopBranch)], heads);
    }

    [Fact]
    public void GitThatCannotLoad_RecordsNoHead_WithAWarning()
    {
        string a = Csproj("shop", "A");
        var notLoaded = new TypeInitializationException("LibGit2Sharp.Core.NativeMethods",
            new DllNotFoundException("Unable to load DLL 'git2': The filename or extension is too long."));

        var heads = RepositoryHeads.Find(root, [a], _ => throw notLoaded);

        Assert.Empty(heads);
        Assert.Contains(Log.Diagnostics, diagnostic => diagnostic.Code == "git-unavailable"
            && diagnostic.Message.Contains("The filename or extension is too long"));
    }

    [Fact]
    public void AFolderThatCannotBeSearched_IsLeftOut_TheOthersKept()
    {
        string a = Csproj("shop", "A");
        string b = Csproj("billing", "B");
        Repository.Init(Path.Combine(root, "shop"));

        var heads = RepositoryHeads.Find(root, [a, b],
            folder => folder.Contains("billing") ? throw new IOException("denied") : Repository.Discover(folder));

        Assert.Equal("shop", Assert.Single(heads).Path);
    }

    [Fact]
    public void ARepositoryWithoutCommits_HasNoHead()
    {
        string a = Csproj(".", "A");
        Repository.Init(root);

        var head = Assert.Single(RepositoryHeads.Find(root, [a]));

        Assert.Equal(".", head.Path);
        Assert.Null(head.Head);
    }
}
