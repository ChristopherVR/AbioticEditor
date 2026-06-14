using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views.World;

/// <summary>
/// Generic master-detail editor for one world-state feature map (power sockets, resource nodes,
/// NPC spawns, triggers, ...). The detail pane renders the selected entry's typed fields; free
/// text and numeric fields apply on unfocus/return so a half-typed value never round-trips
/// through the feature's validation (booleans and choices apply immediately via binding).
/// </summary>
public partial class WorldFeatureTab : ContentView
{
    public WorldFeatureTab()
    {
        InitializeComponent();
    }

    private static void Commit(object? sender)
    {
        if (sender is Entry { BindingContext: WorldFeatureFieldViewModel field })
        {
            field.Commit();
        }
    }

    private void OnFieldEntryUnfocused(object? sender, FocusEventArgs e) => Commit(sender);

    private void OnFieldEntryCompleted(object? sender, EventArgs e) => Commit(sender);
}
