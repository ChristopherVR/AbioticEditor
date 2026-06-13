using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views.World;

/// <summary>
/// Traders tab (metadata save): the full barter roster with per-item stock unlocking.
/// Unlocks write trader-gating + stock flags into the sibling WorldSave_Facility.sav.
/// </summary>
public partial class WorldTradersTab : ContentView
{
    public WorldTradersTab()
    {
        InitializeComponent();
    }

    private async void OnTraderCardTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we }) return;
        var card = ViewUtils.FindBoundContext<TraderCardViewModel>(sender);
        if (card is null) return;
        // A sealed (not-yet-available) trader prompts a clearance override instead of
        // opening its detail, which would expose stock and unlock flags.
        if (card.IsConcealed)
        {
            await card.RevealAsync();
            return;
        }
        // Selecting a card opens its detail in the right-hand slot panel
        // (ShowTraderDetail); tapping the open card again closes it.
        we.SelectedTrader = we.SelectedTrader == card ? null : card;
    }
}
