using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One appearance choice from a <c>ScientistCustomization_&lt;slot&gt;.sav</c> file.
/// </summary>
/// <param name="PropertyName">Exact save property name, e.g. <c>Customization_Head</c>
/// (note <c>customization_beard</c> is lowercase in the save - match case-insensitively).</param>
/// <param name="Label">Human label for the editor, e.g. "Head".</param>
/// <param name="TableName">The DataTable the value is a row of, e.g. <c>DT_Customization_Head</c>.</param>
/// <param name="CurrentValue">The chosen row name, e.g. <c>Head_M01a</c>.</param>
public sealed record CustomizationField(
    string PropertyName,
    string Label,
    string TableName,
    string CurrentValue);

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

        var fields = new List<CustomizationField>(KnownFields.Count);
        foreach (var (propertyName, label, tableName) in KnownFields)
        {
            var tag = FindByName(save.Properties, propertyName);
            if (tag?.Property?.Value is null) continue;
            fields.Add(new CustomizationField(
                // Preserve the actual on-disk casing so Save() round-trips exactly.
                tag.Name.Value, label, tableName, tag.Property.Value.ToString() ?? string.Empty));
        }
        return new CustomizationSaveFile(path, fields);
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
        Diagnostics.EditorLog.Info("Customization", $"Writing {FilePath} (+ .bak backup, {newValues.Count} field(s))");
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

            Saves.SaveBackup.CreateFor(FilePath);
            using var output = File.Create(FilePath);
            save.WriteTo(output);
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

/// <summary>One row of a <c>DT_Customization_*</c> table.</summary>
/// <param name="RowName">The row key stored in the save, e.g. <c>Head_M01a</c>.</param>
/// <param name="DisplayName">In-game label, e.g. "Hubert" - falls back to the row name.</param>
/// <param name="IconAssetPath">2D preview texture (CustomizationIcons/icon_*), when the row has a real one.</param>
/// <param name="ColorHex">Swatch color hex for color rows (HairColor/ShirtColor ColorA).</param>
public sealed record CustomizationOption(
    string RowName,
    string DisplayName,
    string? IconAssetPath = null,
    string? ColorHex = null)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Loads the row vocabulary of every customization DataTable so editor dropdowns can
/// offer all valid choices for each <see cref="CustomizationField"/>.
/// </summary>
public static class CustomizationCatalog
{
    /// <summary>
    /// Maps table name (<c>DT_Customization_Head</c> ...) -> its options. Tables that fail
    /// to load are omitted; without usmap mappings the result is empty.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>> LoadFrom(GameAssetProvider provider)
    {
        var result = new Dictionary<string, IReadOnlyList<CustomizationOption>>(StringComparer.OrdinalIgnoreCase);
        if (!provider.HasMappings) return result;

        foreach (var tableName in CustomizationSaveFile.KnownFields.Select(f => f.TableName).Distinct())
        {
            try
            {
                var pkg = provider.LoadPackageInternal(
                    $"AbioticFactor/Content/Blueprints/DataTables/Customization/{tableName}");
                var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault();
                if (dt is null) continue;

                var options = new List<CustomizationOption>(dt.RowMap.Count);
                foreach (var kv in dt.RowMap)
                {
                    // Columns are hash-suffixed (DisplayName_63_*, Icon_46_*, ColorA_38_*)
                    // - match by prefix.
                    string? display = null, icon = null, colorHex = null;
                    foreach (var p in kv.Value.Properties)
                    {
                        var n = p.Name.Text;
                        if (n.StartsWith("DisplayName", StringComparison.Ordinal))
                        {
                            display = p.Tag?.GenericValue?.ToString();
                        }
                        else if (n.StartsWith("Icon", StringComparison.Ordinal))
                        {
                            var s = p.Tag?.GenericValue?.ToString();
                            // The engine's WhiteSquareTexture placeholder is not a usable preview.
                            if (!string.IsNullOrEmpty(s) && !s.Contains("WhiteSquare", StringComparison.OrdinalIgnoreCase))
                            {
                                icon = s;
                            }
                        }
                        else if (n.StartsWith("ColorA", StringComparison.Ordinal))
                        {
                            colorHex = p.Tag?.GenericValue switch
                            {
                                CUE4Parse.UE4.Objects.Core.Math.FLinearColor lc => lc.Hex,
                                { } v => v.ToString(),
                                _ => null,
                            };
                        }
                    }
                    options.Add(new CustomizationOption(
                        kv.Key.Text,
                        string.IsNullOrWhiteSpace(display) ? kv.Key.Text : display!,
                        icon,
                        colorHex));
                }
                result[tableName] = options;
            }
            catch
            {
                // Table missing or unreadable in this game version - skip it.
            }
        }
        return result;
    }
}
