using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AbioticEditor.Web;

/// <summary>
/// Hides the console window Windows hands a console-subsystem process when it is launched from
/// Explorer, so the editor shows only its own window instead of a black server console beside it.
///
/// <para>Published builds do not reach here with a console to hide: the build marks the finished
/// executable as graphical, so Windows never allocates one and nothing flashes on screen. That
/// cannot be done through <c>WinExe</c>, which would also stop the Web SDK emitting
/// <c>wwwroot/_framework</c> (and therefore <c>blazor.web.js</c>), so the field is rewritten
/// afterwards instead. This remains for the layouts that rewrite does not cover - a plain
/// <c>dotnet build</c> output launched from Explorer - where a console still appears.</para>
///
/// <para>Only the console this process owns is touched, and only when the app owns one that is
/// not being used for anything (a console it inherited from a terminal is left alone, so running
/// the host from a shell or from CI still prints its log).</para>
/// </summary>
internal static class DesktopConsole
{
    public static void HideOwnConsoleWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Hide();
        }
        catch (DllNotFoundException)
        {
            // No console subsystem to talk to: nothing to hide.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Hide()
    {
        var console = GetConsoleWindow();
        if (console == nint.Zero) return;

        // A console shared with a terminal that launched us belongs to that terminal, and its
        // window owner is a different process. Only hide a console this process owns alone.
        _ = GetWindowThreadProcessId(console, out var consoleOwner);
        if (consoleOwner != Environment.ProcessId) return;

        _ = ShowWindow(console, SwHide);
    }

    private const int SwHide = 0;

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);
}
