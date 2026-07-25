using Photino.NET;

namespace AbioticEditor.Web.Services;

/// <summary>Owns the native desktop window that displays the loopback Razor host.</summary>
public sealed class DesktopWindowHost(ILogger<DesktopWindowHost> logger)
{
    private static readonly Action<ILogger, Exception?> DesktopWindowOpening =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, "DesktopWindowOpening"),
            "Opening Abiotic Editor desktop window");

    private static readonly Action<ILogger, string, Exception?> WindowIconMissing =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2, "WindowIconMissing"),
            "Window icon {IconFile} not found beside the executable; keeping the default icon");

    public const string DisableEnvironmentVariable = "ABIOTIC_EDITOR_NO_DESKTOP";

    public static bool ShouldOpen(bool isLinux, bool isUserInteractive, string? disabled, string? displayServer)
        => isUserInteractive
           && !IsDisabled(disabled)
           && (!isLinux || !string.IsNullOrWhiteSpace(displayServer));

    public bool IsEnabled()
    {
        var displayServer = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")
            ?? Environment.GetEnvironmentVariable("DISPLAY");
        return ShouldOpen(
            OperatingSystem.IsLinux(),
            Environment.UserInteractive,
            Environment.GetEnvironmentVariable(DisableEnvironmentVariable),
            displayServer);
    }

    public void Run(string localUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localUrl);

        DesktopWindowOpening(logger, null);
        var window = new PhotinoWindow
        {
            Centered = true,
            LogVerbosity = 0,
        };

        window.SetTitle("Abiotic Editor");
        if (ResolveIconFile() is { } iconFile) window.SetIconFile(iconFile);
        window
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .SetMinSize(960, 640)
            .SetResizable(true)
            .SetContextMenuEnabled(false)
            .SetDevToolsEnabled(false)
            .Load(new Uri(localUrl));

        window.WaitForClose();
    }

    /// <summary>
    /// The window/taskbar icon, copied beside the executable at build time. Photino wants an
    /// .ico on Windows and a .png elsewhere; when the file is absent (an unusual dev layout)
    /// the window simply keeps the default icon instead of failing the whole launch.
    /// </summary>
    private string? ResolveIconFile()
    {
        var name = OperatingSystem.IsWindows() ? "appicon.ico" : "appicon.png";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path)) return path;
        WindowIconMissing(logger, path, null);
        return null;
    }

    private static bool IsDisabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
