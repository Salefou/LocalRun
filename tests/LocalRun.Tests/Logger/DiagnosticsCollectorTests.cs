namespace LocalRun.Tests.Logger;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;

public class DiagnosticsCollectorTests
{
    [Fact]
    public void KeepsWarningsAndErrors_InOrder_Trimmed()
    {
        var collector = new DiagnosticsCollector();
        var logger = collector.CreateLogger("Category");

        logger.LogInformation("Config: a.json");
        logger.LogDebug("details");
        logger.LogWarning("  Kept without their dependency on it: {Dependents}", "App");
        logger.LogError(DiagnosticEvents.NameCollision, "Several projects are named {Name}", "Dup");

        Assert.Equal(
            [
                new Diagnostic("unspecified", "warning", "Kept without their dependency on it: App"),
                new Diagnostic("name-collision", "error", "Several projects are named Dup"),
            ],
            collector.Snapshot());
    }
}
