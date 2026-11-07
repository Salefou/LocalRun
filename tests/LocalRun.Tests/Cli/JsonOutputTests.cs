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
    [InlineData("--strict", "nope", "--json")]
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

    [Theory]
    [InlineData("--json")]
    [InlineData("--json", "--strict")]
    public async Task NoCommand_GivesTheHelp_AsAUsageError(params string[] args)
    {
        var run = await LrunAsync(args);

        Assert.Equal(LocalRunCli.ExitUsage, run.ExitCode);
        AssertPlain(run);
        Assert.Equal(JsonValueKind.Null, run.Json.GetProperty("command").ValueKind);
        Assert.Equal("usage", run.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("--strict", run.Json.GetProperty("result").GetProperty("help").GetString());
    }

    [Fact]
    public async Task AMissingConfig_HasItsOwnCode()
    {
        var run = await LrunAsync("plot", "--json");

        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
        AssertPlain(run);
        Assert.Equal("config-not-found", run.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("lrun build", run.Json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task AMissingRoot_HasItsOwnCode()
    {
        var run = await LrunAsync("build", "--root", Path.Combine(workDir, "nowhere"), "--json");

        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
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
        Assert.Equal("name-collision", error.GetProperty("code").GetString());
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
        Assert.Equal("planned", lib.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, lib.GetProperty("durationMs").ValueKind);
        Assert.Equal(Path.Combine(workDir, ".lrun", "repack.config.json"), plan.GetProperty("plan").GetString());
    }

    [Fact]
    public async Task Go_SaysWhatBecameOfEachProject_AfterAFailureToo()
    {
        string root = Path.Combine(workDir, "repos");
        WriteCsproj(root, "Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework><PackageId>Acme.Lib</PackageId></PropertyGroup>
            </Project>
            """);
        // No target framework: its restore fails
        WriteCsproj(root, "Mid", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        WriteCsproj(root, "Top", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Mid\Mid.csproj" /></ItemGroup>
            </Project>
            """);
        string feed = Path.Combine(workDir, "feed");
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", root, "--local-nuget", feed, "--include-external", "--csproj", "--json")).ExitCode);
        Assert.Equal(LocalRunCli.ExitSuccess, (await LrunAsync("pack", "--dry-run", "--json")).ExitCode);

        var go = await LrunAsync("go", "--json");

        Assert.Equal(LocalRunCli.ExitDotnetFailed, go.ExitCode);
        AssertPlain(go);
        Assert.Equal("dotnet-failed", go.Json.GetProperty("error").GetProperty("code").GetString());
        var result = go.Json.GetProperty("result");
        Assert.False(result.GetProperty("packed").GetBoolean());
        var packages = result.GetProperty("packages").EnumerateArray().ToDictionary(package => package.GetProperty("project").GetString()!);
        Assert.Equal("packed", packages["Lib"].GetProperty("status").GetString());
        Assert.Equal("Acme.Lib", packages["Lib"].GetProperty("packageId").GetString());
        Assert.True(packages["Lib"].GetProperty("durationMs").GetInt64() > 0);
        Assert.True(File.Exists(Path.Combine(feed, "Acme.Lib.1.0.0-local.1.nupkg")));
        Assert.Equal("failed", packages["Mid"].GetProperty("status").GetString());
        var failure = packages["Mid"].GetProperty("failure");
        Assert.Equal("restore", failure.GetProperty("step").GetString());
        Assert.NotEqual(0, failure.GetProperty("exitCode").GetInt32());
        Assert.NotEqual(0, failure.GetProperty("output").GetArrayLength());
        Assert.Equal("skipped", packages["Top"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, packages["Top"].GetProperty("durationMs").ValueKind);
    }

    private string WriteLibAndApp()
    {
        string root = Path.Combine(workDir, "repos");
        WriteCsproj(root, "Lib", """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteCsproj(root, "App", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        return root;
    }

    [Fact]
    public async Task List_SaysWhatBuildWouldKeep_AndWritesNothing()
    {
        string root = WriteLibAndApp();
        WriteCsproj(root, "Other", """<Project Sdk="Microsoft.NET.Sdk" />""");

        var list = await LrunAsync("list", "--root", root, "--users-of", "lib", "--include-external", "--csproj", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, list.ExitCode);
        AssertPlain(list);
        var result = list.Json.GetProperty("result");
        Assert.Equal(root, result.GetProperty("root").GetString());
        Assert.Equal("--users-of lib", result.GetProperty("filters").GetString());
        Assert.Equal(3, result.GetProperty("scanned").GetInt32());
        Assert.Equal(1, result.GetProperty("counts").GetProperty("executables").GetInt32());
        var projects = result.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal([("Lib", 0, "library"), ("App", 1, "executable")], projects.Select(proj =>
            (proj.GetProperty("name").GetString(), proj.GetProperty("depth").GetInt32(), proj.GetProperty("type").GetString())));
        Assert.Equal(["Lib"], projects[1].GetProperty("dependencies").EnumerateArray().Select(dep => dep.GetString()));
        Assert.Equal("1.0.0", projects[0].GetProperty("version").GetString());
        Assert.Equal(Path.Combine("Lib", "Lib.csproj"), projects[0].GetProperty("path").GetString());
        var filtered = Assert.Single(result.GetProperty("leftOut").EnumerateArray());
        Assert.Equal(["Other"], filtered.GetProperty("projects").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(0, list.Json.GetProperty("written").GetArrayLength());
        Assert.False(Directory.Exists(Path.Combine(workDir, ".lrun")));
    }

    [Fact]
    public async Task List_RejectsAnUnknownName()
    {
        var list = await LrunAsync("list", "--root", WriteLibAndApp(), "--deps-of", "Nope", "--csproj", "--json");

        Assert.Equal(LocalRunCli.ExitUsage, list.ExitCode);
        Assert.Contains("Nope", list.Json.GetProperty("error").GetProperty("message").GetString());
    }

    private static IEnumerable<string?> Codes(Run run) =>
        run.Json.GetProperty("diagnostics").EnumerateArray().Select(diagnostic => diagnostic.GetProperty("code").GetString());

    [Fact]
    public async Task Strict_IgnoresWhatIsExpected()
    {
        string root = WriteLibAndApp();

        // Warns that no solution references them: expected, not a reason to fail
        var run = await LrunAsync("--strict", "build", "--root", root, "--include-external", "--csproj", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        Assert.Contains("no-solution", Codes(run));
    }

    [Fact]
    public async Task Strict_FailsWhatLeftSomethingOut_AfterWritingIt()
    {
        string root = WriteLibAndApp();
        WriteCsproj(Path.Combine(root, "One"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteCsproj(Path.Combine(root, "Two"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");

        var run = await LrunAsync("build", "--root", root, "--include-external", "--csproj", "--strict", "--json");

        Assert.Equal(LocalRunCli.ExitStrict, run.ExitCode);
        AssertPlain(run);
        Assert.Equal("failed", run.Json.GetProperty("status").GetString());
        var error = run.Json.GetProperty("error");
        Assert.Equal("strict", error.GetProperty("code").GetString());
        Assert.Contains("name-collision", error.GetProperty("message").GetString());
        // The diagnostics are the reasons; the refusal itself is the error
        Assert.DoesNotContain("strict", Codes(run));
        Assert.NotEqual(JsonValueKind.Null, run.Json.GetProperty("result").ValueKind);
        Assert.True(File.Exists(Path.Combine(workDir, ".lrun", "localrun.config.json")));
        Assert.Contains("--strict", run.Stderr);
    }

    [Fact]
    public async Task Strict_FailsWithoutJsonToo()
    {
        string root = WriteLibAndApp();
        WriteCsproj(Path.Combine(root, "One"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteCsproj(Path.Combine(root, "Two"), "Dup", """<Project Sdk="Microsoft.NET.Sdk" />""");

        var run = await LrunAsync("build", "--root", root, "--include-external", "--csproj", "--strict");

        Assert.Equal(LocalRunCli.ExitStrict, run.ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEmptySelection_Succeeds_WithAMarker(bool strict)
    {
        string root = Directory.CreateDirectory(Path.Combine(workDir, "empty")).FullName;

        string feed = Path.Combine(workDir, "feed");

        var build = await LrunAsync([.. new[] { "build", "--root", root, "--local-nuget", feed, "--csproj", "--json" },
            .. strict ? ["--strict"] : Array.Empty<string>()]);

        Assert.Equal(LocalRunCli.ExitSuccess, build.ExitCode);
        Assert.Equal(["empty-selection"], Codes(build));

        var pack = await LrunAsync("pack", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, pack.ExitCode);
        Assert.Equal(["empty-selection"], Codes(pack));
        Assert.Equal(0, pack.Json.GetProperty("result").GetProperty("packages").GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    public async Task AnInvalidConfig_IsAnInputError(string content)
    {
        Directory.CreateDirectory(Path.Combine(workDir, ".lrun"));
        File.WriteAllText(Path.Combine(workDir, ".lrun", "localrun.config.json"), content);

        var run = await LrunAsync("plot", "--json");

        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
        Assert.Equal("config-invalid", run.Json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidSettings_AreAnInputError()
    {
        File.WriteAllText(Path.Combine(workDir, "settings.json"), """{ "root": "relative/path" }""");

        var run = await LrunAsync("build", "--json");

        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
        Assert.Equal("settings-invalid", run.Json.GetProperty("error").GetProperty("code").GetString());
    }

    private static void WriteApp(string root, string name, string main)
    {
        WriteCsproj(root, name, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, name, "Program.cs"), main);
    }

    [Fact]
    public async Task Up_StreamsOneEventPerLine_ThenTheResult_AndLogsEachService()
    {
        string root = Path.Combine(workDir, "repos");
        WriteApp(root, "Hello", """
            System.Console.WriteLine("hello");
            System.Console.WriteLine("\u001b[31mred\u001b[0m");
            System.Console.Error.WriteLine("oops");
            """);
        WriteApp(root, "Broken", "return 3;");
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", root, "--include-external", "--csproj", "--json")).ExitCode);

        var up = await LrunAsync("up", "--json");

        // Broken failed on its own
        Assert.Equal(LocalRunCli.ExitFailure, up.ExitCode);
        string stdout = Encoding.UTF8.GetString(up.Stdout);
        Assert.DoesNotContain('\x1B', stdout);
        Assert.DoesNotContain("hello", stdout);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        var events = lines[..^1].Select(line => (line.GetProperty("event").GetString(), line.TryGetProperty("project", out var p) ? p.GetString() : null)).ToArray();
        Assert.Equal([("building", "Broken"), ("building", "Hello"), ("started", "Broken"), ("started", "Hello"), ("running", null)], events[..5]);
        Assert.Equal([("exited", "Broken"), ("exited", "Hello")], events[5..].Order());
        var started = lines.First(line => line.GetProperty("event").GetString() == "started");
        Assert.True(started.GetProperty("pid").GetInt32() > 0);
        Assert.True(started.TryGetProperty("time", out _));

        var report = lines[^1];
        Assert.Equal("up", report.GetProperty("command").GetString());
        Assert.Equal("command-failed", report.GetProperty("error").GetProperty("code").GetString());
        string logs = Path.Combine(workDir, ".lrun", "logs");
        var result = report.GetProperty("result");
        Assert.Equal(logs, result.GetProperty("logs").GetString());
        var services = result.GetProperty("services").EnumerateArray().ToDictionary(service => service.GetProperty("project").GetString()!);
        Assert.Equal("failed", services["Broken"].GetProperty("status").GetString());
        Assert.Equal(3, services["Broken"].GetProperty("exitCode").GetInt32());
        Assert.Equal("exited", services["Hello"].GetProperty("status").GetString());
        Assert.Equal(0, services["Hello"].GetProperty("exitCode").GetInt32());
        Assert.Equal(2, report.GetProperty("written").GetArrayLength());

        string helloLog = Path.Combine(logs, "Hello.log");
        Assert.Equal(helloLog, services["Hello"].GetProperty("log").GetString());
        // stdout and stderr lines interleave in no set order; the colors are gone
        Assert.Equal(["hello", "oops", "red"], File.ReadAllLines(helloLog).Order());
    }

    [Fact]
    public async Task Up_WithNothingToRun_IsOneLineToo()
    {
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", Directory.CreateDirectory(Path.Combine(workDir, "empty")).FullName, "--csproj", "--json")).ExitCode);

        var up = await LrunAsync("up", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, up.ExitCode);
        string line = Assert.Single(Encoding.UTF8.GetString(up.Stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var report = JsonDocument.Parse(line).RootElement;
        Assert.Equal(["empty-selection"], Codes(new Run(0, Encoding.UTF8.GetBytes(line), "")));
        Assert.Equal(0, report.GetProperty("result").GetProperty("services").GetArrayLength());
    }

    private static void WriteCsproj(string root, string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllText(Path.Combine(root, name, $"{name}.csproj"), content);
    }
}
