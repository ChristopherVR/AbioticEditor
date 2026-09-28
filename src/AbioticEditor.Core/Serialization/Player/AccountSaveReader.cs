using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Read-only typed readers for the per-account files next to the worlds folder:
/// <c>Unlocks.sav</c>, <c>PlayerStatsSave.sav</c> and <c>UserSettings.sav</c>. Nothing here
/// writes; the files are still preserved byte-exact by the existing copy/convert paths.
/// Password-like host settings are never returned. See docs/reference/research/research-account-saves.md.
/// </summary>
public static class AccountSaveReader
{
    private static readonly string[] SecretFragments = ["password", "secret", "token", "passphrase"];

    public static CustomizationUnlocksModel ReadUnlocks(byte[] gvas)
    {
        var save = Load(gvas);
        var rows = NameArray(save.Properties, "CustomizationUnlocks")
            .Select(n => new CustomizationUnlock(n, CategoryOf(n)))
            .ToList();
        return new CustomizationUnlocksModel(rows);
    }

    public static CustomizationUnlocksModel ReadUnlocksFile(string path) => ReadUnlocks(File.ReadAllBytes(path));

    public static PlayerStatsModel ReadPlayerStats(byte[] gvas)
    {
        var save = Load(gvas);
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        if (save.Properties.FindByPrefix("Stats_Int")?.Property is MapProperty { Value: { } entries })
        {
            foreach (var kv in entries)
            {
                if (kv.Key?.Value?.ToString() is { } key && kv.Value?.Value is int v) stats[key] = v;
            }
        }
        return new PlayerStatsModel(stats, NameArray(save.Properties, "Achievements"));
    }

    public static PlayerStatsModel ReadPlayerStatsFile(string path) => ReadPlayerStats(File.ReadAllBytes(path));

    public static UserSettingsModel ReadUserSettings(byte[] gvas)
    {
        var save = Load(gvas);
        IList<FPropertyTag> props = save.Properties ?? [];
        string[] known =
        [
            "FavouriteRecipesList", "PinnedRecipeList", "HasCreatedACharacter", "HasPlayedTutorial",
            "UIPopupsSeen", "TutorialHintPopupsSeen", "TutorialPanelsSeen", "HostPreferences", "RecentServers",
        ];
        var unmodeled = props.Select(p => p.Name?.Value ?? "")
            .Where(n => n.Length > 0 && !known.Any(k => n.StartsWith(k, StringComparison.Ordinal)))
            .ToList();

        return new UserSettingsModel(
            NameArray(props, "FavouriteRecipesList"),
            NameArray(props, "PinnedRecipeList"),
            props.TryGetBool("HasCreatedACharacter"),
            props.TryGetBool("HasPlayedTutorial"),
            NameArray(props, "UIPopupsSeen"),
            NameArray(props, "TutorialHintPopupsSeen"),
            NameArray(props, "TutorialPanelsSeen"),
            ReadHostPreferences(props),
            NameArray(props, "RecentServers"),
            unmodeled);
    }

    public static UserSettingsModel ReadUserSettingsFile(string path) => ReadUserSettings(File.ReadAllBytes(path));

    private static HostPreferencesModel? ReadHostPreferences(IList<FPropertyTag> props)
    {
        if (props.FindByPrefix("HostPreferences")?.Property is not StructProperty sp) return null;
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var hasPassword = false;
        if (sp.Value is PropertiesStruct ps)
        {
            foreach (var leaf in ps.Properties)
            {
                var name = leaf.Name?.Value ?? "";
                var value = leaf.Property?.Value;
                if (SecretFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                {
                    // Only presence is reported; the text is never copied out of the raw tree.
                    hasPassword |= value?.ToString() is { Length: > 0 };
                }
                else if (value is bool b)
                {
                    flags[TrimHash(name)] = b;
                }
            }
        }
        return new HostPreferencesModel(flags, hasPassword);
    }

    /// <summary>Blueprint names look like <c>SinglePlayer_1_2433A353...</c>; drop the trailing hash and index.</summary>
    internal static string TrimHash(string name)
    {
        var parts = name.Split('_');
        var end = parts.Length;
        if (end > 1 && parts[end - 1].Length >= 16 && parts[end - 1].All(Uri.IsHexDigit)) end--;
        if (end > 1 && parts[end - 1].All(char.IsDigit)) end--;
        return string.Join('_', parts.Take(end));
    }

    /// <summary>Inferred appearance slot from a DataTable row-name prefix. Heuristic, display only.</summary>
    internal static string CategoryOf(string row)
    {
        (string Prefix, string Category)[] map =
        [
            ("Head_", "Head"), ("Glasses_", "Head Accessory"), ("Watch_", "Wristwatch"), ("Tie_", "Tie"),
            ("UpperBody_", "Upper Body"), ("Pants_", "Lower Body"), ("HairColor_", "Hair Color"),
            ("Hair_", "Hair Style"), ("ShirtColor_", "Shirt Color"), ("Shoes_", "Shoes"), ("Belt_", "Belt"),
            ("Beard_", "Beard"), ("id_", "ID Card"),
        ];
        foreach (var (prefix, category) in map)
        {
            if (row.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return category;
        }
        return "Other";
    }

    private static SaveGame Load(byte[] gvas)
    {
        AbioticSaveClasses.EnsureLoaded();
        using var ms = new MemoryStream(gvas);
        return SaveGame.LoadFrom(ms);
    }

    private static List<string> NameArray(IList<FPropertyTag>? props, string prefix)
    {
        if (props.FindByPrefix(prefix)?.Property is not ArrayProperty { Value: { } items }) return [];
        var list = new List<string>(items.Length);
        foreach (var item in items)
        {
            if (item is FProperty p && p.Value?.ToString() is { } s) list.Add(s);
            else if (item?.ToString() is { } t) list.Add(t);
        }
        return list;
    }
}
