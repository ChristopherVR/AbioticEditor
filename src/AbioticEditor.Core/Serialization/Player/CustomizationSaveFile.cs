using UeSaveGame;
using UeSaveGame.PropertyTypes;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Reader/writer for the per-Steam-account character appearance file:
/// <c>%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\&lt;steamid64&gt;\ScientistCustomization_&lt;slot&gt;.sav</c>
/// (save class <c>Abiotic_CustomizationSave_C</c>). Appearance is <em>not</em> stored in
/// the per-world <c>Player_*.sav</c> - see docs/research-customization.md. The file's
/// top-level properties are flat, un-hashed NameProperty values, each a row name into a
/// <c>DT_Customization_*</c> DataTable.
/// </summary>
public sealed class CustomizationSaveFile
{
    /// <summary>
    /// The 13 known appearance properties: save property -> editor label -> DataTable.
    /// Two pak tables have no save property in this file version
    /// (DT_Customization_Labcoats, DT_Customization_FannyPacks, DT_Customization_Makeup).
    /// </summary>
    public static IReadOnlyList<(string PropertyName, string Label, string TableName)> KnownFields { get; } = new[]
    {
        ("Customization_Head",          "Head",           "DT_Customization_Head"),
        ("Customization_HeadAccessory", "Head Accessory", "DT_Customization_HeadAccessory"),
        ("Customization_Wristwatch",    "Wristwatch",     "DT_Customization_Watch"),
        ("Customization_Tie",           "Tie",            "DT_Customization_Tie"),
        ("Customization_UpperBody",     "Upper Body",     "DT_Customization_UpperBody"),
        ("Customization_LowerBody",     "Lower Body",     "DT_Customization_LowerBody"),
        ("Customization_HairStyle",     "Hair Style",     "DT_Customization_HairStyle"),
        ("Customization_HairColor",     "Hair Color",     "DT_Customization_HairColor"),
        ("Customization_ShirtColor",    "Shirt Color",    "DT_Customization_ShirtColor"),
        ("Customization_Shoes",         "Shoes",          "DT_Customization_Shoes"),
        ("Customization_Belt",          "Belt",           "DT_Customization_Belt"),
        ("customization_beard",         "Beard",          "DT_Customization_Beards"),
        ("Customization_IDCard",        "ID Card",        "DT_Customization_IDCard"),
    };

    private CustomizationSaveFile(string filePath, IReadOnlyList<CustomizationField> fields)
    {
        FilePath = filePath;
        Fields = fields;
    }

    /// <summary>
    /// Parses a customization save from raw GVAS bytes - used for Game Pass profile blobs
    /// (<c>ProfileScientistCustomization_&lt;n&gt;</c>), which are uncompressed GVAS and do
    /// not live at a file path. <see cref="FilePath"/> is empty; use
    /// <see cref="ApplyChanges"/> to re-serialize back to bytes for writing.
    /// </summary>
    public static CustomizationSaveFile LoadFromBytes(byte[] data)
    {
        AbioticSaveClasses.EnsureLoaded();
        using var ms = new MemoryStream(data);
        var save = SaveGame.LoadFrom(ms);
        return new CustomizationSaveFile(string.Empty, ParseFields(save));
    }

    /// <summary>
    /// Applies <paramref name="newValues"/> to <paramref name="originalBytes"/> (a raw
    /// customization GVAS blob) and returns the updated bytes. Used by the Game Pass save
    /// path where the result is written back into the wgs container store.
    /// </summary>
    public static byte[] ApplyChanges(byte[] originalBytes, IReadOnlyDictionary<string, string> newValues)
    {
        AbioticSaveClasses.EnsureLoaded();
        SaveGame save;
        using (var ms = new MemoryStream(originalBytes))
            save = SaveGame.LoadFrom(ms);
        foreach (var (propertyName, value) in newValues)
        {
            var tag = FindByName(save.Properties, propertyName);
            if (tag?.Property is null || string.IsNullOrEmpty(value)) continue;
            tag.Property.Value = new FString(value);
        }
        using var outMs = new MemoryStream();
        save.WriteTo(outMs);
        return outMs.ToArray();
    }

    /// <summary>Absolute path of the loaded <c>ScientistCustomization_*.sav</c>.</summary>
    public string FilePath { get; }

    /// <summary>The appearance fields present in the file, in <see cref="KnownFields"/> order.</summary>
    public IReadOnlyList<CustomizationField> Fields { get; }

    /// <summary>The local SaveGames folder for <paramref name="steamId64"/>.</summary>
    private static string AccountDirectory(ulong steamId64) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticFactor", "Saved", "SaveGames",
        steamId64.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Loads the customization file for character <paramref name="slot"/> of the local
    /// Steam account <paramref name="steamId64"/>, or null when the file doesn't exist
    /// (no local install / never played that slot).
    /// </summary>
    public static CustomizationSaveFile? LoadFor(ulong steamId64, int slot = 1)
    {
        var path = Path.Combine(AccountDirectory(steamId64), $"ScientistCustomization_{slot}.sav");
        return File.Exists(path) ? LoadFromFile(path) : null;
    }

    /// <summary>
    /// Lists the character slot numbers that have a <c>ScientistCustomization_&lt;n&gt;.sav</c>
    /// on this machine for <paramref name="steamId64"/>, sorted ascending.
    /// </summary>
    public static IReadOnlyList<int> SlotsFor(ulong steamId64)
    {
        var dir = AccountDirectory(steamId64);
        if (!Directory.Exists(dir)) return Array.Empty<int>();

        var slots = new List<int>();
        foreach (var file in Directory.EnumerateFiles(dir, "ScientistCustomization_*.sav"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var suffix = stem["ScientistCustomization_".Length..];
            if (int.TryParse(suffix, out var slot)) slots.Add(slot);
        }
        slots.Sort();
        return slots;
    }

    /// <summary>Loads a customization save from an explicit path.</summary>
    public static CustomizationSaveFile LoadFromFile(string path)
    {
        AbioticSaveClasses.EnsureLoaded();
        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        return new CustomizationSaveFile(path, ParseFields(save));
    }

    private static List<CustomizationField> ParseFields(SaveGame save)
    {
        var fields = new List<CustomizationField>(KnownFields.Count);
        foreach (var (propertyName, label, tableName) in KnownFields)
        {
            var tag = FindByName(save.Properties, propertyName);
            if (tag?.Property?.Value is null) continue;
            fields.Add(new CustomizationField(
                // Preserve the actual on-disk casing so Save() round-trips exactly.
                tag.Name.Value, label, tableName, tag.Property.Value.ToString() ?? string.Empty));
        }
        return fields;
    }

    /// <summary>
    /// Writes new row-name values back to <see cref="FilePath"/>. The GVAS tree is
    /// re-loaded from disk so only the requested NameProperty values change; everything
    /// else round-trips byte-perfect. Keys of <paramref name="newValues"/> are property
    /// names (case-insensitive); unknown keys are ignored. The previous file content is
    /// preserved as <c>.bak</c> via <see cref="Saves.SaveBackup"/>.
    /// </summary>
    public void Save(IReadOnlyDictionary<string, string> newValues)
    {
        // The account (SteamID64) is the name of the folder the customization file lives in.
        var account = Path.GetFileName(Path.GetDirectoryName(FilePath));
        Diagnostics.EditorLog.Info("Customization",
            $"Writing appearance for account {account} - {Path.GetFileName(FilePath)} (+ .bak backup, {newValues.Count} field(s))");
        try
        {
            AbioticSaveClasses.EnsureLoaded();
            SaveGame save;
            using (var fs = File.OpenRead(FilePath))
            {
                save = SaveGame.LoadFrom(fs);
            }

            foreach (var (propertyName, value) in newValues)
            {
                var tag = FindByName(save.Properties, propertyName);
                if (tag?.Property is null || string.IsNullOrEmpty(value)) continue;
                // NameProperty derives from StrProperty: the value is an FString.
                tag.Property.Value = new FString(value);
            }

            Saves.SaveBackup.WriteWithBackup(FilePath, save.WriteTo);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("Customization", $"Failed to write {FilePath}", ex);
            throw;
        }
    }

    private static FPropertyTag? FindByName(IEnumerable<FPropertyTag>? tags, string name)
    {
        if (tags is null) return null;
        foreach (var t in tags)
        {
            // Case-insensitive: the game writes "customization_beard" in lowercase.
            if (t.Name?.Value is { } n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return null;
    }
}
