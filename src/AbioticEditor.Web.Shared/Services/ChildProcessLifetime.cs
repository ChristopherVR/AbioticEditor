using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Ties a launched child process to this editor's own lifetime, so it is closed when the editor
/// is. On Windows the child is placed in a kill-on-close job object: the operating system ends it
/// when the editor exits, however that happens (normal close, crash, or being killed from Task
/// Manager). Elsewhere it is ended on a normal exit only.
/// </summary>
internal static class ChildProcessLifetime
{
    private static readonly object Sync = new();
    private static IntPtr _job;

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static void BindToCurrentProcess(Process child)
    {
        if (OperatingSystem.IsWindows() && TryAddToJob(child)) return;

        // No job object (non-Windows, or the job could not be set up): end the child when the
        // editor exits normally. The entire tree is killed so a Wine wrapper takes its helper too.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already gone, or not ours to end: nothing more to do on the way out.
            }
        };
    }

    [SupportedOSPlatform("windows")]
    private static bool TryAddToJob(Process child)
    {
        lock (Sync)
        {
            if (_job == IntPtr.Zero)
            {
                var job = CreateJobObjectW(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return false;

                var info = new JobObjectExtendedLimitInformation
                {
                    BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose },
                };
                var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, buffer, (uint)size))
                    {
                        CloseHandle(job);
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                // Deliberately never closed: the handle must stay open for the editor's whole
                // life, and closing it (which the OS does at exit) is what ends the helper.
                _job = job;
            }

            return AssignProcessToJobObject(_job, child.Handle);
        }
    }

    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
