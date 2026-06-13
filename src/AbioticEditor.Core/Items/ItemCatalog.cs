using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Items;

/// <summary>
/// In-memory index of every item in AF's <c>ItemTable_Global</c>. Built once from the
/// user's local install and cached for the session.
/// </summary>
public sealed class ItemCatalog
{
    private readonly IReadOnlyDictionary<string, ItemCatalogEntry> _byId;

    private ItemCatalog(IReadOnlyDictionary<string, ItemCatalogEntry> byId)
    {
        _byId = byId;
    }

    public int Count => _byId.Count;
    public IEnumerable<ItemCatalogEntry> Entries => _byId.Values;

    public ItemCatalogEntry? Find(string? itemId)
        => itemId is not null && _byId.TryGetValue(itemId, out var entry) ? entry : null;

    /// <summary>
    /// Loads the catalog using the supplied provider. Requires usmap mappings - throws
    /// <see cref="GameAssetProvider.MappingsRequiredException"/> otherwise.
    /// </summary>
    public static ItemCatalog LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings)
        {
            throw new GameAssetProvider.MappingsRequiredException("ItemTable_Global");
        }

        var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault()
            ?? throw new InvalidDataException("ItemTable_Global has no UDataTable export.");

        // Case-insensitive: saves carry mixed-case row names (e.g. "PersonalTeleporter"
        // in the table vs lower-cased ids in some save arrays).
        var dict = new Dictionary<string, ItemCatalogEntry>(dt.RowMap.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in dt.RowMap)
        {
            var id = kv.Key.Text;
            if (string.IsNullOrEmpty(id)) continue;
            dict[id] = BuildEntry(id, kv.Value);
        }
        return new ItemCatalog(dict);
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
