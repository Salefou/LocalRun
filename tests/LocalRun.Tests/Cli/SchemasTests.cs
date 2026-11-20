namespace LocalRun.Tests.Cli;

using LocalRun.Cli;
using LocalRun.Core.Model;
using LocalRun.Core.Modules;
using LocalRun.Core.Processing;
using LocalRun.Logger;
using LocalRun.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>The hand-written schemas stay in line with the types they describe.</summary>
public class SchemasTests
{
    private static readonly JsonSerializerOptions Pascal = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    private static readonly JsonSerializerOptions Camel = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Each schema's root and <c>$defs</c>, with the type each describes and how it names its properties.</summary>
    public static TheoryData<string, string, Type> Shapes => new()
    {
        { Schemas.Config, "", typeof(LocalRunConfig) },
        { Schemas.Config, "ProjectNode", typeof(ProjectNode) },
        { Schemas.Config, "ProjectVersion", typeof(ProjectVersion) },
        { Schemas.Config, "BuildRecord", typeof(BuildRecord) },
        { Schemas.Config, "LeftOutProjects", typeof(LeftOutProjects) },
        { Schemas.Config, "RepositoryHead", typeof(RepositoryHead) },
        { Schemas.Repack, "", typeof(RepackConfig) },
        { Schemas.Repack, "RepackTree", typeof(RepackTree) },
        { Schemas.Repack, "ProjectRepack", typeof(ProjectRepack) },
        { Schemas.Repack, "ProjectVersion", typeof(ProjectVersion) },
        { Schemas.Settings, "", typeof(UserSettings) },
        { Schemas.Result, "", typeof(CommandReport) },
        { Schemas.Result, "Diagnostic", typeof(Diagnostic) },
        { Schemas.Result, "ReportError", typeof(ReportError) },
        { Schemas.Result, "BuildResult", typeof(BuildResult) },
        { Schemas.Result, "ProjectCounts", typeof(ProjectCounts) },
        { Schemas.Result, "LeftOutProjects", typeof(LeftOutProjects) },
        { Schemas.Result, "ListResult", typeof(ListResult) },
        { Schemas.Result, "ListedProject", typeof(ListedProject) },
        { Schemas.Result, "VerifyResult", typeof(VerifyResult) },
        { Schemas.Result, "PackageCheck", typeof(PackageCheck) },
        { Schemas.Result, "StatusResult", typeof(StatusResult) },
        { Schemas.Result, "BuildRecord", typeof(BuildRecord) },
        { Schemas.Result, "RepositoryHead", typeof(RepositoryHead) },
        { Schemas.Result, "StatusPlan", typeof(StatusPlan) },
        { Schemas.Result, "PlannedPackage", typeof(PlannedPackage) },
        { Schemas.Result, "PlotResult", typeof(PlotResult) },
        { Schemas.Result, "PackResult", typeof(PackResult) },
        { Schemas.Result, "PackageReport", typeof(PackageReport) },
        { Schemas.Result, "PackFailure", typeof(PackFailure) },
        { Schemas.Result, "BuildError", typeof(BuildError) },
        { Schemas.Result, "UpResult", typeof(UpResult) },
        { Schemas.Result, "ServiceReport", typeof(ServiceReport) },
        { Schemas.Result, "UpEvent", typeof(UpEvent) },
        { Schemas.Result, "SettingsResult", typeof(SettingsResult) },
        { Schemas.Result, "DoctorResult", typeof(DoctorResult) },
        { Schemas.Result, "DoctorCheck", typeof(DoctorCheck) },
        { Schemas.Result, "SchemaResult", typeof(SchemaResult) },
        { Schemas.Result, "ChangelogResult", typeof(ChangelogResult) },
        { Schemas.Result, "ChangelogRelease", typeof(ChangelogRelease) },
        { Schemas.Result, "ChangelogSection", typeof(ChangelogSection) },
        { Schemas.Result, "ChangelogEntry", typeof(ChangelogEntry) },
        { Schemas.Result, "HelpResult", typeof(HelpResult) },
        { Schemas.Result, "CommandInfo", typeof(CommandInfo) },
        { Schemas.Result, "ArgumentInfo", typeof(ArgumentInfo) },
        { Schemas.Result, "OptionInfo", typeof(OptionInfo) },
        { Schemas.Result, "ExitCodeInfo", typeof(ExitCodeInfo) },
        { Schemas.Result, "ErrorCode", typeof(ErrorCode) },
        { Schemas.Result, "DiagnosticCode", typeof(DiagnosticCode) },
        { Schemas.Result, "VersionResult", typeof(VersionResult) },
    };

    private static JsonElement Shape(string schema, string def) =>
        def == "" ? Schemas.Of(schema) : Schemas.Of(schema).GetProperty("$defs").GetProperty(def);

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EachShape_HasThePropertiesItsTypeSerializes(string schema, string def, Type type)
    {
        // The configs keep the C# names; the --json document and the settings are camelCase
        var options = schema is Schemas.Config or Schemas.Repack ? Pascal : Camel;
        var serialized = options.GetTypeInfo(type).Properties
            .Where(property => property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true)
                .Cast<JsonIgnoreAttribute>().Any(ignore => ignore.Condition == JsonIgnoreCondition.Always) != true)
            .Select(property => property.Name);

        var declared = Shape(schema, def).GetProperty("properties").EnumerateObject().Select(property => property.Name);

        Assert.Equal(serialized.Order(), declared.Order());
        Assert.False(Shape(schema, def).GetProperty("additionalProperties").GetBoolean());
    }

    [Theory]
    [InlineData(Schemas.Config)]
    [InlineData(Schemas.Repack)]
    [InlineData(Schemas.Result)]
    [InlineData(Schemas.Settings)]
    public void EachDefinition_IsChecked(string schema)
    {
        var checkedDefs = Shapes.Select(row => ((string)row[0], (string)row[1])).Where(row => row.Item1 == schema).Select(row => row.Item2);
        var defs = Schemas.Of(schema).TryGetProperty("$defs", out var all) ? all.EnumerateObject().Select(def => def.Name) : [];

        Assert.Equal(defs.Order(), checkedDefs.Where(def => def != "").Order());
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", Schemas.Of(schema).GetProperty("$schema").GetString());
    }

    [Fact]
    public void AWrittenConfig_HasNoUnknownField()
    {
        var version = new ProjectVersion { Major = 1, Minor = 2, PatchAndSuffix = "3-dev.1" };
        var config = new LocalRunConfig
        {
            RootPath = "root",
            LocalNugetPath = "feed",
            Projects = [new() { Name = "A", PackageId = "Acme.A", Path = "A.csproj", Type = "library", Test = true, Version = version, LatestVersion = version, Dependencies = ["B"] }],
            Build = new BuildRecord
            {
                Time = DateTimeOffset.Now,
                LrunVersion = "0.6.0",
                UsersOf = ["A"],
                ChangedProjects = ["A"],
                LeftOut = [new("cycle", ["C", "D"]) { Dependents = ["E"] }],
                Repositories = [new(".", null, null)],
            },
        };

        var json = JsonSerializer.SerializeToElement(config);

        Assert.Empty(Schemas.UnknownFields(Schemas.Of(Schemas.Config), json));
    }

    [Fact]
    public void UnknownFields_AreFoundAtAnyDepth()
    {
        var json = JsonDocument.Parse("""
            { "RootPath": "r", "Typo": 1, "Projects": [ { "Name": "A" }, { "Name": "B", "SkipRepack": true, "Version": { "Major": 1, "Extra": 0 } } ] }
            """).RootElement;

        Assert.Equal(["$.Typo", "$.Projects[1].SkipRepack", "$.Projects[1].Version.Extra"], Schemas.UnknownFields(Schemas.Of(Schemas.Config), json));
    }

    [Fact]
    public void UnderAnyOf_TheClosestBranchCounts()
    {
        var plot = JsonDocument.Parse("""{ "result": { "plot": "p.html" }, "error": null }""").RootElement;
        var typo = JsonDocument.Parse("""{ "result": { "plot": "p.html", "plott": 1 } }""").RootElement;

        Assert.Empty(Schemas.UnknownFields(Schemas.Of(Schemas.Result), plot));
        Assert.Equal(["$.result.plott"], Schemas.UnknownFields(Schemas.Of(Schemas.Result), typo));
    }

    [Fact]
    public void TheSettingsTemplate_HasNoUnknownField()
    {
        string uncommented = string.Join('\n', UserSettings.Template.Split('\n').Select(line => line.Replace("// \"", "\"")));
        var json = JsonDocument.Parse(uncommented, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        Assert.Empty(Schemas.UnknownFields(Schemas.Of(Schemas.Settings), json.RootElement));
        Assert.Equal(5, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void TheSchemaNames_AreTheArgumentValues()
    {
        Assert.Equal(Schemas.Names, CommandCatalog.Commands.Single(command => command.Name == "schema").Arguments.Single().Values);
        Assert.All(Schemas.Names, name => Assert.StartsWith("{", Schemas.Text(name)));
    }
}
