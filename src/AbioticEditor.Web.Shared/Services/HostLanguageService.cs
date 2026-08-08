using System.Collections;
using System.Globalization;
using System.Resources;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Services;

/// <summary>Stores the desktop app's display-language preference and reads all translated copy from RESX catalogs.</summary>
public sealed class HostLanguageService
{
    private const string DefaultCode = "en";
    private const string HostPrefix = "Host.";
    private const string DetailPrefix = "Detail.";
    private const string EditorPrefix = "Editor.";
    private const string InventoryPrefix = "Inventory.";

    private static readonly IReadOnlyList<string> SupportedCodes = ["en", "es", "fr", "de", "ru"];
    private static readonly IReadOnlyList<string> SupportedGameDataCodes =
        ["en", "de", "es-419", "fr", "ja", "pt-BR", "ru", "zh-Hans", "zh-Hant"];
    private static readonly ResourceManager AppResources = new(
        "AbioticEditor.Web.Localization.AppResources", typeof(HostLanguageService).Assembly);

    private const string ConfigFileName = "weblanguage.txt";

    /// <summary>
    /// Each language's name in its own words, spelled out here rather than looked up.
    /// </summary>
    /// <remarks>
    /// These are the one set of strings that must never be translated - a picker exists so
    /// someone who cannot read the current language can get out of it, and "German" is no help
    /// to a German speaker. Reading them from the translation catalogs also could not work in a
    /// browser: it only downloads the catalog for the language in use, so every other entry fell
    /// back to English and the list read "English" five times over.
    /// </remarks>
    private static readonly Dictionary<string, string> NativeNames = new(StringComparer.Ordinal)
    {
        ["en"] = "English",
        ["es"] = "Español",
        ["fr"] = "Français",
        ["de"] = "Deutsch",
        ["ru"] = "Русский",
    };

    public IReadOnlyList<HostLanguage> Available => SupportedCodes
        .Select(code => new HostLanguage(code, NativeNames.TryGetValue(code, out var name) ? name : code))
        .ToArray();

    public IReadOnlyList<HostLanguage> AvailableGameData => SupportedGameDataCodes
        .Select(code => new HostLanguage(code, ResourceFor(CurrentCode, "GameDataLanguage_" + ResourceCode(code))))
        .ToArray();

    public event EventHandler? Changed;
    public string CurrentCode { get; private set; } = ReadSavedCode() ?? Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    public string OsDefaultCode => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    public string? GameDataLanguage => GameDataLanguageStore.Saved;
    public string EffectiveGameDataLanguage => GameDataLanguage ?? MapEditorToGameData(CurrentCode);

    public string PlatformLabel(SavePlatform platform, DiscoveredWorldSource? source = null)
    {
        if (source == DiscoveredWorldSource.DedicatedServer)
            return Resource("Discovery_Platform_Server");

        return platform switch
        {
            SavePlatform.Steam => Resource("Main_BadgeSteam"),
            SavePlatform.GamePass => Resource("Main_BadgeGamePass"),
            _ => Resource("Main_BadgeUnknown"),
        };
    }

    public void SetLanguage(string? code)
    {
        var selected = Normalize(code);
        HostPreferenceStore.Write(HostPreferenceStore.Keys.Language, ConfigFileName, selected);
        CurrentCode = selected;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetGameDataLanguage(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) GameDataLanguageStore.Clear();
        else GameDataLanguageStore.Save(culture);
    }

    public static string MapEditorToGameData(string? editorCode)
        => Normalize(editorCode) switch
        {
            "es" => "es-419",
            _ => Normalize(editorCode),
        };

    public string Text(string key, params object?[] arguments)
        => Format(TextFor(CurrentCode, key), arguments);

    public string Detail(string key) => DetailFor(CurrentCode, key);

    public string Editor(string key, params object?[] arguments)
        => Format(EditorFor(CurrentCode, key), arguments);

    public string Inventory(string key, params object?[] arguments)
        => Format(InventoryFor(CurrentCode, key), arguments);

    public string Resource(string key, params object?[] arguments)
        => Format(ResourceFor(CurrentCode, key), arguments);

    /// <summary>
    /// Like <see cref="Resource"/> but returns null instead of the raw key when no translation
    /// exists in the current or default language. Use this for catalogs with partial resx
    /// coverage (e.g. skill milestone perk text) where the caller has its own native-language
    /// fallback and showing the raw key would be a visible bug.
    /// </summary>
    public string? ResourceOrNull(string key)
    {
        var contributed = AbioticEditor.Core.Plugins.PluginLocalizations.Lookup(CurrentCode, key);
        return contributed ?? GetResource(key, CurrentCode) ?? GetResource(key, DefaultCode);
    }

    public static string TextFor(string? languageCode, string key)
        => CatalogFor(languageCode, HostPrefix, key);

    public static IReadOnlyCollection<string> HostResourceKeys => ResourceKeys(HostPrefix);

    public static bool HasTextResource(string? languageCode, string key)
        => HasCatalogResource(languageCode, HostPrefix, key);

    public static string DetailFor(string? languageCode, string key)
        => CatalogFor(languageCode, DetailPrefix, key);

    public static string EditorFor(string? languageCode, string key)
        => CatalogFor(languageCode, EditorPrefix, key);

    public static IReadOnlyCollection<string> EditorResourceKeys => ResourceKeys(EditorPrefix);

    public static string InventoryFor(string? languageCode, string key)
        => CatalogFor(languageCode, InventoryPrefix, key);

    public static IReadOnlyCollection<string> InventoryResourceKeys => ResourceKeys(InventoryPrefix);

    public static bool HasInventoryResource(string? languageCode, string key)
        => HasCatalogResource(languageCode, InventoryPrefix, key);

    public static string ResourceFor(string? languageCode, string key)
    {
        var code = Normalize(languageCode);
        var contributed = AbioticEditor.Core.Plugins.PluginLocalizations.Lookup(code, key);
        return contributed ?? GetResource(key, code) ?? GetResource(key, DefaultCode) ?? key;
    }

    private string Format(string format, object?[] arguments)
        => arguments.Length == 0
            ? format
            : string.Format(CultureInfo.GetCultureInfo(CurrentCode), format, arguments);

    private static string CatalogFor(string? languageCode, string prefix, string key)
    {
        var code = Normalize(languageCode);
        var resourceKey = prefix + key;
        return GetResource(resourceKey, code) ?? GetResource(resourceKey, DefaultCode) ?? key;
    }

    private static string? GetResource(string key, string code)
        => AppResources.GetString(key, ResourceCulture(code));

    private static bool HasCatalogResource(string? languageCode, string prefix, string key)
    {
        var resourceSet = AppResources.GetResourceSet(ResourceCulture(Normalize(languageCode)), true, false);
        return resourceSet?.GetString(prefix + key) is not null;
    }

    private static string[] ResourceKeys(string prefix)
    {
        var resourceSet = AppResources.GetResourceSet(CultureInfo.InvariantCulture, true, false)
            ?? throw new MissingManifestResourceException("The default application resource catalog is missing.");
        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static CultureInfo ResourceCulture(string code)
        => code == DefaultCode ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code);

    private static string ResourceCode(string code)
        => code.Replace('-', '_');

    private static string? ReadSavedCode()
        => HostPreferenceStore.Read(HostPreferenceStore.Keys.Language, ConfigFileName) is { } saved
            ? Normalize(saved)
            : null;

    private static string Normalize(string? code)
        => SupportedCodes.Any(language => string.Equals(language, code, StringComparison.OrdinalIgnoreCase))
            ? code!.ToLowerInvariant()
            : DefaultCode;
}

public sealed record HostLanguage(string Code, string NativeName);
