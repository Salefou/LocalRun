namespace LocalRun.Tests.Processing;

using LocalRun.Core.Model;
using LocalRun.Core.Processing;

public class RunProcessorTests
{
    private static ProjectNode Project(string name, string type, bool skipRun = false, params string[] dependencies) => new()
    {
        Name = name,
        Path = $"{name}/{name}.csproj",
        Type = type,
        Version = ProjectVersion.Parse("1.0.0"),
        Dependencies = [.. dependencies],
        SkipRun = skipRun,
    };

    [Fact]
    public void PlanRun_KeepsExecutablesInDependencyOrder_WithoutSkipRun()
    {
        var config = new LocalRunConfig
        {
            Projects =
            [
                Project("Gateway", "executable", dependencies: ["Pricing", "Risk"]),
                Project("Pricing", "library"),
                Project("Risk", "executable", dependencies: ["Pricing"]),
                Project("Reports", "executable", skipRun: true, dependencies: ["Pricing"]),
                Project("Batch", "executable"),
            ],
        };

        Assert.Equal(["Batch", "Risk", "Gateway"], RunProcessor.PlanRun(config).Select(p => p.Name));
    }
}
