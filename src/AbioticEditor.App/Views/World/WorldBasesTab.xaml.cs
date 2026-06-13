namespace AbioticEditor.App.Views.World;

/// <summary>
/// Bases tab: base list + top-down map. Clicking a base container opens its slot
/// grid in place of the map; slot taps route to the shared slot editor.
/// </summary>
public partial class WorldBasesTab : ContentView
{
    public WorldBasesTab()
    {
        InitializeComponent();
    }

    private void OnCloseBaseContainer(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we })
        {
            we.SelectedBaseContainer = null;
        }
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.SlotTapped(ViewUtils.Vm(this), sender);

    private void OnSlotDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.SlotDragStarting(ViewUtils.Vm(this), sender, e);

    private void OnSlotDrop(object? sender, DropEventArgs e)
        => SlotInteractions.SlotDrop(ViewUtils.Vm(this), sender, e);
}
