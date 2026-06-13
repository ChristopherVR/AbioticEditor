using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Items;

/// <summary>
/// Which backpack inventory indices are special (refrigerated / freezer / shielded /
/// warmer), keyed by backpack item id. Indices are 0-based into <c>Inventory_</c>.
///
/// The game wires this via <c>DT_ItemCosmetics</c> (row name = item id) ->
/// <c>DataAsset_51_*</c> (ObjectProperty) -> an <c>InventoryData_Parent_C</c> data asset
/// carrying per-type slot-index arrays (see docs/research-backpack-traits.md).
/// <see cref="LoadFrom"/> walks that chain at runtime so backpacks added by future game
/// versions are picked up without an editor change; <see cref="Fallback"/> is the table
/// verified against the current build, used when game assets aren't available.
/// </summary>
public sealed class BackpackSpecialSlotCatalog
{
    private static readonly IReadOnlyDictionary<int, string> NoSlots = new Dictionary<int, string>();

    /// <summary>
    /// Slot-array property name -> editor badge label. Any other <c>*Slots</c> int-array
    /// property on a backpack data asset is treated as a future slot type: its badge is
    /// derived from the property name and it's logged on the UNKWN channel.
    /// </summary>
    private static readonly (string Prefix, string Label)[] KnownSlotKinds =
    {
        ("RefrigeratedSlots", "COLD"),
        ("FreezerSlots", "FREEZER"),
        ("ShieldedSlots", "SHIELDED"),
        ("WarmerSlots", "WARM"),
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _map;

    private BackpackSpecialSlotCatalog(IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> map)
    {
        _map = map;
    }

    /// <summary>Number of backpacks with at least one special slot.</summary>
    public int Count => _map.Count;

    /// <summary>Special-slot labels for a backpack id; empty for ordinary packs.</summary>
    public IReadOnlyDictionary<int, string> For(string? backpackId)
        => backpackId is not null && _map.TryGetValue(backpackId, out var slots) ? slots : NoSlots;

    /// <summary>
    /// The slot table verified against the current game build (the four packs that ship
    /// with special slots). Used when the game install / mappings aren't available.
    /// </summary>
    public static BackpackSpecialSlotCatalog Fallback { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["backpack_large"] = new Dictionary<int, string>
            {
                [0] = "SHIELDED",
                [1] = "COLD",
            },
            ["backpack_research_U1a"] = new Dictionary<int, string>
            {
                [0] = "SHIELDED",
                [1] = "WARM",
                [2] = "SHIELDED",
            },
            ["backpack_research_U1b"] = new Dictionary<int, string>
            {
                [0] = "COLD",
                [1] = "FREEZER",
                [2] = "COLD",
            },
            ["backpack_voidpack_U1b"] = new Dictionary<int, string>
            {
                [1] = "FREEZER",
                [6] = "SHIELDED",
                [8] = "COLD",
                [13] = "WARM",
            },
        });

    /// <summary>
    /// Builds the catalog from the game's own data: every DT_ItemCosmetics row with an
    /// inventory data asset contributes its slot arrays. Returns <see cref="Fallback"/>
    /// when assets can't be read, and merges the fallback in for any pack the dynamic
    /// scan somehow misses, so a partial read never loses known packs.
    /// </summary>
    public static BackpackSpecialSlotCatalog LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Fallback;

        var map = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/DT_ItemCosmetics");
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                foreach (var kv in dt.RowMap)
                {
                    var itemId = kv.Key.Text;
                    if (string.IsNullOrEmpty(itemId)) continue;

                    var assetProp = kv.Value.Properties.FirstOrDefault(
                        p => p.Name.Text.StartsWith("DataAsset", StringComparison.Ordinal));
                    if (assetProp?.Tag?.GenericValue is not FPackageIndex idx || idx.IsNull) continue;

                    var slots = ReadSlotArrays(idx);
                    if (slots.Count > 0)
                    {
                        map[itemId] = slots;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn(
                "Assets", $"Dynamic backpack special-slot scan failed; using the built-in table. {ex.Message}");
            return Fallback;
        }

        if (map.Count == 0)
        {
            // A readable table with zero slot-bearing packs would mean the wiring moved;
            // the built-in table is still the best answer.
            Diagnostics.EditorLog.Warn(
                "Assets", "DT_ItemCosmetics scan found no backpack special slots; using the built-in table.");
            return Fallback;
        }

        // Safety net: never lose a pack the verified table knows about.
        foreach (var known in Fallback._map)
        {
            if (!map.ContainsKey(known.Key))
            {
                Diagnostics.EditorLog.Warn(
                    "Assets", $"Dynamic backpack scan missed '{known.Key}'; restored from the built-in table.");
                map[known.Key] = known.Value;
            }
        }

        Diagnostics.EditorLog.Info(
            "Assets", $"Backpack special slots loaded from game data: {map.Count} pack(s).");
        return new BackpackSpecialSlotCatalog(map);
    }

    /// <summary>
    /// Reads every <c>*Slots</c> int-array property off the row's inventory data asset.
    /// Unknown slot kinds (future game versions) get a badge derived from the property
    /// name and an UNKWN log entry instead of being dropped.
    /// </summary>
    private static Dictionary<int, string> ReadSlotArrays(FPackageIndex assetIndex)
    {
        var result = new Dictionary<int, string>();
        UObject? asset;
        try
        {
            asset = assetIndex.Load();
        }
        catch
        {
            return result;
        }
        if (asset is null) return result;

        foreach (var p in asset.Properties)
        {
            var name = p.Name.Text;
            if (p.Tag?.GenericValue is not UScriptArray arr) continue;

            var label = LabelFor(name);
            if (label is null) continue;

            foreach (var entry in arr.Properties)
            {
                if (entry.GenericValue is int slotIndex && slotIndex >= 0)
                {
                    result[slotIndex] = label;
                }
            }
        }
        return result;
    }

    private static string? LabelFor(string propertyName)
    {
        foreach (var (prefix, label) in KnownSlotKinds)
        {
            if (propertyName.StartsWith(prefix, StringComparison.Ordinal)) return label;
        }

        // Future-proofing: a new slot kind would arrive as another "<Kind>Slots" array.
        var suffixAt = propertyName.IndexOf("Slots", StringComparison.Ordinal);
        if (suffixAt > 0)
        {
            var kind = propertyName[..suffixAt];
            Diagnostics.EditorLog.UnknownData(
                "BackpackSlots", propertyName, "slot kind not in the editor's badge table - newer game version?");
            return kind.ToUpperInvariant();
        }
        return null;
    }
}
