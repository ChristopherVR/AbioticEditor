namespace AbioticEditor.App.Views.Player;

/// <summary>GATEPal tab: PDA-style journal chrome over emails/notes/compendium/fish.</summary>
public partial class PlayerCodexTab : ContentView
{
    public PlayerCodexTab()
    {
        InitializeComponent();
    }

    private void OnFishUnlockBaitTapped(object? sender, TappedEventArgs e)
        => OpenBaitInSlotEditor(ViewUtils.Vm(this)?.PlayerEditor?.Codex?.Selected?.FishUnlockBaitItemId);

    private void OnFishRequiredBaitTapped(object? sender, TappedEventArgs e)
        => OpenBaitInSlotEditor(ViewUtils.Vm(this)?.PlayerEditor?.Codex?.Selected?.FishRequiredBaitItemId);

    /// <summary>Surfaces a bait item's encyclopedia card in the right-hand slot editor.</summary>
    private void OpenBaitInSlotEditor(string? baitItemId)
    {
        if (baitItemId is null || ViewUtils.Vm(this) is not { } vm) return;
        vm.ShowItemEncyclopedia(baitItemId);
    }
}
