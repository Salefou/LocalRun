namespace LocalRun.Tests.Cli;

using LocalRun.Cli;
using LocalRun.Core.Processing;
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

        using var process = Process.Start(psi)!;
        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await copy;
        var run = new Run(process.ExitCode, stdout.ToArray(), await stderr);
        if (args.Contains("--json"))
            AssertMatchesTheResultSchema(run.Stdout);
        return run;
    }

    /// <summary>Every document, and every event of <c>up</c>, has only the fields <c>lrun schema result</c> declares.</summary>
    private static void AssertMatchesTheResultSchema(byte[] stdout)
    {
        var schema = Schemas.Of(Schemas.Result);
        var lines = Encoding.UTF8.GetString(stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool streamed = lines.Length > 1 && lines.All(line => line.TrimStart().StartsWith('{') && line.TrimEnd().EndsWith('}'));
        if (!streamed)
        {
            Assert.Empty(Schemas.UnknownFields(schema, JsonDocument.Parse(stdout).RootElement));
            return;
        }
        var upEvent = schema.GetProperty("$defs").GetProperty("UpEvent");
        foreach (var line in lines[..^1])
            Assert.Empty(Schemas.UnknownFields(upEvent, JsonDocument.Parse(line).RootElement));
        Assert.Empty(Schemas.UnknownFields(schema, JsonDocument.Parse(lines[^1]).RootElement));
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

    [Theory]
    [InlineData(null, false, "plot", "--json")]
    [InlineData(null, true, "plot", "--json", "--log-level", "debug")]
    [InlineData(null, true, "--log-level", "Debug", "plot", "--json")]
    [InlineData("Debug", true, "plot", "--json")]
    [InlineData("Debug", false, "plot", "--json", "--log-level", "Information")]   // the option wins
    public async Task TheLogLevel_ComesFromTheOption_ElseTheSettings(string? setting, bool debug, params string[] args)
    {
        if (setting != null)
            File.WriteAllText(Path.Combine(workDir, "settings.json"), $$"""{ "logLevel": "{{setting}}" }""");

        var run = await LrunAsync(args);

        // A failure's stack trace is logged at Debug
        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
        Assert.Equal(debug, run.Stderr.Contains("failure details"));
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
    public async Task Help_IsTheResult_AsTextAndAsData()
    {
        var run = await LrunAsync("help", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        var help = run.Json.GetProperty("result");
        Assert.Contains("--json", help.GetProperty("help").GetString());
        var build = help.GetProperty("commands").EnumerateArray().Single(command => command.GetProperty("name").GetString() == "build");
        var usersOf = build.GetProperty("options").EnumerateArray().Single(option => option.GetProperty("name").GetString() == "--users-of");
        Assert.Equal(("values", "<names>"), (usersOf.GetProperty("kind").GetString(), usersOf.GetProperty("value").GetString()));
        Assert.Contains(help.GetProperty("globalOptions").EnumerateArray(), option => option.GetProperty("name").GetString() == "--strict");
        Assert.Contains(help.GetProperty("exitCodes").EnumerateArray(), exit => exit.GetProperty("code").GetInt32() == LocalRunCli.ExitVerifyFailed);
        var usage = help.GetProperty("errorCodes").EnumerateArray().Single(code => code.GetProperty("code").GetString() == "usage");
        Assert.Equal(LocalRunCli.ExitUsage, usage.GetProperty("exitCode").GetInt32());
        var collision = help.GetProperty("diagnosticCodes").EnumerateArray().Single(code => code.GetProperty("code").GetString() == "name-collision");
        Assert.True(collision.GetProperty("strict").GetBoolean());
        Assert.Equal(AgentInstructions.Text(LocalRunCli.ReleaseVersion), help.GetProperty("agentInstructions").GetString());
    }

    [Fact]
    public async Task HelpAgent_PrintsTheAgentInstructions_OrTheWholeHelpWithJson()
    {
        var plain = await LrunAsync("help", "--agent");

        Assert.Equal(LocalRunCli.ExitSuccess, plain.ExitCode);
        Assert.Equal(AgentInstructions.Text(LocalRunCli.ReleaseVersion), Encoding.UTF8.GetString(plain.Stdout).Replace("\r\n", "\n"));

        var json = await LrunAsync("help", "--agent", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, json.ExitCode);
        Assert.True(json.Json.GetProperty("result").TryGetProperty("commands", out _));
    }

    [Fact]
    public async Task Changelog_PrintsTheReleases_AsWrittenOrAsData()
    {
        var plain = await LrunAsync("changelog", "--since", "0.5.0");

        Assert.Equal(LocalRunCli.ExitSuccess, plain.ExitCode);
        string text = Encoding.UTF8.GetString(plain.Stdout);
        Assert.Contains("## [0.5.1] - 2026-11-12", text);
        Assert.DoesNotContain("## [0.5.0]", text);

        var json = await LrunAsync("changelog", "--since", "0.4.0", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, json.ExitCode);
        var result = json.Json.GetProperty("result");
        Assert.Equal("0.4.0", result.GetProperty("since").GetString());
        var releases = result.GetProperty("releases").EnumerateArray().ToList();
        Assert.Equal("0.5.0", releases[^1].GetProperty("version").GetString());
        var added = releases[^1].GetProperty("sections").EnumerateArray().First(section => section.GetProperty("kind").GetString() == "Added");
        Assert.NotEmpty(added.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Changelog_RejectsASinceThatIsNoVersion()
    {
        var run = await LrunAsync("changelog", "--since", "latest", "--json");

        Assert.Equal(LocalRunCli.ExitUsage, run.ExitCode);
        Assert.Equal("usage", run.Json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Schema_PrintsTheSchemaAsIs_OrAsTheResult()
    {
        var plain = await LrunAsync("schema", "config");

        Assert.Equal(LocalRunCli.ExitSuccess, plain.ExitCode);
        Assert.Equal(Schemas.Text(Schemas.Config), Encoding.UTF8.GetString(plain.Stdout));

        var json = await LrunAsync("--json", "schema", "result");

        Assert.Equal(LocalRunCli.ExitSuccess, json.ExitCode);
        var result = json.Json.GetProperty("result");
        Assert.Equal("result", result.GetProperty("name").GetString());
        Assert.Equal("lrun --json document", result.GetProperty("schema").GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnknownConfigFields_AreIgnoredWithAWarning_ThatStrictRefuses()
    {
        Directory.CreateDirectory(Path.Combine(workDir, ".lrun"));
        File.WriteAllText(Path.Combine(workDir, ".lrun", "localrun.config.json"), """
            { "RootPath": "r", "LocalNugetPath": "f", "Projects": [], "SkipRun": [] }
            """);

        var run = await LrunAsync("plot", "--json", "--output", Path.Combine(workDir, "graph"));

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        var warning = Assert.Single(run.Json.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("unknown-config-field", warning.GetProperty("code").GetString());
        Assert.Contains("$.SkipRun", warning.GetProperty("message").GetString());

        var strict = await LrunAsync("plot", "--json", "--strict", "--output", Path.Combine(workDir, "graph"));

        Assert.Equal(LocalRunCli.ExitStrict, strict.ExitCode);
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
        Assert.Contains(failure.GetProperty("errors").EnumerateArray(), error => error.GetProperty("code").GetString()?.StartsWith("NETSDK") == true);
        Assert.Equal("skipped", packages["Top"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, packages["Top"].GetProperty("durationMs").ValueKind);
    }

    [Fact]
    public async Task Go_GivesACompileError_AsData()
    {
        string root = Path.Combine(workDir, "repos");
        WriteCsproj(root, "Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string source = Path.Combine(root, "Lib", "Broken.cs");
        File.WriteAllText(source, "class Broken\n{\n    void M() { Undefined(); }\n}\n");
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", root, "--local-nuget", Path.Combine(workDir, "feed"), "--include-external", "--csproj", "--json")).ExitCode);
        Assert.Equal(LocalRunCli.ExitSuccess, (await LrunAsync("pack", "--dry-run", "--json")).ExitCode);

        var go = await LrunAsync("go", "--json");

        Assert.Equal(LocalRunCli.ExitDotnetFailed, go.ExitCode);
        var failure = go.Json.GetProperty("result").GetProperty("packages")[0].GetProperty("failure");
        Assert.Equal(1, failure.GetProperty("errorCount").GetInt32()); // MSBuild prints it twice
        var error = failure.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("CS0103", error.GetProperty("code").GetString());
        Assert.Equal(source, error.GetProperty("file").GetString());
        Assert.Equal((3, 16), (error.GetProperty("line").GetInt32(), error.GetProperty("column").GetInt32()));
        Assert.Equal(Path.Combine(root, "Lib", "Lib.csproj"), error.GetProperty("project").GetString());
        Assert.Contains("Undefined", error.GetProperty("message").GetString());
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

    [Fact]
    public async Task Status_ShowsTheConfig_HowItWasBuilt_AndThePlan()
    {
        string root = WriteLibAndApp();
        WriteCsproj(root, "Lib.Tests", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteCsproj(root, "Other", """<Project Sdk="Microsoft.NET.Sdk" />""");
        string feed = Path.Combine(workDir, "feed");
        Assert.Equal(LocalRunCli.ExitSuccess, (await LrunAsync("build", "--root", root, "--local-nuget", feed, "--users-of", "Lib",
            "--include-external", "--tests", "--csproj", "--json")).ExitCode);

        var status = await LrunAsync("status", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, status.ExitCode);
        AssertPlain(status);
        var result = status.Json.GetProperty("result");
        string config = Path.Combine(workDir, ".lrun", "localrun.config.json");
        Assert.Equal(config, result.GetProperty("config").GetString());
        Assert.Equal(root, result.GetProperty("root").GetString());
        Assert.Equal(feed, result.GetProperty("localNuget").GetString());
        var build = result.GetProperty("build");
        Assert.Equal(LocalRunCli.Version, build.GetProperty("lrunVersion").GetString());
        Assert.Equal("--users-of Lib", build.GetProperty("filters").GetString());
        Assert.True(build.GetProperty("tests").GetBoolean());
        Assert.True(build.GetProperty("csproj").GetBoolean());
        Assert.False(build.GetProperty("union").GetBoolean());
        var filtered = Assert.Single(build.GetProperty("leftOut").EnumerateArray());
        Assert.Equal(("filter", "Other"), (filtered.GetProperty("reason").GetString(), filtered.GetProperty("projects")[0].GetString()));
        Assert.Equal([("Lib", 0, false), ("App", 1, false), ("Lib.Tests", 1, true)], result.GetProperty("projects").EnumerateArray()
            .Select(proj => (proj.GetProperty("name").GetString(), proj.GetProperty("depth").GetInt32(), proj.GetProperty("test").GetBoolean())));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("plan").ValueKind);
        Assert.Equal(0, status.Json.GetProperty("written").GetArrayLength());
        Assert.Contains("Plan: none", status.Stderr);
        Assert.Contains("Built with: --users-of Lib, --include-external, --tests, --csproj", status.Stderr);

        Assert.Equal(LocalRunCli.ExitSuccess, (await LrunAsync("pack", "--patch", "--dry-run", "--json")).ExitCode);
        var planned = await LrunAsync("status", "--json");

        var plan = planned.Json.GetProperty("result").GetProperty("plan");
        Assert.Equal(Path.Combine(workDir, ".lrun", "repack.config.json"), plan.GetProperty("path").GetString());
        Assert.False(plan.GetProperty("olderThanConfig").GetBoolean());
        var lib = plan.GetProperty("packages").EnumerateArray().First(package => package.GetProperty("project").GetString() == "Lib");
        Assert.Equal(("1.0.0", "1.0.1-local.1", 0), (lib.GetProperty("from").GetString(), lib.GetProperty("to").GetString(), lib.GetProperty("depth").GetInt32()));
        Assert.DoesNotContain("plan-older-than-config", Codes(planned));

        // A plan made before the config was rebuilt: go would pack the earlier one
        File.SetLastWriteTime(plan.GetProperty("path").GetString()!, DateTime.Now.AddMinutes(-5));
        var stale = await LrunAsync("status", "--strict", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, stale.ExitCode);
        Assert.True(stale.Json.GetProperty("result").GetProperty("plan").GetProperty("olderThanConfig").GetBoolean());
        Assert.Equal(["plan-older-than-config"], Codes(stale));
    }

    [Fact]
    public async Task Status_OfAConfigFromAnOlderLrun_HasNoBuildRecord()
    {
        Directory.CreateDirectory(Path.Combine(workDir, ".lrun"));
        File.WriteAllText(Path.Combine(workDir, ".lrun", "localrun.config.json"), """
            { "RootPath": "C:\\repos", "LocalNugetPath": "C:\\feed", "Projects": [ { "Name": "Lib", "Path": "Lib\\Lib.csproj",
              "Type": "library", "Version": { "Major": 1, "Minor": 2, "PatchAndSuffix": "0" },
              "LatestVersion": { "Major": 1, "Minor": 2, "PatchAndSuffix": "1-local.3" }, "Dependencies": [] } ] }
            """);

        var status = await LrunAsync("status", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, status.ExitCode);
        var result = status.Json.GetProperty("result");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("build").ValueKind);
        var lib = Assert.Single(result.GetProperty("projects").EnumerateArray());
        Assert.Equal(("1.2.0", "1.2.1-local.3"), (lib.GetProperty("version").GetString(), lib.GetProperty("latestVersion").GetString()));
        Assert.Empty(Codes(status));
        Assert.Contains("Built by an older lrun", status.Stderr);
    }

    [Fact]
    public async Task Status_WithoutAConfig_PointsAtBuild()
    {
        var status = await LrunAsync("status", "--json");

        Assert.Equal(LocalRunCli.ExitInput, status.ExitCode);
        Assert.Equal("config-not-found", status.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("lrun build", status.Json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Verify_FailsWithItsOwnCode_WhenAPackageIsMissing()
    {
        string root = WriteLibAndApp();
        string feed = Path.Combine(workDir, "feed");
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", root, "--local-nuget", feed, "--include-external", "--csproj", "--json")).ExitCode);

        var verify = await LrunAsync("verify", "--json");

        Assert.Equal(LocalRunCli.ExitVerifyFailed, verify.ExitCode);
        AssertPlain(verify);
        Assert.Equal("verify-failed", verify.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("package-missing", Codes(verify));
        var result = verify.Json.GetProperty("result");
        Assert.Equal(feed, result.GetProperty("feed").GetString());
        Assert.Equal(1, result.GetProperty("missing").GetInt32());
        var lib = Assert.Single(result.GetProperty("packages").EnumerateArray());
        Assert.Equal(("Lib", "missing"), (lib.GetProperty("project").GetString(), lib.GetProperty("status").GetString()));

        // Packed afterwards: up to date
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, "Lib.1.0.1-local.1.nupkg"), "");
        var again = await LrunAsync("verify", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, again.ExitCode);
        Assert.Equal(1, again.Json.GetProperty("result").GetProperty("ok").GetInt32());
    }

    [Fact]
    public async Task BuildVerify_WritesTheConfig_ThenFailsLikeVerify()
    {
        string root = WriteLibAndApp();

        var build = await LrunAsync("build", "--root", root, "--local-nuget", Path.Combine(workDir, "feed"), "--include-external", "--csproj",
            "--verify", "--json");

        Assert.Equal(LocalRunCli.ExitVerifyFailed, build.ExitCode);
        Assert.True(File.Exists(Path.Combine(workDir, ".lrun", "localrun.config.json")));
        var verify = build.Json.GetProperty("result").GetProperty("verify");
        Assert.Equal(1, verify.GetProperty("missing").GetInt32());
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
    [InlineData("""{"SchemaVersion":2,"Projects":[]}""")] // written by a newer lrun
    [InlineData("""{"SchemaVersion":0,"Projects":[]}""")]
    public async Task AnInvalidConfig_IsAnInputError(string content)
    {
        Directory.CreateDirectory(Path.Combine(workDir, ".lrun"));
        File.WriteAllText(Path.Combine(workDir, ".lrun", "localrun.config.json"), content);

        var run = await LrunAsync("plot", "--json");

        Assert.Equal(LocalRunCli.ExitInput, run.ExitCode);
        Assert.Equal("config-invalid", run.Json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Doctor_ReportsEveryCheck_AndAWarningDoesNotFail()
    {
        string root = Directory.CreateDirectory(Path.Combine(workDir, "empty")).FullName;

        var run = await LrunAsync("doctor", "--json", "--root", root, "--local-nuget", Path.Combine(workDir, "feed"));

        Assert.Equal(LocalRunCli.ExitSuccess, run.ExitCode);
        AssertPlain(run);
        var result = run.Json.GetProperty("result");
        Assert.Equal(["settings", "dotnet", "root", "local-feed"],
            result.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("check").GetString()));
        Assert.Equal(1, result.GetProperty("warn").GetInt32()); // no project under the root
        Assert.Equal(["doctor-warning"], Codes(run));
    }

    [Fact]
    public async Task Doctor_FailsOnAFailedCheck()
    {
        var run = await LrunAsync("doctor", "--json", "--root", Path.Combine(workDir, "no-such-dir"), "--local-nuget", Path.Combine(workDir, "feed"));

        Assert.Equal(LocalRunCli.ExitFailure, run.ExitCode);
        Assert.Equal("doctor-failed", run.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("fail", run.Json.GetProperty("result").GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("check").GetString() == "root").GetProperty("status").GetString());
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

    /// <summary>Records the test process as a running up in the work folder: alive, so taken as running.</summary>
    private void FakeRunningUp() =>
        new UpState(Environment.ProcessId, UpState.CurrentProcessStarted(), "config", "logs", 10, [new UpStateService("Api", 4242, null)])
            .Write(Path.Combine(Directory.CreateDirectory(Path.Combine(workDir, ".lrun")).FullName, UpState.FileName));

    [Fact]
    public async Task Down_WithoutAnUpRunning_HasNothingToStop()
    {
        var down = await LrunAsync("down", "--json");

        Assert.Equal(LocalRunCli.ExitSuccess, down.ExitCode);
        var result = down.Json.GetProperty("result");
        Assert.False(result.GetProperty("stopped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("up").ValueKind);
        Assert.Equal(Path.Combine(workDir, ".lrun", UpState.FileName), result.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Up_WhileAnotherRuns_IsRefused()
    {
        FakeRunningUp();

        var up = await LrunAsync("up", "--json");

        Assert.Equal(LocalRunCli.ExitFailure, up.ExitCode);
        var error = up.Json.GetProperty("error");
        Assert.Equal("up-running", error.GetProperty("code").GetString());
        Assert.Contains("lrun down", error.GetProperty("message").GetString());
        // Still the other one's
        Assert.NotNull(UpState.ReadRunning(Path.Combine(workDir, ".lrun", UpState.FileName)));
    }

    [Fact]
    public async Task Status_ShowsTheRunningUp()
    {
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", Directory.CreateDirectory(Path.Combine(workDir, "empty")).FullName, "--csproj", "--json")).ExitCode);
        Assert.Equal(JsonValueKind.Null, (await LrunAsync("status", "--json")).Json.GetProperty("result").GetProperty("running").ValueKind);
        FakeRunningUp();

        var running = (await LrunAsync("status", "--json")).Json.GetProperty("result").GetProperty("running");

        Assert.Equal(Environment.ProcessId, running.GetProperty("pid").GetInt32());
        Assert.Equal("Api", running.GetProperty("services")[0].GetProperty("project").GetString());
    }

    [Fact]
    public async Task Down_StopsAnUpInTheBackground_AsCtrlCWould()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(workDir, "repos");
        WriteApp(root, "Waiter", """
            var stop = new System.Threading.ManualResetEventSlim();
            System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
            System.Console.WriteLine("ready");
            stop.Wait();
            System.Console.WriteLine("graceful");
            """);
        Assert.Equal(LocalRunCli.ExitSuccess,
            (await LrunAsync("build", "--root", root, "--include-external", "--csproj", "--json")).ExitCode);

        var upRun = LrunAsync("up", "--json");
        string log = Path.Combine(workDir, ".lrun", "logs", "Waiter.log");
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (!ReadShared(log).Contains("ready"))
        {
            Assert.False(upRun.IsCompleted, "up exited before its service was ready");
            Assert.True(DateTime.UtcNow < deadline, "the service never got ready");
            await Task.Delay(200);
        }
        var state = UpState.ReadRunning(Path.Combine(workDir, ".lrun", UpState.FileName));
        Assert.NotNull(state);
        Assert.Equal("Waiter", Assert.Single(state.Services).Project);

        var down = await LrunAsync("down", "--json");
        var up = await upRun.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(LocalRunCli.ExitSuccess, down.ExitCode);
        Assert.True(down.Json.GetProperty("result").GetProperty("stopped").GetBoolean());
        Assert.Equal(state.Pid, down.Json.GetProperty("result").GetProperty("up").GetProperty("pid").GetInt32());
        Assert.Equal(LocalRunCli.ExitSuccess, up.ExitCode);
        var lines = Encoding.UTF8.GetString(up.Stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.Contains(lines, line => line.TryGetProperty("event", out var e) && e.GetString() == "stopping");
        var service = Assert.Single(lines[^1].GetProperty("result").GetProperty("services").EnumerateArray());
        // Stopped by its own Ctrl+C handler, not killed
        Assert.Equal("stopped", service.GetProperty("status").GetString());
        Assert.Equal(0, service.GetProperty("exitCode").GetInt32());
        Assert.Contains("graceful", File.ReadAllText(log));
        Assert.False(File.Exists(Path.Combine(workDir, ".lrun", UpState.FileName)));
    }

    /// <summary>A file another process is writing, or empty when it doesn't exist yet.</summary>
    private static string ReadShared(string path)
    {
        if (!File.Exists(path)) return "";
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

    private static void WriteCsproj(string root, string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllText(Path.Combine(root, name, $"{name}.csproj"), content);
    }
}
