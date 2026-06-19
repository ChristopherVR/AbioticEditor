namespace AbioticEditor.App.Views.Player;

/// <summary>General tab: SteamID account surgery + bulk unlock/discovery actions.</summary>
public partial class PlayerGeneralTab : ContentView
{
    public PlayerGeneralTab()
    {
        InitializeComponent();
    }

    private async void OnChangeSteamId(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { PlayerEditor: not null } vm) return;
        var newId = SteamIdEntry.Text?.Trim() ?? string.Empty;

        var confirmed = await ViewUtils.ConfirmAsync(this,
            Services.LocalizationResourceManager.Instance["PlayerGeneral_ChangeSteamIdTitle"],
            Services.LocalizationResourceManager.Instance.Format("PlayerGeneral_ChangeSteamIdMessage", newId),
            Services.LocalizationResourceManager.Instance["PlayerGeneral_ChangeSteamIdConfirm"],
            Services.LocalizationResourceManager.Instance["Common_Cancel"]);
        if (!confirmed) return;

        var error = await vm.ChangePlayerSteamIdAsync(newId);
        if (error is not null)
        {
            await ViewUtils.AlertAsync(this, "SteamID change failed", error);
        }
    }

    private async void OnUnlockAllRecipes(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "unlock every recipe for this player"))
        {
            pe.RecipeBrowser.UnlockAllCommand.Execute(null);
        }
    }

    private async void OnDiscoverAllItems(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "mark every item as seen (suppresses all NEW badges)"))
        {
            pe.DiscoverAllItemsCommand.Execute(null);
        }
    }

    private async void OnDiscoverAllCrafted(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "mark every item as crafted at least once"))
        {
            pe.DiscoverAllCraftedCommand.Execute(null);
        }
    }

    private async void OnUnlockAllMaps(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "reveal every sector map pamphlet"))
        {
            pe.UnlockAllMapsCommand.Execute(null);
        }
    }
}
