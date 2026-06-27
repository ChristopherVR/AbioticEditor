using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// The slot gesture logic shared by every view that renders inventory slots (player
/// inventory, transmog, world containers, base containers, item palette). Views keep
/// thin instance handlers (XamlC requires handlers on the x:Class type) that delegate
/// here with the inherited MainViewModel.
/// </summary>
public static class SlotInteractions
{
    /// <summary>
    /// Right-click "Send to player" on a world container slot: hands the bound slot to the world
    /// editor's cross-save transfer. The bound context comes off the menu item first (a context
    /// flyout inherits its element's BindingContext), with a parent-walk fallback.
    /// </summary>
    public static async Task SendSlotToPlayerAsync(MainViewModel? vm, object? sender)
    {
        if (vm?.WorldEditor is not { } world) return;
        var slot = (sender as BindableObject)?.BindingContext as InventorySlotViewModel
                   ?? ViewUtils.FindBoundContext<InventorySlotViewModel>(sender);
        await world.SendContainerItemToPlayerAsync(slot);
    }

    public static void SlotTapped(MainViewModel? vm, object? sender)
    {
        if (vm is null) return;
        var slot = ViewUtils.FindBoundContext<InventorySlotViewModel>(sender);
        if (slot is null)
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance["Slot_MsgNoBoundContext"];
            return;
        }
        vm.SelectSlot(slot);
    }

    public static void SlotDragStarting(MainViewModel? vm, object? sender, DragStartingEventArgs e)
    {
        var src = ViewUtils.FindBoundContext<InventorySlotViewModel>(sender);
        if (vm is null || src is null || src.IsEmpty)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties["src"] = src;
        e.Data.Text = src.DisplayName;
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgDragging", src.DisplayName, src.Kind, src.Index);
    }

    public static void SlotDrop(MainViewModel? vm, object? sender, DropEventArgs e)
    {
        if (vm is null) return;
        var target = ViewUtils.FindBoundContext<InventorySlotViewModel>(sender);
        if (target is null) return;

        // Palette drops spawn a fresh copy of the catalog item into the slot.
        if (e.Data?.Properties.TryGetValue("paletteItem", out var paletteObj) == true
            && paletteObj is PaletteItemViewModel palette)
        {
            // Slots reject items that don't fit (same rules the game enforces): equipment
            // role mismatch, and pets dropped into the backpack (hotbar / Companion only).
            if (InventorySlotViewModel.ValidateForSlot(target.Kind, target.Role, palette.Entry) is { } problem)
            {
                vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgBlocked", problem);
                e.Handled = true;
                return;
            }
            SlotSwap.FillFromCatalog(target, palette.Entry);
            vm.SelectSlot(target);
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgGave", palette.DisplayName, target.Count);
            e.Handled = true;
            return;
        }

        if (e.Data?.Properties.TryGetValue("src", out var srcObj) != true) return;
        if (srcObj is not InventorySlotViewModel src || src == target) return;

        // Swap validation both ways: the dragged item must fit the target slot and
        // the displaced item must fit the source slot (role fit + hotbar-only pets).
        if (InventorySlotViewModel.ValidateForSlot(target.Kind, target.Role, src.Entry) is { } targetProblem)
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgBlocked", targetProblem);
            e.Handled = true;
            return;
        }
        if (!target.IsEmpty && InventorySlotViewModel.ValidateForSlot(src.Kind, src.Role, target.Entry) is { } srcProblem)
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgBlockedSwap", srcProblem);
            e.Handled = true;
            return;
        }

        SlotSwap.Swap(src, target);
        vm.SelectSlot(target);
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgSwapped", target.DisplayName);
        e.Handled = true;
    }

    /// <summary>Slot dragged onto a container row: move into its first empty slot.</summary>
    public static void ContainerDrop(MainViewModel? vm, object? sender, DropEventArgs e)
    {
        if (vm is null) return;
        var targetContainer = ViewUtils.FindBoundContext<WorldContainerViewModel>(sender);
        if (targetContainer is null || vm.WorldEditor is not { } editor) return;
        if (e.Data?.Properties.TryGetValue("src", out var srcObj) != true) return;
        if (srcObj is not InventorySlotViewModel src) return;

        // Same-container drops are no-ops here - slot-on-slot handles those.
        var srcContainer = editor.AllContainers.FirstOrDefault(c => c.Slots.Contains(src));
        if (srcContainer is null || srcContainer == targetContainer) return;

        if (SlotSwap.MoveToFirstEmpty(src, targetContainer.Slots))
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgMoved", src.DisplayName, targetContainer.DisplayName);
            editor.SelectedContainer = targetContainer;
            e.Handled = true;
        }
        else
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgNoEmptySlotIn", targetContainer.DisplayName);
        }
    }

    public static void PaletteDragStarting(MainViewModel? vm, object? sender, DragStartingEventArgs e)
    {
        var item = ViewUtils.FindBoundContext<PaletteItemViewModel>(sender);
        if (vm is null || item is null)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties["paletteItem"] = item;
        e.Data.Text = item.DisplayName;
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgDraggingFromCatalog", item.DisplayName);
    }

    public static void PaletteItemSelected(MainViewModel? vm, object? sender)
    {
        var item = ViewUtils.FindBoundContext<PaletteItemViewModel>(sender);
        if (vm is null || item is null || vm.ItemPalette is null) return;
        vm.ItemPalette.SelectedItem = item;
    }

    /// <summary>
    /// Double-tap on a palette item = quick give: fills the selected slot if one is
    /// selected, otherwise the first empty slot of the active editor's main inventory
    /// (player) or selected container (world).
    /// </summary>
    public static void PaletteItemTapped(MainViewModel? vm, object? sender)
    {
        if (vm is null) return;
        var item = ViewUtils.FindBoundContext<PaletteItemViewModel>(sender);
        if (item is null) return;

        // Pets are hotbar-only: when auto-picking a destination, prefer the hotbar so a
        // quick-give never lands them in the backpack (where the game would reject them).
        var hotbarOnly = Core.Items.EquipSlotTypes.IsHotbarOnly(item.Entry);

        InventorySlotViewModel? target = null;
        if (vm.ActiveSlot is { IsEmpty: true } active)
        {
            target = active;
        }
        else if (vm.PlayerEditor is { } pe)
        {
            target = hotbarOnly
                ? pe.Hotbar.FirstOrDefault(s => s.IsEmpty)
                : pe.Main.FirstOrDefault(s => s.IsEmpty) ?? pe.Hotbar.FirstOrDefault(s => s.IsEmpty);
        }
        else if (vm.WorldEditor is { SelectedContainer: { } container })
        {
            target = container.Slots.FirstOrDefault(s => s.IsEmpty);
        }

        if (target is null)
        {
            vm.StatusMessage = hotbarOnly
                ? Services.LocalizationResourceManager.Instance["Slot_MsgNoEmptyHotbar"]
                : Services.LocalizationResourceManager.Instance["Slot_MsgNoEmptyAvailable"];
            return;
        }

        // Honor the same placement rules as drag-drop (e.g. a pet onto a selected backpack slot).
        if (InventorySlotViewModel.ValidateForSlot(target.Kind, target.Role, item.Entry) is { } problem)
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgBlocked", problem);
            return;
        }

        SlotSwap.FillFromCatalog(target, item.Entry);
        vm.SelectSlot(target);
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgGaveToSlot", item.DisplayName, target.Count, target.Index);
    }
}
