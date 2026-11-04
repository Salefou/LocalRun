namespace LocalRun.Tests.Logger;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class LocalRunConsoleFormatterTests
{
    private static string Format(LogLevel level, string message, EventId eventId = default, Exception? exception = null,
        bool onTerminal = false, bool useColors = false)
    {
        var formatter = new LocalRunConsoleFormatter(onTerminal, useColors);
        var entry = new LogEntry<string>(level, "Category", eventId, message, exception, (state, _) => state);
        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);
        return writer.ToString();
    }

    [Fact]
    public void Information_IsPrintedWithoutPrefixOrCategory()
    {
        Assert.Equal($"Config: a.json{Environment.NewLine}", Format(LogLevel.Information, "Config: a.json"));
    }

    [Theory]
    [InlineData(LogLevel.Debug, "[DBG] ")]
    [InlineData(LogLevel.Warning, "[WRN] ")]
    [InlineData(LogLevel.Error, "[ERR] ")]
    [InlineData(LogLevel.Critical, "[ERR] ")]
    public void OtherLevels_GetAPrefix_WithoutColors(LogLevel level, string prefix)
    {
        Assert.Equal($"{prefix}message{Environment.NewLine}", Format(level, "message"));
        Assert.Equal($"{prefix}message{Environment.NewLine}", Format(level, "message", onTerminal: true));
    }

    [Fact]
    public void Colors_ReplaceThePrefix_AndColorTheWholeLine()
    {
        Assert.Equal($"\x1B[33mcareful\x1B[39m\x1B[22m{Environment.NewLine}",
            Format(LogLevel.Warning, "careful", onTerminal: true, useColors: true));
        Assert.Equal($"plain{Environment.NewLine}", Format(LogLevel.Information, "plain", onTerminal: true, useColors: true));
    }

    public static TheoryData<int, string, string> Marks => new()
    {
        { LogEvents.Start.Id, "›", ">" },
        { LogEvents.Done.Id, "✓", "ok" },
        { LogEvents.Failed.Id, "×", "FAILED" },
        { LogEvents.Launched.Id, "▸", "+" },
        { LogEvents.Stopped.Id, "■", "-" },
    };

    [Theory]
    [MemberData(nameof(Marks))]
    public void Marks_AreGlyphsOnATerminal_AndAsciiWhenRedirected(int id, string glyph, string ascii)
    {
        var eventId = new EventId(id);

        Assert.Equal($"{glyph} step{Environment.NewLine}", Format(LogLevel.Information, "step", eventId, onTerminal: true));
        Assert.Equal($"{ascii} step{Environment.NewLine}", Format(LogLevel.Information, "step", eventId));
    }

    [Fact]
    public void AMark_TakesThePlaceOfTheLevelPrefix()
    {
        Assert.Equal($"FAILED go failed: boom{Environment.NewLine}", Format(LogLevel.Error, "go failed: boom", LogEvents.Failed));
    }

    [Fact]
    public void AColoredMark_PrecedesTheLine()
    {
        Assert.Equal($"\x1B[32m✓\x1B[39m\x1B[22m Packed 3 projects{Environment.NewLine}",
            Format(LogLevel.Information, "Packed 3 projects", LogEvents.Done, onTerminal: true, useColors: true));
    }

    [Fact]
    public void Progress_IsPrintedOnATerminalOnly()
    {
        Assert.Equal($"Scanning...{Environment.NewLine}", Format(LogLevel.Information, "Scanning...", LogEvents.Progress, onTerminal: true));
        Assert.Equal(string.Empty, Format(LogLevel.Information, "Scanning...", LogEvents.Progress));
    }

    [Fact]
    public void Exception_IsPrintedAfterTheMessage()
    {
        var output = Format(LogLevel.Error, "failed", exception: new InvalidOperationException("boom"));

        Assert.StartsWith($"[ERR] failed{Environment.NewLine}", output);
        Assert.Contains("System.InvalidOperationException: boom", output);
    }

    [Fact]
    public void Paths_AreNeverLinks()
    {
        string config = Path.GetFullPath("localrun.config.json");

        Assert.Equal($"Config: {config}{Environment.NewLine}",
            Format(LogLevel.Information, $"Config: {config}", onTerminal: true, useColors: true));
    }

    [Fact]
    public void EmptyMessageWithoutException_WritesNothing()
    {
        Assert.Equal(string.Empty, Format(LogLevel.Information, string.Empty));
    }
}
