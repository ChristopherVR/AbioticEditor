namespace AbioticEditor.App.Views;

/// <summary>
/// Reveals a save / config file in the OS file manager, selecting it where the platform
/// supports it (Windows Explorer, macOS Finder). The shared part owns the label and the
/// safe entry point; each desktop platform supplies the actual reveal. Android and iOS have
/// no meaningful "show in file manager" flow, so they fall through to a no-op (the partial
/// method simply isn't implemented there).
/// </summary>
public static partial class FileRevealer
{
    /// <summary>OS-appropriate menu label, evaluated once at XAML load via <c>x:Static</c>.</summary>
    public static string RevealLabel =>
#if WINDOWS
        Services.LocalizationResourceManager.Instance["FileReveal_OpenInExplorer"];
#elif MACCATALYST
        Services.LocalizationResourceManager.Instance["FileReveal_OpenInFinder"];
#else
        Services.LocalizationResourceManager.Instance["FileReveal_OpenFileLocation"];
#endif

    /// <summary>
    /// Opens the OS file manager at <paramref name="path"/> (file highlighted where supported).
    /// No-ops on empty paths and on platforms without a file manager; failures are logged
    /// rather than thrown so a context-menu click can never crash the app.
    /// </summary>
    public static void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            PlatformReveal(path);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Warn("App", $"Open in file manager failed for '{path}': {ex.Message}");
        }
    }

    static partial void PlatformReveal(string path);
}
