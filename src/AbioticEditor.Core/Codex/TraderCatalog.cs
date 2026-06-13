using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Codex;

/// <summary>One item a trader sells: what you get and the world flag gating it.</summary>
public sealed record TraderOffer(string ItemId, int Count, string? RequiredFlag);

/// <summary>
/// One trader from <c>DT_NPC_Traders</c> joined with their stock from
/// <c>DT_NPC_TraderItems</c>.
/// </summary>
public sealed record TraderInfo(
    string Id,
    string? ImageAssetPath,
    IReadOnlyList<string> RequiredFlags,
    IReadOnlyList<TraderOffer> Sells,
    IReadOnlyList<TraderOffer> Accepts);

/// <summary>
/// Loads the trader roster and their barter stock. Trading in AF is barter-only:
/// "Sells" are the items a trader offers, "Accepts" what they take in exchange.
/// </summary>
public static class TraderCatalog
{
    public static IReadOnlyList<TraderInfo> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Array.Empty<TraderInfo>();
        try
        {
            var stock = LoadStock(provider);

            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_Traders");
            var result = new List<TraderInfo>();
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                foreach (var kv in dt.RowMap)
                {
                    var id = kv.Key.Text;
                    if (string.IsNullOrEmpty(id)) continue;

                    string? image = null, stockRow = null;
                    var flags = new List<string>();
                    foreach (var p in kv.Value.Properties)
                    {
                        switch (p.Name.Text)
                        {
                            case "NPCImage":
                                image = p.Tag?.GenericValue?.ToString();
                                break;
                            case "NPCTradeInventory":
                                stockRow = RowNameOf(p.Tag?.GenericValue);
                                break;
                            case "RequiredWorldFlags":
                                if (p.Tag?.GenericValue is UScriptArray arr)
                                {
                                    foreach (var fp in arr.Properties)
                                    {
                                        var f = RowNameOf(fp.GenericValue);
                                        if (!string.IsNullOrEmpty(f) && f != "None") flags.Add(f!);
                                    }
                                }
                                break;
                        }
                    }

                    var (sells, accepts) = stockRow is not null && stock.TryGetValue(stockRow, out var s)
                        ? s
                        : (Array.Empty<TraderOffer>() as IReadOnlyList<TraderOffer>, Array.Empty<TraderOffer>() as IReadOnlyList<TraderOffer>);
                    result.Add(new TraderInfo(id, image, flags, sells, accepts));
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<TraderInfo>();
        }
    }

    private static Dictionary<string, (IReadOnlyList<TraderOffer> Sells, IReadOnlyList<TraderOffer> Accepts)>
        LoadStock(GameAssetProvider provider)
    {
        var result = new Dictionary<string, (IReadOnlyList<TraderOffer>, IReadOnlyList<TraderOffer>)>(StringComparer.OrdinalIgnoreCase);
        var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_TraderItems");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            foreach (var kv in dt.RowMap)
            {
                var sells = new List<TraderOffer>();
                var accepts = new List<TraderOffer>();
                foreach (var p in kv.Value.Properties)
                {
                    if (p.Name.Text.StartsWith("BuyableItems_", StringComparison.Ordinal))
                    {
                        // { Item_2 = { Item_5 = handle, Count_6 }, WorldFlagToMakeAvailable_9, AvailableStock_13 }
                        foreach (var entry in StructArray(p.Tag?.GenericValue))
                        {
                            string? itemId = null, flag = null;
                            var count = 1;
                            foreach (var ep in entry.Properties)
                            {
                                if (ep.Name.Text.StartsWith("Item_", StringComparison.Ordinal))
                                {
                                    var inner = ep.Tag?.GenericValue;
                                    if (inner is FScriptStruct iss) inner = iss.StructType;
                                    if (inner is FStructFallback isf)
                                    {
                                        foreach (var ip in isf.Properties)
                                        {
                                            if (ip.Name.Text.StartsWith("Item_", StringComparison.Ordinal))
                                                itemId = RowNameOf(ip.Tag?.GenericValue);
                                            else if (ip.Name.Text.StartsWith("Count_", StringComparison.Ordinal))
                                                count = ip.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                                        }
                                    }
                                }
                                else if (ep.Name.Text.StartsWith("WorldFlagToMakeAvailable_", StringComparison.Ordinal))
                                {
                                    flag = RowNameOf(ep.Tag?.GenericValue);
                                    if (flag == "None") flag = null;
                                }
                            }
                            if (!string.IsNullOrEmpty(itemId)) sells.Add(new TraderOffer(itemId!, count, flag));
                        }
                    }
                    else if (p.Name.Text.StartsWith("TradeableItems_", StringComparison.Ordinal))
                    {
                        foreach (var entry in StructArray(p.Tag?.GenericValue))
                        {
                            string? itemId = null;
                            var count = 1;
                            foreach (var ep in entry.Properties)
                            {
                                if (ep.Name.Text.StartsWith("Item_", StringComparison.Ordinal))
                                    itemId = RowNameOf(ep.Tag?.GenericValue);
                                else if (ep.Name.Text.StartsWith("Count_", StringComparison.Ordinal))
                                    count = ep.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                            }
                            if (!string.IsNullOrEmpty(itemId)) accepts.Add(new TraderOffer(itemId!, count, null));
                        }
                    }
                }
                result[kv.Key.Text] = (sells, accepts);
            }
        }
        return result;
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
