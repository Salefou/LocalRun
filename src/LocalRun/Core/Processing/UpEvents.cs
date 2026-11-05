namespace LocalRun.Core.Processing;

using System;

/// <summary>Something <c>up</c> did or saw, as it happens: <c>--json</c> streams each one as a line of stdout.</summary>
/// <param name="Event">One of <see cref="UpEvents"/>.</param>
public sealed record UpEvent(string Event)
{
    /// <summary>When it happened (UTC).</summary>
    public DateTimeOffset Time { get; init; }

    public string? Project { get; init; }

    /// <summary><see cref="UpEvents.Started"/>: the process id of <c>dotnet run</c>.</summary>
    public int? Pid { get; init; }

    /// <summary><see cref="UpEvents.Started"/>: the file the service's output goes to.</summary>
    public string? Log { get; init; }

    /// <summary><see cref="UpEvents.Running"/>: how many processes were started.</summary>
    public int? Count { get; init; }

    /// <summary><see cref="UpEvents.Exited"/>: the exit code.</summary>
    public int? ExitCode { get; init; }

    /// <summary><see cref="UpEvents.Exited"/>: the <see cref="ServiceStatus"/>.</summary>
    public string? Status { get; init; }
}

public static class UpEvents
{
    /// <summary>An executable is about to be built (one at a time, dependencies first).</summary>
    public const string Building = "building";

    /// <summary>An executable was started.</summary>
    public const string Started = "started";

    /// <summary>All were started: <c>up</c> now waits until they exit, or until it is stopped.</summary>
    public const string Running = "running";

    /// <summary>Ctrl+C: every process is being stopped.</summary>
    public const string Stopping = "stopping";

    /// <summary>A process exited.</summary>
    public const string Exited = "exited";
}

/// <summary>What became of a service under <c>up</c>.</summary>
public static class ServiceStatus
{
    /// <summary>Never started: <c>up</c> failed before (a build), or it is not started yet.</summary>
    public const string NotStarted = "not-started";

    /// <summary>Started, not exited yet.</summary>
    public const string Running = "running";

    /// <summary>Exited on its own with code 0 (a one-shot job).</summary>
    public const string Exited = "exited";

    /// <summary>Exited on its own with another code: <c>up</c> fails.</summary>
    public const string Failed = "failed";

    /// <summary>Exited after the stop was requested.</summary>
    public const string Stopped = "stopped";

    /// <summary>Still running after the grace period, so killed.</summary>
    public const string Killed = "killed";
}

/// <summary>A service of an <c>up</c> run.</summary>
public sealed class ServiceOutcome
{
    public string Status { get; internal set; } = ServiceStatus.NotStarted;

    public int? ExitCode { get; internal set; }

    /// <summary>The file its output goes to, or null without a logs folder.</summary>
    public string? Log { get; internal init; }
}
