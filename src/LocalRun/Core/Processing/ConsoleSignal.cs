namespace LocalRun.Core.Processing;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

/// <summary>
/// Sends Ctrl+C to processes that each run on a console of their own, as <c>up</c> starts them (<c>CreateNoWindow</c>):
/// only them, never the console <c>up</c> itself runs on, which may be shared with the caller's shell. Windows only.
/// A process can only signal the console it is attached to, and attaching means leaving its own: so <c>up</c> starts
/// itself again as a helper (<see cref="HelperFlag"/>), which attaches to each console in turn.
/// </summary>
internal static class ConsoleSignal
{
    private static readonly ILogger logger = Log.For(typeof(ConsoleSignal).FullName!);

    /// <summary>Not a command: <c>Main</c> hands the arguments here before anything else, and nothing is printed.</summary>
    internal const string HelperFlag = "--internal-send-ctrl-c";

    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(10);

    private const uint CTRL_C_EVENT = 0;

    public static bool IsHelper(string[] args) => args.Length > 0 && args[0] == HelperFlag;

    /// <summary>The helper: sends Ctrl+C to the console of each process id given; exit code 0 if all were reached.</summary>
    public static int RunHelper(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            return 1;
        int failed = 0;
        foreach (string arg in args.Skip(1))
        {
            FreeConsole();
            bool sent = uint.TryParse(arg, out uint pid) && AttachConsole(pid)
                // The helper is on that console now: it must not stop on its own signal
                && SetConsoleCtrlHandler(IntPtr.Zero, true)
                && GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
            if (!sent)
                failed++;
        }
        FreeConsole();
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Starts the helper on these processes and waits for it. On failure it only warns: they will be killed after the grace period.</summary>
    public static async Task SendCtrlCAsync(IReadOnlyCollection<int> pids)
    {
        if (pids.Count == 0)
            return;
        try
        {
            var psi = HelperStartInfo();
            foreach (int pid in pids)
                psi.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var helper = Process.Start(psi)!;
            using var timeout = new CancellationTokenSource(HelperTimeout);
            await helper.WaitForExitAsync(timeout.Token);
            if (helper.ExitCode != 0)
            {
                logger.LogWarning(DiagnosticEvents.StopSignalFailed,
                    "Could not send Ctrl+C to every process: those still running will be killed after the grace period");
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(DiagnosticEvents.StopSignalFailed,
                "Could not send Ctrl+C to the processes ({Error}): they will be killed after the grace period", ex.Message);
        }
    }

    /// <summary>This same program: the <c>lrun</c> executable, or <c>dotnet LocalRun.dll</c> when started that way.</summary>
    private static ProcessStartInfo HelperStartInfo()
    {
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("the path of lrun is unknown");
        // On a console of its own too, which it leaves at once: never the one up runs on
        var psi = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("the path of lrun is unknown"));
        psi.ArgumentList.Add(HelperFlag);
        return psi;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
}
