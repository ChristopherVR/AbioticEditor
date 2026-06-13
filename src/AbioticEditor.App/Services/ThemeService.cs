namespace AbioticEditor.App.Services;

/// <summary>Accent family for the editor chrome.</summary>
public enum ThemeAccent
{
    /// <summary>The amber-CRT hazard-orange look (legacy alternate).</summary>
    Hazard,

    /// <summary>The game-accurate blue-teal facility look (default).</summary>
    Cascade,
}

/// <summary>
/// Applies one of the four palettes (Hazard/Cascade x Dark/Light) by overwriting the
/// Af* color + brush resources at application scope. The shared styles reference these
/// via DynamicResource, so styled properties re-color live; inline StaticResource
/// references and converter output on already-inflated pages do not, which is why
/// callers still recreate the window's page after switching (<see cref="MainPage"/>
/// handles that). Persisted via Preferences and re-applied at startup.
/// </summary>
public static class ThemeService
{
    // V2 key: the default accent flipped to the game-accurate Cascade/facility palette,
    // and a fresh key re-defaults existing installs onto it (they can switch back).
    private const string AccentKey = "ThemeAccentV2";
    private const string ModeKey = "ThemeMode";

    public static ThemeAccent Accent { get; private set; } = ThemeAccent.Cascade;
    public static bool IsLight { get; private set; }

    /// <summary>Reads the persisted choice and applies it. Call before the first page exists.</summary>
    public static void ApplyPersisted()
    {
        var accent = Preferences.Default.Get(AccentKey, nameof(ThemeAccent.Cascade));
        var light = Preferences.Default.Get(ModeKey, "Dark") == "Light";
        Apply(Enum.TryParse<ThemeAccent>(accent, out var a) ? a : ThemeAccent.Cascade, light);
    }

    /// <summary>Applies and persists a palette.</summary>
    public static void Apply(ThemeAccent accent, bool light)
    {
        Accent = accent;
        IsLight = light;
        Preferences.Default.Set(AccentKey, accent.ToString());
        Preferences.Default.Set(ModeKey, light ? "Light" : "Dark");

        var palette = (accent, light) switch
        {
            (ThemeAccent.Hazard, false) => HazardDark,
            (ThemeAccent.Hazard, true) => HazardLight,
            (ThemeAccent.Cascade, false) => CascadeDark,
            (ThemeAccent.Cascade, true) => CascadeLight,
            _ => HazardDark,
        };

        var resources = Application.Current?.Resources;
        if (resources is null) return;

        foreach (var (key, hex) in palette)
        {
            resources[key] = Color.FromArgb(hex);
        }

        // Brushes wrap the colors; refresh them so brush consumers match.
        foreach (var (brushKey, colorKey) in BrushMap)
        {
            resources[brushKey] = new SolidColorBrush((Color)resources[colorKey]);
        }

        Application.Current!.UserAppTheme = light ? AppTheme.Light : AppTheme.Dark;
    }

    private static readonly (string Brush, string Color)[] BrushMap =
    {
        ("AfAmberBrush", "AfAmber"),
        ("AfPageBackgroundBrush", "AfPageBackground"),
        ("AfPanelBackgroundBrush", "AfPanelBackground"),
        ("AfPanelElevatedBrush", "AfPanelElevated"),
        ("AfDividerBrush", "AfDivider"),
        ("AfBorderSubtleBrush", "AfBorderSubtle"),
        ("AfTextPrimaryBrush", "AfTextPrimary"),
        ("AfTextSecondaryBrush", "AfTextSecondary"),
        ("AfAccentOrangeBrush", "AfAccentOrange"),
        ("AfHazardYellowBrush", "AfHazardYellow"),
        ("AfTerminalGreenBrush", "AfTerminalGreen"),
        ("AfAlertRedBrush", "AfAlertRed"),
    };

    // The shipped look: dark facility surfaces, hazard-orange accents.
    private static readonly (string Key, string Hex)[] HazardDark =
    {
        ("AfShellBackground", "#0C0B07"),
        ("AfPageBackground", "#16140E"),
        ("AfPanelBackground", "#201D15"),
        ("AfPanelElevated", "#2A261C"),
        ("AfPanelHover", "#363026"),
        ("AfDivider", "#403724"),
        ("AfBorderSubtle", "#2E2A1F"),
        ("AfTextPrimary", "#EFE4C5"),
        ("AfTextSecondary", "#A89B7F"),
        ("AfTextMuted", "#73694F"),
        ("AfTextOnAccent", "#0C0B07"),
        ("AfAccentOrange", "#F08418"),
        ("AfAccentOrangeDim", "#B05F12"),
        ("AfHazardYellow", "#F5C518"),
        ("AfTerminalGreen", "#8CCB58"),
        ("AfTerminalGreenDim", "#557F37"),
        ("AfAlertRed", "#D14A30"),
        ("AfAmber", "#FFB347"),
    };

    // The game look (default): blue-teal facility chrome lifted from the shipped
    // inventory UI (panes #306481 + #5292B7 outlines, POCKETS header #71C5F6, cyan
    // item names #8CFFFB, unread orange #F89A4F, weight-bar yellow #FFE563).
    private static readonly (string Key, string Hex)[] CascadeDark =
    {
        ("AfShellBackground", "#081119"),
        ("AfPageBackground", "#0C1A24"),
        ("AfPanelBackground", "#132736"),
        ("AfPanelElevated", "#1B3648"),
        ("AfPanelHover", "#26475D"),
        ("AfDivider", "#2E5471"),
        ("AfBorderSubtle", "#224158"),
        ("AfTextPrimary", "#DCEFF9"),
        ("AfTextSecondary", "#8FB8D0"),
        ("AfTextMuted", "#587C93"),
        ("AfTextOnAccent", "#07121A"),
        ("AfAccentOrange", "#F89A4F"),
        ("AfAccentOrangeDim", "#B86F33"),
        ("AfHazardYellow", "#FFE563"),
        ("AfTerminalGreen", "#7FE9E2"),
        ("AfTerminalGreenDim", "#3F837E"),
        ("AfAlertRed", "#E25B4B"),
        ("AfAmber", "#71C5F6"),
    };

    // Light counterparts: paper surfaces, darkened accents for contrast.
    private static readonly (string Key, string Hex)[] HazardLight =
    {
        ("AfShellBackground", "#E8E2D2"),
        ("AfPageBackground", "#F2EDDD"),
        ("AfPanelBackground", "#FBF6E6"),
        ("AfPanelElevated", "#EFE7D2"),
        ("AfPanelHover", "#E5DCC4"),
        ("AfDivider", "#C9BC9C"),
        ("AfBorderSubtle", "#D8CDAF"),
        ("AfTextPrimary", "#2A2417"),
        ("AfTextSecondary", "#5D5440"),
        ("AfTextMuted", "#8A7F63"),
        ("AfTextOnAccent", "#FFF6E2"),
        ("AfAccentOrange", "#C76408"),
        ("AfAccentOrangeDim", "#9C4F07"),
        ("AfHazardYellow", "#A87F04"),
        ("AfTerminalGreen", "#4E8B25"),
        ("AfTerminalGreenDim", "#6FA94F"),
        ("AfAlertRed", "#B5371F"),
        ("AfAmber", "#A86E06"),
    };

    private static readonly (string Key, string Hex)[] CascadeLight =
    {
        ("AfShellBackground", "#DCE6EE"),
        ("AfPageBackground", "#E9F1F7"),
        ("AfPanelBackground", "#F4F9FD"),
        ("AfPanelElevated", "#E7EFF6"),
        ("AfPanelHover", "#D9E6F0"),
        ("AfDivider", "#B5C9D8"),
        ("AfBorderSubtle", "#C8D8E4"),
        ("AfTextPrimary", "#16242F"),
        ("AfTextSecondary", "#3E586C"),
        ("AfTextMuted", "#718999"),
        ("AfTextOnAccent", "#F4FAFF"),
        ("AfAccentOrange", "#1273B8"),
        ("AfAccentOrangeDim", "#0E5A90"),
        ("AfHazardYellow", "#0E80B8"),
        ("AfTerminalGreen", "#1B8C94"),
        ("AfTerminalGreenDim", "#57A6AC"),
        ("AfAlertRed", "#C03A22"),
        ("AfAmber", "#0F86C4"),
    };
}
