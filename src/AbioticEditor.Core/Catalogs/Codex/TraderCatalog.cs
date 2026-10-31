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
/// <param name="DisplayName">The name the game shows (the trader's conversation row <c>NPCName</c>), when known.</param>
/// <param name="NpcClass">The character class the game spawns for this trader, e.g.
/// <c>/DrKempc9PPu/Blueprints/NarrativeNPC_Human_DrKemp.NarrativeNPC_Human_DrKemp_C</c>.</param>
/// <param name="Origin">The mod that added this trader; null for a base-game trader.</param>
/// <param name="StockOrigin">The mod that supplies this trader's stock list, when a mod added or
/// rewrote it (e.g. a rebalance that changes a base trader's stock); null for base-game stock.</param>
public sealed record TraderInfo(
    string Id,
    string? ImageAssetPath,
    IReadOnlyList<string> RequiredFlags,
    IReadOnlyList<TraderOffer> Sells,
    IReadOnlyList<TraderOffer> Accepts,
    string? DisplayName = null,
    string? NpcClass = null,
    ModOrigin? Origin = null,
    ModOrigin? StockOrigin = null)
{
    /// <summary>True when a mod added this trader or changed what they trade.</summary>
    public bool IsModded => Origin is not null || StockOrigin is not null;
}

/// <summary>
/// Loads the trader roster and their barter stock. Trading in AF is barter-only:
/// "Sells" are the items a trader offers, "Accepts" what they take in exchange.
/// </summary>
public static partial class TraderCatalog
{
    /// <summary>
    /// Loads the trader roster from the game's data tables, falling back to the built-in
    /// <see cref="Fallback"/> snapshot when the game isn't installed or the tables can't be
    /// read - so the editor always shows traders and their unlock flags. Live data supersedes
    /// the snapshot (and adds pak-only portraits) whenever the game is available.
    /// </summary>
    private const string TradersTable = "AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_Traders";
    private const string TraderItemsTable = "AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_TraderItems";
    private const string ConversationsTable = "AbioticFactor/Content/Blueprints/DataTables/DT_NPC_Conversations";

    private sealed record Stock(IReadOnlyList<TraderOffer> Sells, IReadOnlyList<TraderOffer> Accepts, ModOrigin? Origin);

    /// <summary>
    /// The trader whose character a saved actor is, matched on the character class the trader row
    /// spawns. A saved actor is named after its class (<c>NarrativeNPC_Human_DrKemp_C_2</c>), so this
    /// names a modded trader or companion the level data knows nothing about. Null when none match.
    /// </summary>
    /// <param name="actorPathOrClass">A <c>WorldNpc.Id</c>/actor name, or a class path.</param>
    public static TraderInfo? FindForActor(IEnumerable<TraderInfo> traders, string? actorPathOrClass)
    {
        ArgumentNullException.ThrowIfNull(traders);
        if (string.IsNullOrEmpty(actorPathOrClass)) return null;
        var tail = actorPathOrClass[(Math.Max(actorPathOrClass.LastIndexOf('.'), actorPathOrClass.LastIndexOf(':')) + 1)..];
        // Strip a trailing instance number: NarrativeNPC_Human_DrKemp_C_2 -> NarrativeNPC_Human_DrKemp_C.
        var instance = tail.LastIndexOf("_C_", StringComparison.Ordinal);
        if (instance > 0 && tail[(instance + 3)..].All(char.IsDigit)) tail = tail[..(instance + 2)];
        return traders.FirstOrDefault(trader => trader.NpcClass is { } cls
            && cls[(cls.LastIndexOf('.') + 1)..].Equals(tail, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<TraderInfo> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Fallback;
        try
        {
            var stock = LoadStock(provider);

            var names = LoadConversationNames(provider);

            var result = new List<TraderInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var primary = provider.TryLoadDataTable(TradersTable);
            var structName = primary?.RowStructName;
            AddTraderRows(primary, stock, names, id => provider.RowOrigin(TradersTable, id), result, seen);

            // Merge mod/patch trader tables (same row struct, any content root); base wins.
            foreach (var path in ModTableDiscovery.DiscoverTablesByRowStruct(provider, structName, new[] { TradersTable }))
            {
                var origin = provider.ModOfAsset(path);
                AddTraderRows(provider.TryLoadDataTable(path), stock, names, _ => origin, result, seen);
            }
            return result.Count > 0 ? result : Fallback;
        }
        catch
        {
            return Fallback;
        }
    }

    private static void AddTraderRows(
        UDataTable? dt,
        Dictionary<string, Stock> stock,
        Dictionary<string, string> names,
        Func<string, ModOrigin?> originOf,
        List<TraderInfo> result,
        HashSet<string> seen)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            var id = kv.Key.Text;
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;

            string? image = null, stockRow = null, conversation = null, npcClass = null;
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
                    case "NPCConversation":
                        conversation = RowNameOf(p.Tag?.GenericValue);
                        break;
                    case "NPCClass":
                        npcClass = p.Tag?.GenericValue?.ToString();
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

            var offers = stockRow is not null && stock.TryGetValue(stockRow, out var s) ? s : new Stock([], [], null);
            var name = conversation is not null && names.TryGetValue(conversation, out var shown) ? shown : null;
            result.Add(new TraderInfo(id, image, flags, offers.Sells, offers.Accepts,
                name, string.IsNullOrEmpty(npcClass) || npcClass == "None" ? null : npcClass, originOf(id), offers.Origin));
        }
    }

    private static Dictionary<string, Stock> LoadStock(GameAssetProvider provider)
    {
        var result = new Dictionary<string, Stock>(StringComparer.OrdinalIgnoreCase);
        var primary = provider.TryLoadDataTable(TraderItemsTable);
        var structName = primary?.RowStructName;
        // Official mod rows (new stock lists, and overrides of a base trader's list) are already
        // merged into the primary table by the provider; this only says which mod supplied them.
        AddStockRows(primary, id => provider.RowOrigin(TraderItemsTable, id), result);

        // Merge mod/patch trader-stock tables (same row struct); base wins on conflict.
        foreach (var path in ModTableDiscovery.DiscoverTablesByRowStruct(provider, structName, new[] { TraderItemsTable }))
        {
            var origin = provider.ModOfAsset(path);
            AddStockRows(provider.TryLoadDataTable(path), _ => origin, result);
        }
        return result;
    }

    /// <summary>Conversation row id to the character's shown name (<c>NPCName</c>), mod rows included.</summary>
    private static Dictionary<string, string> LoadConversationNames(GameAssetProvider provider)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (provider.TryLoadDataTable(ConversationsTable) is not { } table) return names;
        foreach (var (key, row) in table.RowMap)
        {
            var text = row.Properties.FirstOrDefault(p => p.Name.Text == "NPCName")?.Tag?.GenericValue;
            var name = text is CUE4Parse.UE4.Objects.Core.i18N.FText ft ? ft.Text : text?.ToString();
            if (!string.IsNullOrWhiteSpace(key.Text) && !string.IsNullOrWhiteSpace(name)) names[key.Text] = name.Trim();
        }
        return names;
    }

    private static void AddStockRows(
        UDataTable? dt,
        Func<string, ModOrigin?> originOf,
        Dictionary<string, Stock> result)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            if (string.IsNullOrEmpty(kv.Key.Text) || result.ContainsKey(kv.Key.Text)) continue;

            var sells = new List<TraderOffer>();
            var accepts = new List<TraderOffer>();
            foreach (var p in kv.Value.Properties)
            {
                if (p.Name.Text.MatchesUnrealProperty("BuyableItems_"))
                {
                    // Older: { Item_2 = { Item_5 = handle, Count_6 }, WorldFlagToMakeAvailable_9, AvailableStock_13 }.
                    // Native 1.5 (TraderItemWF): { Output = InstancedStruct of AbioticItemCount { Item, Count },
                    // WorldFlagToMakeAvailable, AvailableStock }.
                    foreach (var entry in StructArray(p.Tag?.GenericValue))
                    {
                        string? itemId = null, flag = null;
                        var count = 1;
                        foreach (var ep in entry.Properties)
                        {
                            var raw = ep.Tag?.GenericValue is FScriptStruct wrapped ? wrapped.StructType : ep.Tag?.GenericValue;
                            var inner = ep.Name.Text.MatchesUnrealProperty("Item_") ? ep.Tag?.GenericValue
                                : ep.Name.Text == "Output" && raw is FInstancedStruct output ? output.NonConstStruct
                                : null;
                            if (inner is FScriptStruct iss) inner = iss.StructType;
                            if (inner is FStructFallback isf)
                            {
                                foreach (var ip in isf.Properties)
                                {
                                    if (ip.Name.Text.MatchesUnrealProperty("Item_"))
                                        itemId = RowNameOf(ip.Tag?.GenericValue);
                                    else if (ip.Name.Text.MatchesUnrealProperty("Count_"))
                                        count = ip.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                                }
                            }
                            else if (ep.Name.Text.MatchesUnrealProperty("WorldFlagToMakeAvailable_"))
                            {
                                flag = RowNameOf(ep.Tag?.GenericValue);
                                if (flag == "None") flag = null;
                            }
                        }
                        if (!string.IsNullOrEmpty(itemId)) sells.Add(new TraderOffer(itemId!, count, flag));
                    }
                }
                else if (p.Name.Text.MatchesUnrealProperty("TradeableItems_"))
                {
                    foreach (var entry in StructArray(p.Tag?.GenericValue))
                    {
                        string? itemId = null;
                        var count = 1;
                        foreach (var ep in entry.Properties)
                        {
                            if (ep.Name.Text.MatchesUnrealProperty("Item_"))
                                itemId = RowNameOf(ep.Tag?.GenericValue);
                            else if (ep.Name.Text.MatchesUnrealProperty("Count_"))
                                count = ep.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                        }
                        if (!string.IsNullOrEmpty(itemId)) accepts.Add(new TraderOffer(itemId!, count, null));
                    }
                }
            }
            result[kv.Key.Text] = new Stock(sells, accepts, originOf(kv.Key.Text));
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
