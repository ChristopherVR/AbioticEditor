using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views.World;

/// <summary>Dropped-items tab: filter and prune ground litter for in-game performance.</summary>
public partial class WorldDroppedTab : ContentView
{
    public WorldDroppedTab()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Dropped item tapped -> its slot opens in the sidebar editor AND the item's
    /// encyclopedia card (description, stats, crafted-by / used-in / sold-by) opens in
    /// the palette below it.
    /// </summary>
    private void OnDroppedItemTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        var dropped = ViewUtils.FindBoundContext<DroppedItemViewModel>(sender);
        if (dropped is null) return;
        dropped.Slot.EnsureIcon();
        vm.ActiveSlot = dropped.Slot;
        vm.ShowItemEncyclopedia(dropped.Slot.ItemId);
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("WorldDropped_InspectingStatus", dropped.DisplayName);
    }
}
