using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Items;

/// <summary>
/// One upgrade path from <c>DT_ItemUpgrades</c>: applying <see cref="Required"/> at an
/// upgrade bench turns <see cref="SourceId"/> into <see cref="OutputId"/>. Both ids are
/// rows in <c>ItemTable_Global</c>.
/// </summary>
public sealed record ItemUpgrade(
    string SourceId,
    string OutputId,
    IReadOnlyList<RecipeIngredient> Required);

/// <summary>
/// The game's authoritative item upgrade graph (<c>DT_ItemUpgrades</c>): weapons, armor,
/// trinkets, backpacks and tools. Row name = source item; each row lists the required
/// ingredients and the upgraded output item.
/// </summary>
public sealed class ItemUpgradeCatalog
{
    private readonly Dictionary<string, ItemUpgrade> _bySource;
    private readonly Dictionary<string, ItemUpgrade> _byOutput;

    private ItemUpgradeCatalog(IReadOnlyList<ItemUpgrade> upgrades, IEnumerable<string>? knownItemIds = null)
    {
        // The game looks an upgrade up by the held item's row name, so a row naming something
        // that is not an item can never apply in game. DT_ItemUpgrades has shipped one
        // (recipe_makeshift_screwdriver -> handdrill); kept, it made "downgrade" turn a hand drill
        // into that non-item id. Null = no item list to check against, so nothing is dropped.
        var known = knownItemIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) is { Count: > 0 } set ? set : null;
        var bySource = new Dictionary<string, ItemUpgrade>(StringComparer.OrdinalIgnoreCase);
        var byOutput = new Dictionary<string, ItemUpgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in upgrades)
        {
            if (known is not null && !(known.Contains(u.SourceId) && known.Contains(u.OutputId))) continue;
            bySource[u.SourceId] = u;
            // Several items can upgrade into one output; keep the first in table order (the
            // primary table loads before mod overlays) so a downgrade is stable.
            byOutput.TryAdd(u.OutputId, u);
        }
        _bySource = bySource;
        _byOutput = byOutput;
    }

    public static ItemUpgradeCatalog Empty { get; } = new(Array.Empty<ItemUpgrade>());

    /// <summary>
    /// Rebuilds the graph from a previously-dumped registry, with no game install needed.
    /// <paramref name="knownItemIds"/> (the registry's items) drops rows that are not item to item;
    /// older dumps still carry them.
    /// </summary>
    public static ItemUpgradeCatalog FromRegistry(IReadOnlyList<ItemUpgrade> upgrades, IEnumerable<string>? knownItemIds = null)
        => new(upgrades, knownItemIds);

    /// <summary>Every upgrade row, in no particular order. Captured for the bundled registry dump.</summary>
    public IReadOnlyList<ItemUpgrade> Upgrades => _bySource.Values.ToArray();

    public int Count => _bySource.Count;

    /// <summary>The upgrade applied TO this item (next tier), or null when maxed/not upgradable.</summary>
    public ItemUpgrade? UpgradeFor(string? itemId)
        => itemId is not null && _bySource.TryGetValue(itemId, out var u) ? u : null;

    /// <summary>The upgrade that PRODUCED this item (for downgrades), or null for base tiers.</summary>
    public ItemUpgrade? SourceOf(string? itemId)
        => itemId is not null && _byOutput.TryGetValue(itemId, out var u) ? u : null;

    public bool IsUpgradable(string? itemId) => UpgradeFor(itemId) is not null;

    private const string PrimaryTable = "AbioticFactor/Content/Blueprints/DataTables/DT_ItemUpgrades";

    public static ItemUpgradeCatalog LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Empty;

        var upgrades = new List<ItemUpgrade>();
        string? structName = null;
        try
        {
            var primary = provider.TryLoadDataTable(PrimaryTable);
            structName = primary?.RowStructName;
            ParseRows(primary, upgrades);
        }
        catch
        {
            // Missing table -> no upgrade UI; not fatal.
        }

        // Merge mod/patch upgrade tables sharing the row struct, under any content root.
        foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, structName, new[] { PrimaryTable }))
        {
            ParseRows(dt, upgrades);
        }
        return new ItemUpgradeCatalog(upgrades, KnownItemIds(provider));
    }

    // Cached per provider by ItemCatalog, so this shares the read the host already does.
    private static string[]? KnownItemIds(GameAssetProvider provider)
    {
        try { return ItemCatalog.LoadFrom(provider).Entries.Select(entry => entry.Id).ToArray(); }
        catch { return null; }
    }

    private static void ParseRows(UDataTable? dt, List<ItemUpgrade> upgrades)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            var sourceId = kv.Key.Text;
            if (string.IsNullOrEmpty(sourceId)) continue;

            foreach (var p in kv.Value.Properties)
            {
                if (!p.Name.Text.StartsWith("Upgrades", StringComparison.Ordinal)) continue;
                foreach (var entry in StructArray(p.Tag?.GenericValue))
                {
                    string? output = null;
                    var required = new List<RecipeIngredient>();
                    foreach (var ep in entry.Properties)
                    {
                        if (ep.Name.Text.StartsWith("OutputItem", StringComparison.Ordinal))
                        {
                            output = RowNameOf(ep.Tag?.GenericValue);
                        }
                        else if (ep.Name.Text.StartsWith("RequiredItems", StringComparison.Ordinal))
                        {
                            foreach (var req in StructArray(ep.Tag?.GenericValue))
                            {
                                string? itemId = null;
                                var n = 1;
                                foreach (var rp in req.Properties)
                                {
                                    if (rp.Name.Text.StartsWith("Item", StringComparison.Ordinal)
                                        && !rp.Name.Text.StartsWith("ItemCount", StringComparison.Ordinal))
                                    {
                                        itemId ??= RowNameOf(rp.Tag?.GenericValue);
                                    }
                                    else if (rp.Name.Text.StartsWith("Count", StringComparison.Ordinal))
                                    {
                                        n = rp.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                                    }
                                }
                                if (!string.IsNullOrEmpty(itemId)) required.Add(new RecipeIngredient(itemId!, n));
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(output))
                    {
                        upgrades.Add(new ItemUpgrade(sourceId, output!, required));
                    }
                }
            }
        }
    }

    private static IEnumerable<FStructFallback> StructArray(object? value)
    {
        if (value is not UScriptArray arr) yield break;
        foreach (var p in arr.Properties)
        {
            var v = p.GenericValue;
            if (v is FScriptStruct ss) v = ss.StructType;
            if (v is FStructFallback sf) yield return sf;
        }
    }

    private static string? RowNameOf(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        return sf.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
    }
}
