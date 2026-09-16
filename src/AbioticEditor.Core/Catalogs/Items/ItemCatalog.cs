using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Items;

/// <summary>
/// In-memory index of every item in AF's item tables. Built once from the user's local
/// install and cached for the session. <c>ItemTable_Global</c> is the required primary;
/// all other <c>ItemTable_*.uasset</c> files in the same directory are merged in as
/// supplemental sources so future DLC tables are picked up without an editor update.
/// </summary>
public sealed class ItemCatalog
{
    private readonly IReadOnlyDictionary<string, ItemCatalogEntry> _byId;
    private readonly IReadOnlyDictionary<string, string> _tableRefs;

    private ItemCatalog(
        IReadOnlyDictionary<string, ItemCatalogEntry> byId,
        IReadOnlyDictionary<string, string> tableRefs)
    {
        _byId = byId;
        _tableRefs = tableRefs;
    }

    public int Count => _byId.Count;
    public IEnumerable<ItemCatalogEntry> Entries => _byId.Values;

    /// <summary>
    /// Item id -> the DataTable object reference its row lives in (see <see cref="ItemTableIndex"/>).
    /// Captured so the catalog can be serialized into the bundled registry and the writers still
    /// resolve the right table when the game isn't installed.
    /// </summary>
    public IReadOnlyDictionary<string, string> TableRefs => _tableRefs;

    public ItemCatalogEntry? Find(string? itemId)
        => itemId is not null && _byId.TryGetValue(itemId, out var entry) ? entry : null;

    /// <summary>
    /// Rebuilds the catalog from a previously-dumped registry (no game install needed) and
    /// repopulates <see cref="ItemTableIndex"/> so the save writers resolve row tables exactly
    /// as they would against a live install. Used by the offline/bundled-registry load path.
    /// </summary>
    public static ItemCatalog FromRegistry(
        IReadOnlyList<ItemCatalogEntry> entries,
        IReadOnlyDictionary<string, string> tableRefs)
    {
        var dict = new Dictionary<string, ItemCatalogEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(e.Id)) dict[e.Id] = e;
        }

        var refs = new Dictionary<string, string>(tableRefs.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in tableRefs) refs[kv.Key] = kv.Value;

        ItemTableIndex.Set(refs);
        return new ItemCatalog(dict, refs);
    }

    private const string ItemsDir = "AbioticFactor/Content/Blueprints/Items/";
    private const string PrimaryTable = "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";
    private const string SalvageTable = "AbioticFactor/Content/Blueprints/DataTables/DT_Salvage";

    /// <summary>
    /// True when <paramref name="assetPath"/> (a mounted pak file path) is named like a
    /// base-game supplemental item table - any <c>ItemTable_*.uasset</c> in the Items directory
    /// other than <c>ItemTable_Global</c>. Kept as a name-shape helper; actual discovery is now
    /// struct-based (see <see cref="DiscoverSupplementalTables"/>), which also catches mod item
    /// tables under their own content root.
    /// </summary>
    public static bool IsSupplementalItemTable(string assetPath)
    {
        if (!assetPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return false;
        if (!assetPath.StartsWith(ItemsDir, StringComparison.OrdinalIgnoreCase)) return false;

        var nameStart = assetPath.LastIndexOf('/') + 1;
        var nameEnd = assetPath.Length - ".uasset".Length;
        if (nameEnd <= nameStart) return false;
        var name = assetPath[nameStart..nameEnd];

        return name.StartsWith("ItemTable_", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "ItemTable_Global", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds item DataTables beyond <c>ItemTable_Global</c> by matching its row struct
    /// (see <see cref="ModTableDiscovery"/>), returning their package paths (no extension).
    /// This picks up both DLC tables added by future patches and mod item tables under any
    /// content root (e.g. <c>MyMod/Content/...</c>).
    /// </summary>
    public static IReadOnlyList<string> DiscoverSupplementalTables(GameAssetProvider provider)
    {
        var structName = provider.TryLoadDataTable(PrimaryTable)?.RowStructName;
        return ModTableDiscovery.DiscoverTablesByRowStruct(provider, structName, new[] { PrimaryTable });
    }

    /// <summary>
    /// Loads the catalog using the supplied provider. Requires usmap mappings - throws
    /// <see cref="GameAssetProvider.MappingsRequiredException"/> otherwise.
    /// <c>ItemTable_Global</c> is loaded first and its rows win on any conflict with
    /// supplemental tables.
    /// </summary>
    public static ItemCatalog LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings)
            throw new GameAssetProvider.MappingsRequiredException("ItemTable_Global");

        // Primary table - required. Throws if absent or malformed.
        var pkg = provider.LoadPackageInternal(PrimaryTable);
        var primaryDt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault()
            ?? throw new InvalidDataException("ItemTable_Global has no UDataTable export.");

        // Best-effort: resolves SalvageData_ row references into concrete drop lists (see
        // BuildStats). Absent entirely if the table can't be read - salvage stats just don't
        // populate, same graceful-degradation rule as every other optional catalog.
        UDataTable? salvageDt = null;
        try
        {
            salvageDt = provider.LoadPackageInternal(SalvageTable).GetExports().OfType<UDataTable>().FirstOrDefault();
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("ItemCatalog", "Failed to load DT_Salvage; salvage stats will be absent.", ex);
        }

        // Case-insensitive: saves carry mixed-case row names (e.g. "PersonalTeleporter"
        // in the table vs lower-cased ids in some save arrays).
        var dict = new Dictionary<string, ItemCatalogEntry>(primaryDt.RowMap.Count, StringComparer.OrdinalIgnoreCase);
        // id -> the DataTable object ref its row lives in, so the save writers can point an added
        // item's row handle at the table that actually holds it (see ItemTableIndex).
        var tableRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var globalRef = ToTableRef(PrimaryTable);
        foreach (var kv in primaryDt.RowMap)
        {
            var id = kv.Key.Text;
            if (string.IsNullOrEmpty(id)) continue;
            dict[id] = BuildEntry(id, kv.Value, salvageDt);
            tableRefs[id] = globalRef;
        }

        // Supplemental tables - best-effort. Any ItemTable_*.uasset in the same directory
        // that isn't ItemTable_Global is merged in for rows Global doesn't have; rows already
        // in Global keep Global's row data, but their display name/description are relinked
        // to the supplemental copy (see RelinkLocalizedText below).
        var supplemental = DiscoverSupplementalTables(provider);
        Diagnostics.EditorLog.Info(
            "ItemCatalog",
            $"Loaded {dict.Count} rows from ItemTable_Global; merging {supplemental.Count} supplemental table(s).");
        foreach (var tablePath in supplemental)
        {
            try
            {
                var suppPkg = provider.LoadPackageInternal(tablePath);
                var dt = suppPkg.GetExports().OfType<UDataTable>().FirstOrDefault();
                if (dt is null) continue;
                var suppRef = ToTableRef(tablePath);
                var added = 0;
                foreach (var kv in dt.RowMap)
                {
                    var id = kv.Key.Text;
                    if (string.IsNullOrEmpty(id)) continue;

                    if (dict.TryGetValue(id, out var existing))
                    {
                        dict[id] = RelinkLocalizedText(existing, kv.Value);
                        continue;
                    }

                    dict[id] = BuildEntry(id, kv.Value, salvageDt);
                    tableRefs[id] = suppRef;
                    added++;
                }
                if (added > 0)
                    Diagnostics.EditorLog.Info("ItemCatalog", $"  +{added} new row(s) from {tablePath[(tablePath.LastIndexOf('/') + 1)..]}");
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("ItemCatalog", $"Failed to load supplemental item table '{tablePath}'.", ex);
            }
        }

        ItemTableIndex.Set(tableRefs);
        return new ItemCatalog(dict, tableRefs);
    }

    /// <summary>
    /// Converts a mounted package path ("AbioticFactor/Content/Blueprints/Items/ItemTable_X") to
    /// the DataTable object reference a save's row handle stores
    /// ("/Game/Blueprints/Items/ItemTable_X.ItemTable_X").
    /// </summary>
    private static string ToTableRef(string pkgPath)
    {
        const string contentRoot = "AbioticFactor/Content";
        var gamePath = pkgPath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase)
            ? "/Game" + pkgPath[contentRoot.Length..]
            : pkgPath;
        var name = gamePath[(gamePath.LastIndexOf('/') + 1)..];
        return $"{gamePath}.{name}";
    }

    /// <summary>
    /// Rewrites <paramref name="original"/>'s display name/description from the given
    /// supplemental-table row, leaving every other field untouched.
    /// </summary>
    /// <remarks>
    /// <c>ItemTable_Global</c>'s row FText gets re-baked at cook time under a fresh
    /// namespace/key that Abiotic Factor's shipped locres files never cover - confirmed by a
    /// live probe against the installed game: with a non-English culture loaded, 0 of 1545
    /// <c>ItemTable_Global</c> item names differed from their English source string. The
    /// per-category supplemental table this same row was originally authored in (e.g.
    /// <c>ItemTable_Gear</c>, <c>ItemTable_Weapons</c>) keeps the row's real namespace/key, which
    /// the locres does cover. The two copies' <c>ItemName_</c>/<c>ItemDescription_</c> source text
    /// is byte-identical in English, so preferring the supplemental copy is a no-op under English
    /// and a real translation under any other loaded culture.
    /// </remarks>
    private static ItemCatalogEntry RelinkLocalizedText(ItemCatalogEntry original, FStructFallback row)
    {
        var name = ReadText(row, "ItemName_");
        var description = ReadText(row, "ItemDescription_");
        if (name is null && description is null) return original;
        return original with
        {
            DisplayName = name ?? original.DisplayName,
            Description = description ?? original.Description,
        };
    }

    private static ItemCatalogEntry BuildEntry(string id, FStructFallback row, UDataTable? salvageTable)
    {
        var isWeapon = ReadBool(row, "IsWeapon_");
        return new ItemCatalogEntry(
            Id: id,
            DisplayName: ReadText(row, "ItemName_") ?? id,
            Description: ReadText(row, "ItemDescription_"),
            IconAssetPath: ReadSoftObjectPath(row, "InventoryIcon_"),
            StackSize: ReadInt(row, "StackSize_", 1),
            MaxDurability: ReadFloat(row, "MaxItemDurability_"),
            IsWeapon: isWeapon,
            Weight: ReadFloat(row, "Weight_"),
            Tags: ReadGameplayTags(row, "GameplayTags_"),
            ContainerCapacity: ReadNestedInt(row, "EquipmentData_", "ContainerCapacity_"),
            EquipSlot: ReadNestedEnumInt(row, "EquipmentData_", "EquipSlot_"),
            MaxLiquid: ReadNestedInt(row, "LiquidData_", "MaxLiquid_"),
            AllowedLiquids: ReadNestedEnumArray(row, "LiquidData_", "AllowedLiquids_"))
        {
            Stats = BuildStats(row, isWeapon, salvageTable),
        };
    }

    // ---------- wiki-style stat block (see ItemStats.cs) ----------

    /// <summary>
    /// Builds the stat block for one row, gating every group on the same rule: a group only
    /// appears when the row's data actually says something, because every item carries the full
    /// set of nested structs (<c>WeaponData_</c>, <c>EquipmentData_</c>, <c>ConsumableData_</c>,
    /// ...) whether or not it uses them - e.g. a can of food still has a placeholder
    /// <c>WeaponData_</c> with 10 damage and a 0.5s swing.
    /// </summary>
    private static ItemStats? BuildStats(FStructFallback row, bool isWeapon, UDataTable? salvageTable)
    {
        var weapon = isWeapon ? BuildWeaponStats(row) : null;
        var armor = BuildArmorStats(row);
        var consumable = BuildConsumableStats(row);
        var repair = BuildRepairInfo(row);
        var salvage = BuildSalvageInfo(row, salvageTable);

        if (weapon is null && armor is null && consumable is null && repair is null && salvage is null)
            return null;
        return new ItemStats(weapon, armor, consumable, repair, salvage);
    }

    private static WeaponStats? BuildWeaponStats(FStructFallback row)
    {
        GetByPrefix(row, "WeaponData_", out var tag);
        if (!TryGetNestedStruct(tag, out var data)) return null;

        return new WeaponStats(
            IsMelee: ReadInnerBool(data, "Melee_"),
            DamagePerHit: ReadInnerFloat(data, "DamagePerHit_"),
            TimeBetweenAttacks: ReadInnerFloat(data, "TimeBetweenShots_"),
            MagazineSize: (int)ReadInnerFloat(data, "MagazineSize_"),
            RequireAmmo: ReadInnerBool(data, "RequireAmmo_"),
            DamageType: ReadInnerClassShortName(data, "DamageType_Hitscan_"));
    }

    private static ArmorStats? BuildArmorStats(FStructFallback row)
    {
        GetByPrefix(row, "EquipmentData_", out var tag);
        if (!TryGetNestedStruct(tag, out var data)) return null;

        var armorBonus = ReadInnerFloat(data, "ArmorBonus_");
        var heatResist = ReadInnerFloat(data, "HeatResist_");
        var coldResist = ReadInnerFloat(data, "ColdResist_");
        var setBonus = ReadInnerRowName(data, "SetBonus_");
        if (armorBonus == 0 && heatResist == 0 && coldResist == 0 && setBonus is null) return null;

        return new ArmorStats(armorBonus, heatResist, coldResist, setBonus);
    }

    private static ConsumableStats? BuildConsumableStats(FStructFallback row)
    {
        GetByPrefix(row, "ConsumableData_", out var tag);
        if (!TryGetNestedStruct(tag, out var data)) return null;

        var hunger = ReadInnerFloat(data, "HungerFill_");
        var thirst = ReadInnerFloat(data, "ThirstFill_");
        var fatigue = ReadInnerFloat(data, "FatigueFill_");
        var sanity = ReadInnerFloat(data, "SanityFill_");
        var buffs = ReadInnerStringArray(data, "BuffsToAdd_");
        if (hunger == 0 && thirst == 0 && fatigue == 0 && sanity == 0 && buffs.Count == 0) return null;

        return new ConsumableStats(hunger, thirst, fatigue, sanity, buffs);
    }

    private static RepairInfo? BuildRepairInfo(FStructFallback row)
    {
        GetByPrefix(row, "RepairItem_", out var tag);
        if (tag?.Tag?.GenericValue is not { } raw) return null;
        var data = Unwrap(raw);
        if (data is not FStructFallback repair) return null;

        var itemId = ReadInnerRowNameOf(repair, "ItemDataTable_");
        if (string.IsNullOrEmpty(itemId)) return null;
        var min = (int)ReadInnerFloat(repair, "QuantityMin_");
        var max = (int)ReadInnerFloat(repair, "QuantityMax_");
        return new RepairInfo(itemId, min, max <= 0 ? min : max);
    }

    private static SalvageInfo? BuildSalvageInfo(FStructFallback row, UDataTable? salvageTable)
    {
        if (salvageTable is null) return null;
        GetByPrefix(row, "SalvageData_", out var tag);
        if (tag?.Tag?.GenericValue is not { } raw) return null;
        var salvageRowName = ReadRowNameOf(Unwrap(raw) as FStructFallback);
        if (string.IsNullOrEmpty(salvageRowName)) return null;

        var salvageRow = salvageTable.RowMap.FirstOrDefault(
            kv => string.Equals(kv.Key.Text, salvageRowName, StringComparison.OrdinalIgnoreCase)).Value;
        if (salvageRow is null) return null;

        GetByPrefix(salvageRow, "SalvageDropItems_", out var dropsTag);
        if (dropsTag?.Tag?.GenericValue is not CUE4Parse.UE4.Assets.Objects.UScriptArray array) return null;

        var drops = new List<SalvageDrop>(array.Properties.Count);
        foreach (var element in array.Properties)
        {
            if (Unwrap(element.GenericValue) is not FStructFallback drop) continue;
            var itemId = ReadInnerRowNameOf(drop, "ItemDataTable_");
            if (string.IsNullOrEmpty(itemId)) continue;
            var min = (int)ReadInnerFloat(drop, "QuantityMin_");
            var max = (int)ReadInnerFloat(drop, "QuantityMax_");
            var chance = ReadInnerFloat(drop, "ChanceToDrop_");
            drops.Add(new SalvageDrop(itemId, min, max <= 0 ? min : max, chance <= 0 ? 1 : chance));
        }
        return drops.Count > 0 ? new SalvageInfo(drops) : null;
    }

    /// <summary>Unwraps an <c>FScriptStruct</c> box to its inner <c>FStructFallback</c>, if any.</summary>
    private static object? Unwrap(object? value)
        => value is CUE4Parse.UE4.Assets.Objects.FScriptStruct scriptStruct ? scriptStruct.StructType : value;

    private static bool TryGetNestedStruct(FPropertyTag? tag, out FStructFallback data)
    {
        var v = Unwrap(tag?.Tag?.GenericValue);
        if (v is FStructFallback inner) { data = inner; return true; }
        data = null!;
        return false;
    }

    private static double ReadInnerFloat(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            return p.Tag?.GenericValue switch { float f => f, double d => d, int i => i, uint u => u, _ => 0 };
        }
        return 0;
    }

    private static bool ReadInnerBool(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            return p.Tag?.GenericValue is bool b && b;
        }
        return false;
    }

    /// <summary>Reads a nested <c>{ RowName: ... }</c> struct field, returning null for "None".</summary>
    private static string? ReadInnerRowName(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            return ReadRowNameOf(Unwrap(p.Tag?.GenericValue) as FStructFallback);
        }
        return null;
    }

    /// <summary>
    /// Reads a nested <c>{ DataTable: ..., RowName: ... }</c> DataTable-row-handle field (the
    /// shape <c>RepairItem_</c>/<c>SalvageDropItems_</c> elements use for their item reference).
    /// </summary>
    private static string? ReadInnerRowNameOf(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            return ReadRowNameOf(Unwrap(p.Tag?.GenericValue) as FStructFallback);
        }
        return null;
    }

    private static string? ReadRowNameOf(FStructFallback? handle)
    {
        if (handle is null) return null;
        var rowName = handle.Properties.FirstOrDefault(
            p => p.Name.Text.Equals("RowName", StringComparison.OrdinalIgnoreCase))
            ?.Tag?.GenericValue?.ToString();
        return string.IsNullOrEmpty(rowName) || string.Equals(rowName, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : rowName;
    }

    private static IReadOnlyList<string> ReadInnerStringArray(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            if (p.Tag?.GenericValue is not CUE4Parse.UE4.Assets.Objects.UScriptArray array) return Array.Empty<string>();
            var result = new List<string>(array.Properties.Count);
            foreach (var element in array.Properties)
            {
                var s = element.GenericValue?.ToString();
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            return result;
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// The short asset name of a damage-type class reference, e.g.
    /// <c>BlueprintGeneratedClass'/Game/.../DamageType_Blunt_HEAVY.DamageType_Blunt_HEAVY_C'</c>
    /// becomes <c>"Blunt_HEAVY"</c>. Null for the generic base damage type or an empty reference.
    /// </summary>
    private static string? ReadInnerClassShortName(FStructFallback data, string innerPrefix)
    {
        foreach (var p in data.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            var s = p.Tag?.GenericValue?.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            var dot = s.LastIndexOf('.');
            var name = dot >= 0 ? s[(dot + 1)..] : s;
            name = name.TrimEnd('\'');
            if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
            // "Abiotic_DamageType_ParentBP" is the field's placeholder default on a non-weapon
            // row; real weapon rows always point at a specific DamageType_* class.
            if (string.Equals(name, "Abiotic_DamageType_ParentBP", StringComparison.Ordinal)) return null;
            return name.StartsWith("DamageType_", StringComparison.Ordinal) ? name["DamageType_".Length..] : name;
        }
        return null;
    }

    /// <summary>Array of byte-enum values inside a nested struct (LiquidData_ -> AllowedLiquids_).</summary>
    private static List<int>? ReadNestedEnumArray(FStructFallback row, string outerPrefix, string innerPrefix)
    {
        GetByPrefix(row, outerPrefix, out var tag);
        var v = tag?.Tag?.GenericValue;
        if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
        if (v is not FStructFallback inner) return null;

        foreach (var p in inner.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;
            if (p.Tag?.GenericValue is not CUE4Parse.UE4.Assets.Objects.UScriptArray arr) return null;

            var result = new List<int>(arr.Properties.Count);
            foreach (var el in arr.Properties)
            {
                var n = ParseEnumeratorNumber(el.GenericValue);
                if (n >= 0) result.Add(n);
            }
            return result;
        }
        return null;
    }

    /// <summary>"E_LiquidType::NewEnumerator8" -> 8; raw byte/int pass through; else -1.</summary>
    private static int ParseEnumeratorNumber(object? raw)
    {
        switch (raw)
        {
            case byte b: return b;
            case int i: return i;
        }
        var s = raw?.ToString();
        if (string.IsNullOrEmpty(s)) return -1;
        var end = s.Length;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(s[start - 1])) start--;
        return start < end && int.TryParse(s[start..end], out var n) ? n : -1;
    }

    /// <summary>
    /// Byte-enum column inside a nested struct column (e.g. EquipmentData_ -> EquipSlot_).
    /// With usmap mappings the value renders as <c>E_InventorySlotType::NewEnumerator14</c>
    /// - the trailing integer is the enumerator number. Absent column -> 0.
    /// </summary>
    private static int ReadNestedEnumInt(FStructFallback row, string outerPrefix, string innerPrefix)
    {
        GetByPrefix(row, outerPrefix, out var tag);
        var v = tag?.Tag?.GenericValue;
        if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
        if (v is not FStructFallback inner) return 0;

        foreach (var p in inner.Properties)
        {
            if (!p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal)) continue;

            var raw = p.Tag?.GenericValue;
            switch (raw)
            {
                case byte b: return b;
                case int i: return i;
            }

            var s = raw?.ToString();
            if (string.IsNullOrEmpty(s)) return 0;

            // "E_InventorySlotType::NewEnumerator14" -> 14 (parse the trailing digits).
            var end = s.Length;
            var start = end;
            while (start > 0 && char.IsAsciiDigit(s[start - 1])) start--;
            return start < end && int.TryParse(s[start..end], out var n) ? n : 0;
        }
        return 0;
    }

    /// <summary>Int column inside a nested struct column (e.g. EquipmentData_ -> ContainerCapacity_).</summary>
    private static int ReadNestedInt(FStructFallback row, string outerPrefix, string innerPrefix)
    {
        GetByPrefix(row, outerPrefix, out var tag);
        var v = tag?.Tag?.GenericValue;
        if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
        if (v is not FStructFallback inner) return 0;

        foreach (var p in inner.Properties)
        {
            if (p.Name.Text.StartsWith(innerPrefix, StringComparison.Ordinal))
            {
                return p.Tag?.GenericValue switch { int i => i, byte b => b, long l => (int)l, _ => 0 };
            }
        }
        return 0;
    }

    // ---------- property extractors ----------

    private static string? GetByPrefix(FStructFallback row, string prefix, out FPropertyTag? tag)
    {
        foreach (var p in row.Properties)
        {
            if (p.Name.Text is { } n && n.StartsWith(prefix, StringComparison.Ordinal))
            {
                tag = p;
                return n;
            }
        }
        tag = null;
        return null;
    }

    private static string? ReadText(FStructFallback row, string prefix)
    {
        GetByPrefix(row, prefix, out var tag);
        if (tag?.Tag?.GenericValue is not { } raw) return null;
        return raw.ToString();
    }

    private static int ReadInt(FStructFallback row, string prefix, int defaultValue = 0)
    {
        GetByPrefix(row, prefix, out var tag);
        return tag?.Tag?.GenericValue switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            _ => defaultValue,
        };
    }

    private static double ReadFloat(FStructFallback row, string prefix)
    {
        GetByPrefix(row, prefix, out var tag);
        return tag?.Tag?.GenericValue switch
        {
            float f => f,
            double d => d,
            int i => i,
            _ => 0,
        };
    }

    private static bool ReadBool(FStructFallback row, string prefix)
    {
        GetByPrefix(row, prefix, out var tag);
        return tag?.Tag?.GenericValue is bool b && b;
    }

    private static string? ReadSoftObjectPath(FStructFallback row, string prefix)
    {
        GetByPrefix(row, prefix, out var tag);
        // The probe showed `InventoryIcon` rendering as a plain "/Game/..." string via
        // ToString(). Be permissive: accept whatever .ToString() yields and strip the
        // duplicate ".AssetName" suffix UE puts on object paths.
        var s = tag?.Tag?.GenericValue?.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        return s;
    }

    private static readonly char[] TagDelimiters = { '|', ',' };

    private static string[] ReadGameplayTags(FStructFallback row, string prefix)
    {
        GetByPrefix(row, prefix, out var tag);
        // CUE4Parse renders FGameplayTagContainer as "Tag.A | Tag.B (FGameplayTagContainer)";
        // strip the trailing struct-type annotation then split on the pipe delimiter.
        var s = tag?.Tag?.GenericValue?.ToString();
        if (string.IsNullOrEmpty(s)) return Array.Empty<string>();

        // Drop the "(FGameplayTagContainer)" or any other parenthesised type tail.
        var paren = s.LastIndexOf('(');
        if (paren > 0) s = s[..paren];

        // CUE4Parse renders the container comma-separated (older builds used pipes).
        // Splitting on one delimiter only would collapse all tags into a single string,
        // making every tag check match just the FIRST tag (shields carry Item.Material.*
        // first and Item.Gear.Shield.* second, so they never validated).
        return s.Split(TagDelimiters, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
