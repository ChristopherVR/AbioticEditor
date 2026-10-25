using AbioticEditor.Core.Codex;

namespace AbioticEditor.Core.Items;

public enum ItemAcquisitionKind { Trade, Salvage, Fishing }

/// <summary>
/// One catalog-listed way to acquire an item, separate from learning a recipe. This is not
/// evidence that a player owns the inputs, has access to the source, or will receive a drop.
/// </summary>
public sealed record ItemAcquisitionSource(
    ItemAcquisitionKind Kind,
    string ItemId,
    TraderInfo? Trader = null,
    TraderOffer? Offer = null,
    string? SalvageSourceItemId = null,
    SalvageDrop? Salvage = null,
    FishDefinition? Fish = null)
{
    /// <summary>
    /// Items the trader accepts, not the price of <see cref="Offer"/>. The source catalogs
    /// store independent stock and accepted-item lists with no explicit exchange pairing.
    /// </summary>
    public IReadOnlyList<TraderOffer> AcceptedItems => Trader?.Accepts ?? [];
}

/// <summary>
/// Reverse index of explicit trader stock, item salvage tables and catchable fish. It does
/// not infer enemy drops, loot locations, reverse crafting, or recipe-learning requirements.
/// Empty results mean no source is known in the supplied catalogs, not that none exists.
/// </summary>
public sealed class ItemAcquisitionIndex
{
    private readonly Dictionary<string, IReadOnlyList<ItemAcquisitionSource>> _sources = new(StringComparer.OrdinalIgnoreCase);

    public ItemAcquisitionIndex(IEnumerable<ItemCatalogEntry>? items,
        IEnumerable<TraderInfo>? traders = null, IEnumerable<FishDefinition>? fish = null)
    {
        var building = new Dictionary<string, List<ItemAcquisitionSource>>(StringComparer.OrdinalIgnoreCase);
        foreach (var trader in traders ?? [])
        {
            if (!PresentId(trader.Id)) continue;
            var accepted = new List<TraderOffer>();
            foreach (var payment in trader.Accepts)
                if (ValidOffer(payment) && !accepted.Any(existing => SameOffer(existing, payment))) accepted.Add(payment);
            var snapshot = trader with
            {
                RequiredFlags = trader.RequiredFlags.Where(PresentId).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Sells = trader.Sells.Where(ValidOffer).ToArray(),
                Accepts = accepted.OrderBy(offer => offer.ItemId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(offer => offer.Count).ThenBy(offer => offer.RequiredFlag, StringComparer.OrdinalIgnoreCase).ToArray(),
            };
            foreach (var offer in snapshot.Sells)
                Add(new(ItemAcquisitionKind.Trade, offer.ItemId, snapshot, offer));
        }

        foreach (var item in items ?? [])
        {
            if (!ConcreteId(item.Id) || item.Stats?.Salvage is not { } salvage) continue;
            foreach (var drop in salvage.Drops)
            {
                // Some upstream tables use fractional chances, others carry 100. Preserve the
                // raw value; do not invent a probability scale or a guaranteed salvage result.
                if (!ConcreteId(drop.ItemId) || drop.QuantityMin < 0 || drop.QuantityMax <= 0
                    || drop.QuantityMax < drop.QuantityMin || !double.IsFinite(drop.ChanceToDrop)
                    || drop.ChanceToDrop <= 0) continue;
                Add(new(ItemAcquisitionKind.Salvage, drop.ItemId, SalvageSourceItemId: item.Id, Salvage: drop));
            }
        }

        foreach (var catchable in fish ?? [])
            if (PresentId(catchable.Id) && ConcreteId(catchable.ItemId))
                Add(new(ItemAcquisitionKind.Fishing, catchable.ItemId!, Fish: catchable));

        foreach (var (itemId, routes) in building) _sources[itemId] = routes.AsReadOnly();

        void Add(ItemAcquisitionSource source)
        {
            if (!building.TryGetValue(source.ItemId, out var routes)) building[source.ItemId] = routes = [];
            if (!routes.Any(existing => SameSource(existing, source))) routes.Add(source);
        }
    }

    public IReadOnlyList<ItemAcquisitionSource> SourcesFor(string itemId)
        => ConcreteId(itemId) && _sources.TryGetValue(itemId, out var sources) ? sources : [];

    private static bool PresentId(string? id)
        => !string.IsNullOrWhiteSpace(id) && !id.Equals("None", StringComparison.OrdinalIgnoreCase);
    private static bool ConcreteId(string? id)
        => PresentId(id) && !id!.StartsWith("Any", StringComparison.OrdinalIgnoreCase);

    private static bool ValidOffer(TraderOffer offer) => ConcreteId(offer.ItemId) && offer.Count > 0;
    private static bool SameText(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool SameOffer(TraderOffer left, TraderOffer right)
        => SameText(left.ItemId, right.ItemId) && left.Count == right.Count && SameText(left.RequiredFlag, right.RequiredFlag);

    private static bool SameSource(ItemAcquisitionSource left, ItemAcquisitionSource right)
    {
        if (left.Kind != right.Kind || !SameText(left.ItemId, right.ItemId)) return false;
        return left.Kind switch
        {
            ItemAcquisitionKind.Trade => SameTrade(left, right),
            ItemAcquisitionKind.Salvage => SameText(left.SalvageSourceItemId, right.SalvageSourceItemId)
                && left.Salvage!.QuantityMin == right.Salvage!.QuantityMin
                && left.Salvage.QuantityMax == right.Salvage.QuantityMax
                && left.Salvage.ChanceToDrop == right.Salvage.ChanceToDrop,
            ItemAcquisitionKind.Fishing => SameFish(left.Fish!, right.Fish!),
            _ => false,
        };
    }

    private static bool SameTrade(ItemAcquisitionSource left, ItemAcquisitionSource right)
    {
        var a = left.Trader!;
        var b = right.Trader!;
        return SameText(a.Id, b.Id) && SameOffer(left.Offer!, right.Offer!)
            && a.RequiredFlags.SequenceEqual(b.RequiredFlags, StringComparer.OrdinalIgnoreCase)
            && a.Accepts.Count == b.Accepts.Count
            && a.Accepts.Zip(b.Accepts).All(pair => SameOffer(pair.First, pair.Second));
    }

    private static bool SameFish(FishDefinition left, FishDefinition right)
        => SameText(left.Id, right.Id) && SameText(left.ItemId, right.ItemId)
            && SameText(left.Location, right.Location) && left.IsRare == right.IsRare
            && SameText(left.RequiredWorldFlag, right.RequiredWorldFlag)
            && SameText(left.RequiredDlcId, right.RequiredDlcId)
            && SameText(left.RequiredBaitTag, right.RequiredBaitTag)
            && left.MidnightMult == right.MidnightMult && left.DawnMult == right.DawnMult
            && left.NoonMult == right.NoonMult && left.DuskMult == right.DuskMult;
}
