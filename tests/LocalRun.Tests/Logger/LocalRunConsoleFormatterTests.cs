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

    // The state of a templated log call: its values, by name
    private static string FormatTemplate(bool useColors, string message, params object[] values)
    {
        var formatter = new LocalRunConsoleFormatter(useColors);
        List<KeyValuePair<string, object?>> state = [.. values.Select((value, i) => new KeyValuePair<string, object?>($"Value{i}", value))];
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(LogLevel.Information, "Category", default, state, null, (_, _) => message);
        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);
        return writer.ToString();
    }

    [Fact]
    public void AbsolutePaths_BecomeLinks_OnATerminal()
    {
        string config = Path.GetFullPath(Path.Combine("my repos", "localrun.config.json"));
        string uri = new Uri(config).AbsoluteUri;

        Assert.Equal($"Written to \x1B]8;;{uri}\x1B\\{config}\x1B]8;;\x1B\\ for A{Environment.NewLine}",
            FormatTemplate(useColors: true, $"Written to {config} for A", config, "A"));
        Assert.Contains("%20", uri);
    }

    [Fact]
    public void Paths_StayPlain_WhenRedirected()
    {
        string config = Path.GetFullPath("localrun.config.json");

        Assert.Equal($"Written to {config}{Environment.NewLine}", FormatTemplate(useColors: false, $"Written to {config}", config));
    }

    [Fact]
    public void APathInsideALongerOne_IsLeftToIt()
    {
        string root = Path.GetFullPath("repos");
        string config = Path.Combine(root, "localrun.config.json");

        string output = FormatTemplate(useColors: true, $"{config} from {root}", config, root);

        Assert.Equal(1, output.Split("\x1B]8;;file").Length - 1);
    }

    [Fact]
    public void EmptyMessageWithoutException_WritesNothing()
    {
        Assert.Equal(string.Empty, Format(LogLevel.Information, string.Empty));
    }
}
