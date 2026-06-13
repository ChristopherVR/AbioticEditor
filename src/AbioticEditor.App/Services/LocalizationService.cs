using System.Globalization;

namespace AbioticEditor.App.Services;

/// <summary>
/// App language policy: the list of shipped languages, the OS-default pick, persistence, and
/// applying a choice to the live <see cref="LocalizationResourceManager"/>. On first run no
/// language is stored, so the editor starts in the OS language (<see cref="OsDefaultCode"/>) and
/// asks the user to confirm/choose (see <c>LanguagePage</c>).
/// </summary>
public static class LocalizationService
{
    private const string PrefKey = "AppLanguage";

    /// <summary>A shipped language: its culture code and the name shown in its own language.</summary>
    public sealed record LanguageOption(string Code, string NativeName);

    /// <summary>Languages the app ships translations for (English is the neutral fallback).</summary>
    public static IReadOnlyList<LanguageOption> Available { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("es", "Español"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("de", "Deutsch"),
    };

    /// <summary>True once the user has explicitly chosen a language (drives the first-run prompt).</summary>
    public static bool HasChosenLanguage => Preferences.Default.ContainsKey(PrefKey);

    /// <summary>The currently-active two-letter language code.</summary>
    public static string CurrentCode => LocalizationResourceManager.Instance.CurrentCulture.TwoLetterISOLanguageName;

    /// <summary>The OS UI language mapped to a shipped code, or English when unsupported.</summary>
    public static string OsDefaultCode
    {
        get
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Available.Any(l => l.Code == two) ? two : "en";
        }
    }

    /// <summary>
    /// Applies the saved language, or - on first run - the OS default. Call once at startup,
    /// before any page resolves its strings. Never throws.
    /// </summary>
    public static void ApplyStartup()
    {
        var saved = Preferences.Default.Get(PrefKey, string.Empty);
        Apply(string.IsNullOrEmpty(saved) ? OsDefaultCode : saved);
    }

    /// <summary>Sets the language, persists it, and re-localizes the open UI.</summary>
    public static void SetLanguage(string code)
    {
        Preferences.Default.Set(PrefKey, code);
        Apply(code);
    }

    private static void Apply(string code)
    {
        try
        {
            LocalizationResourceManager.Instance.CurrentCulture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            LocalizationResourceManager.Instance.CurrentCulture = CultureInfo.GetCultureInfo("en");
        }
    }
}
