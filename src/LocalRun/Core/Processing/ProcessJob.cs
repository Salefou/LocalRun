namespace LocalRun.Core.Processing;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// A Windows job object that kills its processes when it is closed. LocalRun holds the only handle to it, and Windows
/// closes that handle when LocalRun's process ends, however it ends: the started processes are stopped even when
/// LocalRun is killed from outside (Task Manager, a debugger stopping it), when no code of ours can run.
/// Processes started by a process of the job join it automatically (e.g. the app `dotnet run` launches).
/// A process is added just after it starts: anything it launched in those few microseconds would escape the job.
/// </summary>
internal sealed class ProcessJob : IDisposable
{
    private static readonly ILogger<ProcessJob> logger = Log.For<ProcessJob>();

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private readonly SafeJobHandle handle;

    private ProcessJob(SafeJobHandle handle)
    {
        this.handle = handle;
    }

    /// <summary>A new job, or null when job objects aren't available (not Windows, or the system refused).</summary>
    public static ProcessJob? TryCreate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            logger.LogWarning("Could not create a job object ({Error}): processes will outlive LocalRun if it is killed",
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
            handle.Dispose();
            return null;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, (uint)ExtendedLimitInformationSize))
        {
            logger.LogWarning("Could not configure the job object ({Error}): processes will outlive LocalRun if it is killed",
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
            handle.Dispose();
            return null;
        }
        return new ProcessJob(handle);
    }

    /// <summary>Adds a process to the job. On failure it only warns: the process still runs, without the guarantee.</summary>
    public void Add(Process process, string name)
    {
        try
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                logger.LogWarning("Could not add {Project} to the job object ({Error}): it will outlive LocalRun if LocalRun is killed",
                    name, new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited already
        }
    }

    /// <summary>
    /// Size of the limits struct as passed to Windows, which rejects any other: 144 bytes in a 64-bit process
    /// (x64, ARM64), 112 in a 32-bit one. The pointer-sized fields and their padding follow the architecture.
    /// </summary>
    internal static int ExtendedLimitInformationSize => Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();

    /// <summary>Kills the processes still in the job.</summary>
    public void Dispose() => handle.Dispose();

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
