namespace AbioticEditor.App.Views.Player;

/// <summary>
/// Inventory tab: equipment paper-doll, hotbar and the POCKETS backpack grid.
/// Gesture handlers delegate to the shared SlotInteractions logic.
/// </summary>
public partial class PlayerInventoryTab : ContentView
{
    public PlayerInventoryTab()
    {
        InitializeComponent();
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.SlotTapped(ViewUtils.Vm(this), sender);

    private void OnSlotDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.SlotDragStarting(ViewUtils.Vm(this), sender, e);

    private void OnSlotDrop(object? sender, DropEventArgs e)
        => SlotInteractions.SlotDrop(ViewUtils.Vm(this), sender, e);

    private void OnSortBackpack(object? sender, EventArgs e)
        => ViewUtils.Vm(this)?.SortBackpack();

    private async void OnDropActiveItem(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { } vm)
        {
            await vm.DropActiveItemAsync();
        }
    }

    private void OnPickUpGroundItem(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        if (ViewUtils.FindBoundContext<ViewModels.GroundItemOption>(sender) is { } option)
        {
            vm.PickUpGroundItem(option);
        }
    }
}
