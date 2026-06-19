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

    /// <summary>
    /// True when <paramref name="assetPath"/> (a mounted pak file path) is an item
    /// DataTable we don't load by name yet - i.e. any <c>ItemTable_*.uasset</c> in the
    /// Items directory other than <c>ItemTable_Global</c>. Used so future DLC tables that
    /// ship alongside the main pak are picked up automatically.
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
    /// Scans the mounted asset list for <c>ItemTable_*.uasset</c> files other than
    /// <c>ItemTable_Global</c> and returns their package paths (no extension). Used to
    /// pick up DLC item tables added by future game patches without an editor update.
    /// </summary>
    public static IReadOnlyList<string> DiscoverSupplementalTables(GameAssetProvider provider)
    {
        var result = new List<string>();
        try
        {
            foreach (var assetPath in provider.AssetPaths)
            {
                if (IsSupplementalItemTable(assetPath))
                    result.Add(assetPath[..^".uasset".Length]);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("ItemCatalog", "Item table discovery scan failed.", ex);
        }
        return result;
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
            dict[id] = BuildEntry(id, kv.Value);
            tableRefs[id] = globalRef;
        }

        // Supplemental tables - best-effort. Any ItemTable_*.uasset in the same directory
        // that isn't ItemTable_Global is merged in; rows already in Global are skipped so
        // Global always wins on conflict.
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
                    if (string.IsNullOrEmpty(id) || dict.ContainsKey(id)) continue;
                    dict[id] = BuildEntry(id, kv.Value);
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

    private static ItemCatalogEntry BuildEntry(string id, FStructFallback row)
    {
        return new ItemCatalogEntry(
            Id: id,
            DisplayName: ReadText(row, "ItemName_") ?? id,
            Description: ReadText(row, "ItemDescription_"),
            IconAssetPath: ReadSoftObjectPath(row, "InventoryIcon_"),
            StackSize: ReadInt(row, "StackSize_", 1),
            MaxDurability: ReadFloat(row, "MaxItemDurability_"),
            IsWeapon: ReadBool(row, "IsWeapon_"),
            Weight: ReadFloat(row, "Weight_"),
            Tags: ReadGameplayTags(row, "GameplayTags_"),
            ContainerCapacity: ReadNestedInt(row, "EquipmentData_", "ContainerCapacity_"),
            EquipSlot: ReadNestedEnumInt(row, "EquipmentData_", "EquipSlot_"),
            MaxLiquid: ReadNestedInt(row, "LiquidData_", "MaxLiquid_"),
            AllowedLiquids: ReadNestedEnumArray(row, "LiquidData_", "AllowedLiquids_"));
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
