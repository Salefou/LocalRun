namespace LocalRun.Tests.Cli;

using LocalRun.Cli;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>
/// The <c>--json</c> contract, checked on a real process with redirected output: the mode is read before anything logs,
/// and console encodings only show through a real pipe.
/// </summary>
public sealed class JsonOutputTests : IDisposable
{
    private readonly string workDir = Directory.CreateTempSubdirectory("localrun-json-").FullName;

    public void Dispose() => Directory.Delete(workDir, recursive: true);

    private sealed record Run(int ExitCode, byte[] Stdout, string Stderr)
    {
        public JsonElement Json => JsonDocument.Parse(Stdout).RootElement;
    }

    private async Task<Run> LrunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LocalRun.dll"));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // Never the real settings file
        psi.Environment["LOCALRUN_SETTINGS"] = Path.Combine(workDir, "settings.json");
        psi.Environment.Remove("LOCALRUN_LOG_LEVEL");

        using var process = Process.Start(psi)!;
        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await copy;
        return new(process.ExitCode, stdout.ToArray(), await stderr);
    }

    private static void AssertPlain(Run run)
    {
        string stdout = Encoding.UTF8.GetString(run.Stdout);
        Assert.Equal((byte)'{', run.Stdout[0]); // no BOM, nothing before the document
        foreach (var text in new[] { stdout, run.Stderr })
        {
            Assert.DoesNotContain('\x1B', text);
            Assert.DoesNotMatch("[›✓×▸■→]", text);
        }
    }

    [Fact]
    public async Task Version_IsTheOnlyThingOnStdout()
    {
        var run = await LrunAsync("version", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        AssertPlain(run);
        var json = run.Json;
        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("version", json.GetProperty("command").GetString());
        Assert.Equal("succeeded", json.GetProperty("status").GetString());
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());
        Assert.Equal(LocalRunCli.Version, json.GetProperty("result").GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("error").ValueKind);
        Assert.Equal(string.Empty, run.Stderr);
    }

    [Fact]
    public async Task TheFlag_CanComeBeforeTheCommand()
    {
        var run = await LrunAsync("--json", "version");

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        Assert.Equal("version", run.Json.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheEnvelope_KeepsItsFieldsAndTheirOrder()
    {
        var run = await LrunAsync("version", "--json");

        // Renaming one is a schemaVersion bump: change this test deliberately
        Assert.Equal(["schemaVersion", "command", "status", "exitCode", "durationMs", "result", "diagnostics", "written", "error"],
            run.Json.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("build", "--nope", "--json")]
    [InlineData("version", "--json", "--json")]
    [InlineData("version", "--json", "yes")]
    [InlineData("nope", "--json")]
    [InlineData("--json")]
    public async Task UsageErrors_StillWriteADocument(params string[] args)
    {
        var run = await LrunAsync(args);

        Assert.Equal(LocalRunCli.ExitUsage, run.ExitCode);
        AssertPlain(run);
        Assert.Equal("failed", run.Json.GetProperty("status").GetString());
        Assert.Equal(LocalRunCli.ExitUsage, run.Json.GetProperty("exitCode").GetInt32());
        Assert.Equal("usage", run.Json.GetProperty("error").GetProperty("code").GetString());
        // The logs say it too, on stderr; the document doesn't repeat the error as a diagnostic
        Assert.NotEqual(string.Empty, run.Stderr);
        Assert.Equal(0, run.Json.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AMissingConfig_HasItsOwnCode()
    {
        var run = await LrunAsync("plot", "--json");

        Assert.Equal(LocalRunCli.ExitFailure, run.ExitCode);
        AssertPlain(run);
        Assert.Equal("config-not-found", run.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("lrun build", run.Json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task AMissingRoot_HasItsOwnCode()
    {
        var run = await LrunAsync("build", "--root", Path.Combine(workDir, "nowhere"), "--json");

        Assert.Equal(LocalRunCli.ExitFailure, run.ExitCode);
        Assert.Equal("root-not-found", run.Json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Help_IsTheResult()
    {
        var run = await LrunAsync("help", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        Assert.Contains("--json", run.Json.GetProperty("result").GetProperty("help").GetString());
    }

    [Fact]
    public async Task BuildThenPack_ReportWhatTheyDid()
    {
        string root = Path.Combine(workDir, "repos");
        WriteCsproj(root, "Lib", """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteCsproj(root, "App", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        WriteCsproj(Path.Combine(root, "One"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteCsproj(Path.Combine(root, "Two"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");
        string feed = Path.Combine(workDir, "feed");

        var build = await LrunAsync("build", "--root", root, "--local-nuget", feed, "--include-external", "--csproj", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, build.ExitCode);
        AssertPlain(build);
        string config = Path.Combine(workDir, ".lrun", "localrun.config.json");
        var result = build.Json.GetProperty("result");
        Assert.Equal(config, result.GetProperty("config").GetString());
        Assert.Equal(2, result.GetProperty("projects").GetProperty("total").GetInt32());
        Assert.Equal(1, result.GetProperty("projects").GetProperty("executables").GetInt32());
        Assert.Equal(["App"], result.GetProperty("services").EnumerateArray().Select(name => name.GetString()));
        var collision = Assert.Single(result.GetProperty("leftOut").EnumerateArray());
        Assert.Equal("name-collision", collision.GetProperty("reason").GetString());
        Assert.Equal(2, collision.GetProperty("paths").GetArrayLength());
        // Besides the warnings about projects no solution references
        var error = Assert.Single(build.Json.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("level").GetString() == "error");
        Assert.StartsWith("Several projects are named Dup", error.GetProperty("message").GetString());
        Assert.Equal([config], build.Json.GetProperty("written").EnumerateArray().Select(path => path.GetString()));
        Assert.Contains("Dup", build.Stderr);

        var pack = await LrunAsync("pack", "--patch", "--dry-run", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, pack.ExitCode);
        AssertPlain(pack);
        var plan = pack.Json.GetProperty("result");
        Assert.False(plan.GetProperty("packed").GetBoolean());
        Assert.Equal(["Lib", "App"], plan.GetProperty("packages").EnumerateArray().Select(package => package.GetProperty("project").GetString()));
        var lib = plan.GetProperty("packages")[0];
        Assert.Equal("1.0.0", lib.GetProperty("from").GetString());
        Assert.Equal("1.0.1-local.1", lib.GetProperty("to").GetString());
        Assert.Equal(Path.Combine(workDir, ".lrun", "repack.config.json"), plan.GetProperty("plan").GetString());
    }

    private static void WriteCsproj(string root, string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllText(Path.Combine(root, name, $"{name}.csproj"), content);
    }
}
