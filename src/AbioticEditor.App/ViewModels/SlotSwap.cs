namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Helpers for moving / swapping item data between inventory slot VMs. Positional
/// metadata (Index, Role, Kind) stays attached to each tile; only item data moves.
/// </summary>
internal static class SlotSwap
{
    /// <summary>Move <paramref name="source"/>'s contents into the first empty slot of
    /// <paramref name="targetSlots"/>. Returns false if the target has no empty slot.</summary>
    public static bool MoveToFirstEmpty(InventorySlotViewModel source, IReadOnlyList<InventorySlotViewModel> targetSlots)
    {
        if (source.IsEmpty) return false;
        var empty = targetSlots.FirstOrDefault(s => s.IsEmpty);
        if (empty is null) return false;

        empty.ItemId = source.ItemId;
        empty.Count = source.Count;
        empty.Durability = source.Durability;
        empty.MaxDurability = source.MaxDurability;
        empty.AmmoInMagazine = source.AmmoInMagazine;
        empty.LiquidLevel = source.LiquidLevel;
        empty.LiquidType = source.LiquidType;
        empty.DynamicState = source.DynamicState;
        empty.PlayerMadeString = source.PlayerMadeString;
        empty.AssetId = source.AssetId;

        ClearToEmpty(source);
        return true;
    }

    /// <summary>
    /// Fill <paramref name="target"/> with a fresh copy of a catalog item: full stack,
    /// full durability, no ammo/liquid. Used by the item-palette drag/give actions.
    /// </summary>
    public static void FillFromCatalog(InventorySlotViewModel target, Core.Items.ItemCatalogEntry entry)
    {
        target.ItemId = entry.Id;
        target.Count = Math.Max(1, entry.StackSize);
        target.MaxDurability = entry.MaxDurability;
        target.Durability = entry.MaxDurability;
        target.AmmoInMagazine = 0;
        target.LiquidLevel = 0;
        target.LiquidType = null;
        target.DynamicState = false;
        target.PlayerMadeString = null;
    }

    /// <summary>Reset all mutable item fields to the AF empty-slot sentinel.</summary>
    public static void ClearToEmpty(InventorySlotViewModel s)
    {
        s.ItemId = "Empty";
        s.Count = 0;
        s.Durability = 0;
        s.MaxDurability = 0;
        s.AmmoInMagazine = 0;
        s.LiquidLevel = 0;
        s.LiquidType = null;
        s.DynamicState = false;
        s.PlayerMadeString = null;
        s.AssetId = null;
    }

    public static void Swap(InventorySlotViewModel a, InventorySlotViewModel b)
    {
        if (a == b) return;

        var aItem = a.ItemId;       var bItem = b.ItemId;
        var aCount = a.Count;       var bCount = b.Count;
        var aDur = a.Durability;    var bDur = b.Durability;
        var aMax = a.MaxDurability; var bMax = b.MaxDurability;
        var aAmmo = a.AmmoInMagazine; var bAmmo = b.AmmoInMagazine;
        var aLiq = a.LiquidLevel;   var bLiq = b.LiquidLevel;
        var aLT = a.LiquidType;     var bLT = b.LiquidType;
        var aDyn = a.DynamicState;  var bDyn = b.DynamicState;
        var aPMS = a.PlayerMadeString; var bPMS = b.PlayerMadeString;
        var aAss = a.AssetId;       var bAss = b.AssetId;

        a.ItemId = bItem;       b.ItemId = aItem;
        a.Count = bCount;       b.Count = aCount;
        a.Durability = bDur;    b.Durability = aDur;
        a.MaxDurability = bMax; b.MaxDurability = aMax;
        a.AmmoInMagazine = bAmmo; b.AmmoInMagazine = aAmmo;
        a.LiquidLevel = bLiq;   b.LiquidLevel = aLiq;
        a.LiquidType = bLT;     b.LiquidType = aLT;
        a.DynamicState = bDyn;  b.DynamicState = aDyn;
        a.PlayerMadeString = bPMS; b.PlayerMadeString = aPMS;
        a.AssetId = bAss;       b.AssetId = aAss;
    }
}
