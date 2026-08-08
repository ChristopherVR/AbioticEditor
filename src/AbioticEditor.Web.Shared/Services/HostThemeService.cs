namespace AbioticEditor.Web.Services;

/// <summary>Persists the Razor host colour preference without requiring a platform UI toolkit.</summary>
public sealed class HostThemeService
{
    // Through HostPreferenceStore rather than straight to these files: a browser tab's file
    // system does not survive a reload, and choosing a display language reloads the page - so
    // the theme would have reset itself every time the language changed.
    private const string ConfigFileName = "webtheme.txt";
    private const string AccentConfigFileName = "webaccent.txt";

    public event EventHandler? Changed;

    public HostTheme Current { get; private set; } = ReadSavedTheme();
    public HostThemeAccent Accent { get; private set; } = ReadSavedAccent();

    public void SetTheme(string? value)
    {
        var theme = Parse(value);
        if (theme == Current) return;

        HostPreferenceStore.Write(HostPreferenceStore.Keys.Theme, ConfigFileName, theme.ToString().ToLowerInvariant());
        Current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAccent(string? value)
    {
        var accent = ParseAccent(value);
        if (accent == Accent) return;

        HostPreferenceStore.Write(HostPreferenceStore.Keys.Accent, AccentConfigFileName, accent.ToString().ToLowerInvariant());
        Accent = accent;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string CssClass => $"{ThemeCssClass} accent-{Accent.ToString().ToLowerInvariant()}";

    private string ThemeCssClass => Current switch
    {
        HostTheme.Light => "theme-light",
        HostTheme.Dark => "theme-dark",
        _ => "theme-system",
    };

    private static HostTheme ReadSavedTheme()
        => HostPreferenceStore.Read(HostPreferenceStore.Keys.Theme, ConfigFileName) is { } saved
            ? Parse(saved)
            : HostTheme.Dark;

    private static HostTheme Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => HostTheme.Light,
        "dark" => HostTheme.Dark,
        _ => HostTheme.System,
    };

    private static HostThemeAccent ReadSavedAccent()
    {
        // Cascade (the game-accurate blue-teal facility palette) is the default, matching the
        // native app's ThemeService. Hazard is the legacy alternate and remains available.
        return HostPreferenceStore.Read(HostPreferenceStore.Keys.Accent, AccentConfigFileName) is { } saved
            ? ParseAccent(saved)
            : HostThemeAccent.Cascade;
    }

    private static HostThemeAccent ParseAccent(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "hazard" => HostThemeAccent.Hazard,
        _ => HostThemeAccent.Cascade,
    };
}

public enum HostTheme { System, Dark, Light }
public enum HostThemeAccent { Cascade, Hazard }
