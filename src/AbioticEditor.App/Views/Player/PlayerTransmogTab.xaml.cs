namespace AbioticEditor.App.Views.Player;

/// <summary>Transmog tab: cosmetic override slots + armor visibility toggles.</summary>
public partial class PlayerTransmogTab : ContentView
{
    public PlayerTransmogTab()
    {
        InitializeComponent();
    }

    private void OnSlotTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.SlotTapped(ViewUtils.Vm(this), sender);

    private void OnSlotDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.SlotDragStarting(ViewUtils.Vm(this), sender, e);

    private void OnSlotDrop(object? sender, DropEventArgs e)
        => SlotInteractions.SlotDrop(ViewUtils.Vm(this), sender, e);
}
