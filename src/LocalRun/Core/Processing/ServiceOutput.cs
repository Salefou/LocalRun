namespace LocalRun.Core.Processing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public enum OutputLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

/// <summary>What `up` makes of the lines a service prints: its level, read from the text, without the colors it brought.</summary>
internal static partial class ServiceOutput
{
    // CSI sequences (colors, cursor) and OSC ones (hyperlinks, titles)
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)")]
    private static partial Regex AnsiSequence();

    // Microsoft.Extensions.Logging console ("fail:", "warn:", ...) and Serilog-like bracketed levels ("[12:00:01 WRN]")
    [GeneratedRegex(@"^\s*(fail|crit|error|fatal):|\[[^\]]*\b(ERR|ERROR|FTL|FATAL)\]|^Unhandled exception", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"^\s*warn(ing)?:|\[[^\]]*\b(WRN|WARN|WARNING)\]", RegexOptions.IgnoreCase)]
    private static partial Regex WarningLine();

    [GeneratedRegex(@"^\s*(dbug|debug|trce|trace):|\[[^\]]*\b(DBG|DEBUG|VRB|TRC|VERBOSE)\]", RegexOptions.IgnoreCase)]
    private static partial Regex DebugLine();

    [GeneratedRegex(@"^\s*info:|\[[^\]]*\b(INF|INFO)\]", RegexOptions.IgnoreCase)]
    private static partial Regex InformationLine();

    public static string StripAnsi(string line) => AnsiSequence().Replace(line, string.Empty);

    /// <summary>The level a line announces, or null when it doesn't (a message continuation, a stack frame, plain text).</summary>
    public static OutputLevel? DetectLevel(string line)
    {
        if (ErrorLine().IsMatch(line)) return OutputLevel.Error;
        if (WarningLine().IsMatch(line)) return OutputLevel.Warning;
        if (DebugLine().IsMatch(line)) return OutputLevel.Debug;
        if (InformationLine().IsMatch(line)) return OutputLevel.Information;
        return null;
    }

    /// <summary>Colors are used on a terminal only, and not when NO_COLOR is set (https://no-color.org).</summary>
    public static bool ColorsEnabled() =>
        !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
}

/// <summary>
/// The colors of a service's output: its `[name]` tag always has the service's hue, and the message is colored by
/// the level of the line (orange for warnings, red for errors), so the two never get mixed up.
/// </summary>
internal sealed class ServiceStyle
{
    private const double Saturation = 0.75;
    private const string ResetForeground = "\x1B[39m";

    private readonly double hue;

    internal ServiceStyle(double hue)
    {
        this.hue = hue;
    }

    /// <summary>
    /// A style per service: the hues are spread evenly around the wheel, in alphabetical order of the names, so
    /// the colors depend only on the set of services. They are shifted half a step so none sits on the red of errors.
    /// </summary>
    public static Dictionary<string, ServiceStyle> Assign(IEnumerable<string> names)
    {
        var sorted = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal).ToList();
        return sorted
            .Select((name, index) => (name, style: new ServiceStyle((index + 0.5) * 360.0 / sorted.Count)))
            .ToDictionary(entry => entry.name, entry => entry.style, StringComparer.Ordinal);
    }

    /// <summary>The tag in the service's color, followed by a space.</summary>
    public string Tag(string name)
    {
        var (r, g, b) = FromHsl(hue, Saturation, 0.62);
        return $"\x1B[38;2;{r};{g};{b}m[{name}]{ResetForeground} ";
    }

    /// <summary>The line colored for its level: orange for warnings, red for errors, dim for debug, as printed otherwise.</summary>
    public static string Message(string line, OutputLevel level) => level switch
    {
        OutputLevel.Error => $"\x1B[38;2;255;85;85m{line}{ResetForeground}",
        OutputLevel.Warning => $"\x1B[38;2;255;165;0m{line}{ResetForeground}",
        OutputLevel.Debug => $"\x1B[90m{line}{ResetForeground}",
        _ => line,
    };

    internal static (int R, int G, int B) FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - chroma / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
