namespace LocalRun.Tests.Logger;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class LocalRunConsoleFormatterTests
{
    private static string Format(LogLevel level, string message, Exception? exception = null, bool useColors = false)
    {
        var formatter = new LocalRunConsoleFormatter(useColors);
        var entry = new LogEntry<string>(level, "Category", default, message, exception, (state, _) => state);
        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);
        return writer.ToString();
    }

    [Fact]
    public void Information_IsPrintedWithoutPrefixOrCategory()
    {
        Assert.Equal($"[📦] Packing A{Environment.NewLine}", Format(LogLevel.Information, "[📦] Packing A"));
    }

    [Theory]
    [InlineData(LogLevel.Debug, "[DBG] ")]
    [InlineData(LogLevel.Warning, "[WRN] ")]
    [InlineData(LogLevel.Error, "[ERR] ")]
    [InlineData(LogLevel.Critical, "[ERR] ")]
    public void OtherLevels_GetAPrefix(LogLevel level, string prefix)
    {
        Assert.Equal($"{prefix}message{Environment.NewLine}", Format(level, "message"));
    }

    [Fact]
    public void Exception_IsPrintedAfterTheMessage()
    {
        var output = Format(LogLevel.Error, "failed", new InvalidOperationException("boom"));

        Assert.StartsWith($"[ERR] failed{Environment.NewLine}", output);
        Assert.Contains("System.InvalidOperationException: boom", output);
    }

    [Fact]
    public void Colors_OnlyWrapThePrefix()
    {
        var output = Format(LogLevel.Warning, "careful", useColors: true);

        Assert.Equal($"\x1B[33m[WRN] \x1B[39m\x1B[22mcareful{Environment.NewLine}", output);
    }

    [Fact]
    public void EmptyMessageWithoutException_WritesNothing()
    {
        Assert.Equal(string.Empty, Format(LogLevel.Information, string.Empty));
    }
}
