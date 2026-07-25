namespace AbioticEditor.Web.Services;

/// <summary>Persists the Razor host colour preference without requiring a platform UI toolkit.</summary>
public sealed class HostThemeService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "webtheme.txt");
    private static readonly string AccentConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "webaccent.txt");

    public event EventHandler? Changed;

    public HostTheme Current { get; private set; } = ReadSavedTheme();
    public HostThemeAccent Accent { get; private set; } = ReadSavedAccent();

    public void SetTheme(string? value)
    {
        var theme = Parse(value);
        if (theme == Current) return;

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, theme.ToString().ToLowerInvariant());
        Current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAccent(string? value)
    {
        var accent = ParseAccent(value);
        if (accent == Accent) return;

        Directory.CreateDirectory(Path.GetDirectoryName(AccentConfigPath)!);
        File.WriteAllText(AccentConfigPath, accent.ToString().ToLowerInvariant());
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
    {
        try { return File.Exists(ConfigPath) ? Parse(File.ReadAllText(ConfigPath).Trim()) : HostTheme.Dark; }
        catch (IOException) { return HostTheme.Dark; }
        catch (UnauthorizedAccessException) { return HostTheme.Dark; }
    }

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
        try { return File.Exists(AccentConfigPath) ? ParseAccent(File.ReadAllText(AccentConfigPath).Trim()) : HostThemeAccent.Cascade; }
        catch (IOException) { return HostThemeAccent.Cascade; }
        catch (UnauthorizedAccessException) { return HostThemeAccent.Cascade; }
    }

    private static HostThemeAccent ParseAccent(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "hazard" => HostThemeAccent.Hazard,
        _ => HostThemeAccent.Cascade,
    };
}

public enum HostTheme { System, Dark, Light }
public enum HostThemeAccent { Cascade, Hazard }
