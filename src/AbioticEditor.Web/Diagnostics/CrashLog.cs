using AbioticEditor.Core.Diagnostics;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Diagnostics;

/// <summary>
/// Catches the failures that reach nobody else and writes them to the editor's log file.
///
/// <para>A desktop app that has no console has nowhere to print a crash. Before this, an
/// exception nothing caught took the process down with no window, no message and no log entry -
/// from the player's side the editor simply vanished. These handlers do not make the app
/// survive anything; they make sure the reason is on disk afterwards.</para>
/// </summary>
public static class CrashLog
{
    /// <summary>
    /// Installs the process-wide handlers. Safe to call more than once - only the first call
    /// subscribes, so a test host or a second entry point cannot double-write every crash.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // ExceptionObject is typed as object because the CLR allows throwing non-Exceptions
            // from other languages; ToString still gives us something to record.
            var error = args.ExceptionObject as Exception;
            var detail = args.IsTerminating ? "Unhandled exception (terminating)" : "Unhandled exception";
            EditorLog.Error("Crash", error is null ? $"{detail}: {args.ExceptionObject}" : detail, error);
        };

        // A faulted Task whose exception is never observed. These do not take the process down
        // on modern .NET, so they are the ones that silently corrupt a session: a save that
        // never finished writing, a catalog that never loaded, and no sign of why.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // Round 118: once a circuit is gone (a window closed, a lost connection, or the crash
            // this whole file exists to catch further up the stack), every JS interop call that
            // was still in flight when it died throws JSDisconnectedException with nobody left to
            // observe it - a live report showed a run of these logged as "Crash" right alongside
            // the actual failure that killed the circuit, which buried the one entry worth
            // reading under noise that just meant "yes, and then it disconnected, as expected".
            // Only silence a fault that is ENTIRELY disconnect noise (AggregateException can carry
            // more than one inner exception) - anything else in the same faulted Task still gets
            // recorded exactly as before.
            if (IsOnlyDisconnectNoise(args.Exception))
            {
                args.SetObserved();
                return;
            }
            EditorLog.Error("Crash", "Unobserved task exception", args.Exception);
            // Marking it observed only stops the (already non-fatal) escalation policy; the
            // failure is on disk either way.
            args.SetObserved();
        };
    }

    /// <summary>True when every exception a faulted, unobserved task carries is exactly the kind
    /// a JS interop call throws once its circuit is already gone - see the remarks above.</summary>
    private static bool IsOnlyDisconnectNoise(AggregateException exception)
    {
        var flattened = exception.Flatten().InnerExceptions;
        return flattened.Count > 0 && flattened.All(inner => inner is JSDisconnectedException);
    }

    private static int _installed;
}
