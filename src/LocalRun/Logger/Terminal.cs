namespace LocalRun.Logger;

using System;

/// <summary>
/// Whether someone is watching a terminal. Redirected output (a pipe, a file, CI) gets the plain presentation: no colors,
/// no glyphs, no progress lines. So does JSON mode (<see cref="OutputMode"/>), whose reader is a program even when it runs
/// in a terminal. This is about presentation only, never about the format of the output.
/// </summary>
public static class Terminal
{
    public static bool IsAttached => !OutputMode.Json && !Console.IsOutputRedirected;

    /// <summary>On a terminal only, and not when NO_COLOR is set (https://no-color.org).</summary>
    public static bool ColorsEnabled => IsAttached && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    /// <summary>A change, from one value to another.</summary>
    public static string Arrow => IsAttached ? "→" : "->";
}
