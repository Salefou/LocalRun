namespace LocalRun.Tests.Processing;

using LocalRun.Core.Processing;

public class ServiceOutputTests
{
    [Theory]
    [InlineData("fail: Orders.Gateway[0]", OutputLevel.Error)]
    [InlineData("crit: Orders.Gateway[0]", OutputLevel.Error)]
    [InlineData("[12:00:01 ERR] Connection refused", OutputLevel.Error)]
    [InlineData("[12:00:01 FTL] Gone", OutputLevel.Error)]
    [InlineData("Unhandled exception. System.InvalidOperationException: boom", OutputLevel.Error)]
    [InlineData("warn: Orders.Gateway[0]", OutputLevel.Warning)]
    [InlineData("[12:00:01 WRN] Slow", OutputLevel.Warning)]
    [InlineData("dbug: Orders.Gateway[0]", OutputLevel.Debug)]
    [InlineData("[12:00:01 DBG] Tick", OutputLevel.Debug)]
    [InlineData("[12:00:01 VRB] Tick", OutputLevel.Debug)]
    [InlineData("info: Orders.Gateway[0]", OutputLevel.Information)]
    [InlineData("[12:00:01 INF] Started", OutputLevel.Information)]
    public void DetectLevel_reads_the_level_a_line_announces(string line, OutputLevel expected)
    {
        Assert.Equal(expected, ServiceOutput.DetectLevel(line));
    }

    [Theory]
    [InlineData("      Listening on http://localhost:5101/")]
    [InlineData("   at Orders.Gateway.Program.Main()")]
    [InlineData("Error handling is on")]
    [InlineData("")]
    public void DetectLevel_is_null_for_lines_without_a_level(string line)
    {
        Assert.Null(ServiceOutput.DetectLevel(line));
    }

    [Fact]
    public void StripAnsi_removes_colors_and_hyperlinks_but_keeps_the_text()
    {
        string line = "\x1B[32minfo\x1B[39m: see \x1B]8;;file:///c:/a.txt\x1B\\c:\\a.txt\x1B]8;;\x1B\\";

        Assert.Equal("info: see c:\\a.txt", ServiceOutput.StripAnsi(line));
    }

    [Fact]
    public void Assign_spreads_the_hues_in_alphabetical_order_whatever_the_input_order()
    {
        var first = ServiceStyle.Assign(["Risk", "Batch", "Gateway", "Reports"]);
        var second = ServiceStyle.Assign(["Reports", "Gateway", "Batch", "Risk"]);

        foreach (var name in first.Keys)
        {
            Assert.Equal(first[name].Tag(name, OutputLevel.Information), second[name].Tag(name, OutputLevel.Information));
        }
        var tags = new[] { "Batch", "Gateway", "Reports", "Risk" }.Select(name => first[name].Tag("x", OutputLevel.Information)).ToList();
        Assert.Equal(4, tags.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(240, 0, 0, 255)]
    public void FromHsl_converts_the_primary_hues(double hue, int r, int g, int b)
    {
        Assert.Equal((r, g, b), ServiceStyle.FromHsl(hue, 1, 0.5));
    }

    [Fact]
    public void Tag_shades_the_service_hue_by_level_and_keeps_errors_red()
    {
        var style = ServiceStyle.Assign(["Gateway", "Risk"])["Risk"];

        string debug = style.Tag("Risk", OutputLevel.Debug);
        string info = style.Tag("Risk", OutputLevel.Information);
        string warning = style.Tag("Risk", OutputLevel.Warning);

        Assert.Equal(3, new[] { debug, info, warning }.Distinct().Count());
        Assert.Equal(style.Tag("Risk", OutputLevel.Error), ServiceStyle.Assign(["Batch", "Risk"])["Batch"].Tag("Risk", OutputLevel.Error));
        Assert.EndsWith("[Risk]\x1B[39m ", info);
    }
}
