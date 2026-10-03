using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AbioticEditor.Web.Services;

/// <summary>Sets both Windows icon sizes after the native window exists, including dotnet launches.</summary>
internal sealed class WindowsTaskbarIcon : IDisposable
{
    private nint _small, _large;

    [SupportedOSPlatform("windows")]
    public static void SetAppIdentity() => _ = SetCurrentProcessExplicitAppUserModelID("AbioticEditor.Desktop");

    [SupportedOSPlatform("windows")]
    public void Apply(nint window, string file)
    {
        _small = LoadImage(0, file, 1, 16, 16, 0x10);
        _large = LoadImage(0, file, 1, 32, 32, 0x10);
        if (_small != 0) _ = SendMessage(window, 0x80, 0, _small);
        if (_large != 0) _ = SendMessage(window, 0x80, 1, _large);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_small != 0) _ = DestroyIcon(_small);
        if (_large != 0) _ = DestroyIcon(_large);
        _small = _large = 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
