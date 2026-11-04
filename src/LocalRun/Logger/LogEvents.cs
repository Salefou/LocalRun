namespace LocalRun.Logger;

using Microsoft.Extensions.Logging;

/// <summary>
/// The marks of LocalRun's output, given as the event id of a log call: each means one thing, and
/// <see cref="LocalRunConsoleFormatter"/> prints it as a glyph on a terminal (without an emoji variant, so widths stay
/// even) or as an ASCII word when output is redirected. Messages never carry a mark themselves.
/// </summary>
public static class LogEvents
{
    /// <summary><c>›</c> / <c>&gt;</c>: a step about to run.</summary>
    public static readonly EventId Start = new(1, "start");

    /// <summary><c>✓</c> / <c>ok</c>: a step done.</summary>
    public static readonly EventId Done = new(2, "done");

    /// <summary><c>×</c> / <c>FAILED</c>: a step or the command failed.</summary>
    public static readonly EventId Failed = new(3, "failed");

    /// <summary><c>▸</c> / <c>+</c>: a process started, running until it stops (<see cref="Stopped"/>).</summary>
    public static readonly EventId Launched = new(6, "launched");

    /// <summary><c>■</c> / <c>-</c>: a process stopped.</summary>
    public static readonly EventId Stopped = new(4, "stopped");

    /// <summary>A slow step going on: printed on a terminal only, unmarked.</summary>
    public static readonly EventId Progress = new(5, "progress");
}
