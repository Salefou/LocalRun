namespace LocalRun.Logger;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>Short forms used in messages, so a line stays one line on a big platform.</summary>
public static class Text
{
    /// <summary><c>A, B, C and 47 more</c>: at most <paramref name="shown"/> names.</summary>
    public static string Abbreviate(IEnumerable<string> names, int shown = 8)
    {
        var list = names.ToList();
        return list.Count <= shown
            ? string.Join(", ", list)
            : $"{string.Join(", ", list.Take(shown))} and {list.Count - shown} more";
    }

    /// <summary><c>850 ms</c>, <c>4.2 s</c>, <c>41 s</c>, <c>3 min 05 s</c>.</summary>
    public static string Duration(TimeSpan duration) => duration.TotalSeconds switch
    {
        < 1 => $"{duration.TotalMilliseconds:0} ms",
        < 10 => duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s",
        < 60 => $"{duration.TotalSeconds:0} s",
        _ => $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s",
    };

    /// <summary><c>1 project</c>, <c>12 projects</c>.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
