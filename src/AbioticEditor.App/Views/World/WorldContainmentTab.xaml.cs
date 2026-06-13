using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views.World;

/// <summary>
/// Containment tab (metadata save): trapped creatures with appearance + location detail,
/// alongside the world-wide GlobalUnlocks bulk actions.
/// </summary>
public partial class WorldContainmentTab : ContentView
{
    public WorldContainmentTab()
    {
        InitializeComponent();
    }

    private async void OnContainmentTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we }) return;
        var entry = ViewUtils.FindBoundContext<LeyakContainmentViewModel>(sender);
        if (entry is null) return;
        // A sealed entry prompts a clearance override instead of opening its detail
        // (which would expose the creature's appearance and location).
        if (entry.IsConcealed)
        {
            await entry.RevealAsync();
            return;
        }
        we.SelectedContainment = we.SelectedContainment == entry ? null : entry;
    }

    private void OnCloseContainmentDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.SelectedContainment = null;
    }

    private async void OnUnlockAllWorldItems(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }
            && await ViewUtils.ConfirmBulkAsync(this, "mark every catalog item as seen world-wide"))
        {
            we.UnlockAllWorldItems();
        }
    }

    private async void OnUnlockAllWorldEmails(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }
            && await ViewUtils.ConfirmBulkAsync(this, "mark every email as read world-wide"))
        {
            we.UnlockAllWorldEmails();
        }
    }

    private async void OnUnlockAllWorldJournals(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }
            && await ViewUtils.ConfirmBulkAsync(this, "mark every journal page as found world-wide"))
        {
            we.UnlockAllWorldJournals();
        }
    }

    private async void OnUnlockAllWorldCompendium(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }
            && await ViewUtils.ConfirmBulkAsync(this, "unlock every compendium section world-wide"))
        {
            we.UnlockAllWorldCompendium();
        }
    }
}
