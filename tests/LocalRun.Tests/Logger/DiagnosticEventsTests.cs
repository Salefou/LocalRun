namespace LocalRun.Tests.Logger;

using LocalRun.Cli;
using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System.Reflection;

public class DiagnosticEventsTests
{
    private static List<EventId> All => [.. typeof(DiagnosticEvents).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(EventId))
        .Select(field => (EventId)field.GetValue(null)!)];

    [Fact]
    public void Codes_AreUniqueKebabCase_AndApartFromTheMarks()
    {
        Assert.Equal(All.Count, All.Select(code => code.Id).Distinct().Count());
        Assert.Equal(All.Count, All.Select(code => code.Name).Distinct().Count());
        Assert.All(All, code =>
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", code.Name);
            Assert.True(code.Id >= 100);
        });
    }

    [Fact]
    public void StrictCodes_AreCodes()
    {
        Assert.Subset(All.Select(code => code.Name!).ToHashSet(), DiagnosticEvents.Strict.ToHashSet());
        Assert.DoesNotContain(DiagnosticEvents.EmptySelection.Name!, DiagnosticEvents.Strict);
    }

    [Fact]
    public void ACodelessLog_IsUnspecified()
    {
        Assert.Equal("unspecified", DiagnosticEvents.CodeOf(default));
        Assert.Equal("unspecified", DiagnosticEvents.CodeOf(LogEvents.Failed));
        Assert.Equal("cycle-removed", DiagnosticEvents.CodeOf(DiagnosticEvents.CycleRemoved));
    }

    [Fact]
    public void CheckStrict_CountsEachCode_AndIgnoresTheOthers()
    {
        Assert.Equal((LocalRunCli.ExitSuccess, null),
            LocalRunCli.CheckStrict([new("no-solution", "warning", "a"), new("empty-selection", "warning", "b")]));

        var (exitCode, error) = LocalRunCli.CheckStrict(
        [
            new("cycle-removed", "warning", "a"),
            new("kept-without-dependency", "warning", "b"),
            new("cycle-removed", "warning", "c"),
            new("no-solution", "warning", "d"),
        ]);

        Assert.Equal(LocalRunCli.ExitStrict, exitCode);
        Assert.Equal("strict", error!.Code);
        Assert.EndsWith("cycle-removed (2), kept-without-dependency.", error.Message);
    }
}
