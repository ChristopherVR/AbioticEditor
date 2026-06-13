namespace AbioticEditor.App.Views.World;

/// <summary>Containers tab: filterable container list + the selected container's slots.</summary>
public partial class WorldContainersTab : ContentView
{
    public WorldContainersTab()
    {
        InitializeComponent();
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.SlotTapped(ViewUtils.Vm(this), sender);

    private void OnSlotDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.SlotDragStarting(ViewUtils.Vm(this), sender, e);

    private void OnSlotDrop(object? sender, DropEventArgs e)
        => SlotInteractions.SlotDrop(ViewUtils.Vm(this), sender, e);

    private void OnContainerDrop(object? sender, DropEventArgs e)
        => SlotInteractions.ContainerDrop(ViewUtils.Vm(this), sender, e);
}
