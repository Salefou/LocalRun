namespace LocalRun.Core.Processing;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// The running <c>up</c>, as it records itself in <see cref="FileName"/> next to its config while it runs (deleted when it
/// exits): <c>down</c> finds it there to stop it, <c>status</c> shows it, and a second <c>up</c> is refused. A file left
/// behind (<c>up</c> killed) is ignored: its process is gone, or is another one now (<see cref="Started"/> differs).
/// </summary>
/// <param name="Pid">The <c>lrun up</c> process.</param>
/// <param name="Started">When that process started.</param>
/// <param name="StopTimeout">Its grace period in seconds: how long a stop may take before the kills.</param>
/// <param name="Services">The executables started so far, with their <c>dotnet run</c> process.</param>
public sealed record UpState(int Pid, DateTimeOffset Started, string Config, string Logs, int StopTimeout, IReadOnlyList<UpStateService> Services)
{
    public const string FileName = "up.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The state file of the <c>up</c> started with this config: in the config's folder.</summary>
    public static string PathFor(string configPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, FileName);

    /// <summary>The named event <c>down</c> sets to stop the <c>up</c> of this process id (Windows; per logon session).</summary>
    public static string StopEventName(int pid) => $@"Local\lrun-up-stop-{pid}";

    /// <summary>The <c>up</c> recorded in this file while its process still runs; null when there is none.</summary>
    public static UpState? ReadRunning(string path)
    {
        UpState? state;
        try
        {
            state = JsonSerializer.Deserialize<UpState>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // missing, or left half-written by a killed up
        }
        return state != null && IsRunning(state.Pid, state.Started) ? state : null;
    }

    /// <summary>The current process's start time, as <see cref="Started"/> records it.</summary>
    public static DateTimeOffset CurrentProcessStarted()
    {
        using var current = Process.GetCurrentProcess();
        return new DateTimeOffset(current.StartTime).ToUniversalTime();
    }

    private static bool IsRunning(int pid, DateTimeOffset started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            // A process id is reused once its process is gone
            return !process.HasExited && (new DateTimeOffset(process.StartTime) - started).Duration() < TimeSpan.FromSeconds(1);
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true; // can't be read (another user's): when unsure, take it as running
        }
    }

    /// <summary>Writes the file whole, so that a reader never sees it half-written.</summary>
    public void Write(string path)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Deletes the file if it is still this <c>up</c>'s.</summary>
    public void Delete(string path)
    {
        try
        {
            var recorded = JsonSerializer.Deserialize<UpState>(File.ReadAllText(path), JsonOptions);
            if (recorded?.Pid == Pid)
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Gone already, or not ours: left for the next up to ignore
        }
    }
}

/// <param name="Pid">The <c>dotnet run</c> process.</param>
/// <param name="Log">The file its output goes to.</param>
public sealed record UpStateService(string Project, int Pid, string? Log);
