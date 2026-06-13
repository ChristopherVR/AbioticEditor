using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// View-model for one container in a world save. Wraps its inventories' slots as
/// mutable <see cref="InventorySlotViewModel"/>s so they can be edited + dragged just
/// like player inventory slots.
/// </summary>
public sealed class WorldContainerViewModel
{
    public WorldContainerViewModel(WorldContainer container)
    {
        Source = container;
        Slots = BuildSlots(container);
        OccupiedCount = Slots.Count(s => !s.IsEmpty);
    }

    public WorldContainer Source { get; }

    public string Id => Source.Id;
    public string DisplayName => Source.ClassName?.Replace("_C", "") ?? Source.Id;
    public string SourceLabel => Source.Source.ToString().ToUpperInvariant();
    public bool IsDeployed => Source.Source == WorldContainerSource.Deployed;

    public IReadOnlyList<InventorySlotViewModel> Slots { get; }
    public int OccupiedCount { get; }
    public int TotalSlots => Slots.Count;
    public string CountText => $"{OccupiedCount}/{TotalSlots}";

    /// <summary>True when any item inside matches <paramref name="filter"/> (name or id).</summary>
    public bool ContainsItem(string filter)
        => Slots.Any(s => !s.IsEmpty
            && (s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
             || (s.ItemId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)));

    /// <summary>Rebuild the immutable container record from current slot VM state.</summary>
    public WorldContainer ToCurrentContainer()
    {
        if (Source.Inventories.Count == 0) return Source;

        // Containers we've sampled have exactly one inventory; if more, only the first
        // is edited - extra inventories stay as-is.
        var first = new WorldInventory(Slots.Select(s => s.ToCurrentSlot()).ToList());
        IReadOnlyList<WorldInventory> inventories = Source.Inventories.Count == 1
            ? new[] { first }
            : (IReadOnlyList<WorldInventory>)new[] { first }.Concat(Source.Inventories.Skip(1)).ToList();
        return Source with { Inventories = inventories };
    }

    private static List<InventorySlotViewModel> BuildSlots(WorldContainer container)
    {
        var catalog = Services.GameDataServices.Catalog;
        var result = new List<InventorySlotViewModel>();

        if (container.Inventories.Count == 0) return result;
        foreach (var slot in container.Inventories[0].Slots)
        {
            var entry = slot.IsEmpty ? null : catalog?.Find(slot.ItemId);
            // Icons load lazily via EnsureIcon when the container is selected - a world
            // save has hundreds of container slots and eager extraction froze the load.
            result.Add(new InventorySlotViewModel(InventoryKind.Main, slot, entry, iconPath: null));
        }
        return result;
    }

    /// <summary>Starts async icon loads for this container's slots (call on selection).</summary>
    public void EnsureIcons()
    {
        foreach (var s in Slots) s.EnsureIcon();
    }
}
