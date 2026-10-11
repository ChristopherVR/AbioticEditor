using System.Diagnostics;
using System.Runtime.Versioning;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Ties a launched child process to this editor's own lifetime, so it is closed when the editor
/// is. On Windows the child is placed in a kill-on-close job object (see
/// <see cref="ChildProcessJob"/>): the operating system ends it when the editor exits, however
/// that happens. Elsewhere it is ended on a normal exit only.
/// </summary>
internal static class ChildProcessLifetime
{
    private static readonly object Sync = new();

    // Deliberately never disposed: the handle must stay open for the editor's whole life, and the
    // OS closing it at exit is what ends the children.
    private static ChildProcessJob? _job;

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
            _job ??= ChildProcessJob.TryCreate();
            return _job?.TryAdd(child) ?? false;
        }
    }
}
