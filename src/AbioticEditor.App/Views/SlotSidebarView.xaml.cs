using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Right pane: slot editor, contextual detail cards (trader/chapter/door/world
/// recipe/flag) and the item catalog. Lives inline on desktop and re-homes into
/// the overlay drawer on phones (ResponsivePaneController).
/// </summary>
public partial class SlotSidebarView : ContentView
{
    public SlotSidebarView()
    {
        InitializeComponent();
    }

    // ---------- slot editor ----------

    private void OnCloseSlotEditor(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        vm.ActiveSlot = null;
        if (vm.PlayerEditor is { } editor) editor.SelectedSlot = null;
    }

    private void OnUpgradeSlot(object? sender, EventArgs e) => ViewUtils.Vm(this)?.ActiveSlot?.Upgrade();

    private void OnDowngradeSlot(object? sender, EventArgs e) => ViewUtils.Vm(this)?.ActiveSlot?.Downgrade();

    private void OnDismantleSlot(object? sender, EventArgs e) => ViewUtils.Vm(this)?.BeginDismantlePreview();

    private void OnCancelDismantle(object? sender, EventArgs e) => ViewUtils.Vm(this)?.CancelDismantle();

    private void OnConfirmDismantle(object? sender, EventArgs e) => ViewUtils.Vm(this)?.ConfirmDismantle();

    // ---------- detail cards ----------

    private void OnCloseTraderDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.SelectedTrader = null;
    }

    private void OnCloseMilestoneDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }) pe.SelectedMilestone = null;
    }

    private void OnTraderOfferTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.FindBoundContext<TraderOfferRowViewModel>(sender) is { } row)
        {
            row.Inspect();
        }
    }

    private async void OnUnlockSelectedStock(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we } vm || we.SelectedTrader is not { } trader) return;

        var flags = trader.SelectedUnlockFlags;
        if (flags.Count == 0)
        {
            vm.StatusMessage = "Tick at least one locked item to unlock.";
            return;
        }

        var target = we.IsMetadataSave ? "WorldSave_Facility.sav" : "this save";
        var confirmed = await ViewUtils.ConfirmAsync(this,
            $"Unlock {trader.SelectedStockFlags.Count} item(s) from {trader.Name}?",
            $"This sets {flags.Count} world flag(s) in {target}:\n\n{string.Join("\n", flags)}\n\n" +
            "These are story/progression flags — anything else gated by them also unlocks. " +
            (we.IsMetadataSave
                ? "The Facility save is written immediately (a .bak is kept)."
                : "Changes are staged; press SAVE to write."),
            "UNLOCK", "Cancel");
        if (!confirmed) return;

        var (_, message) = await we.UnlockTraderFlagsAsync(trader, flags);
        vm.StatusMessage = $"{trader.Name}: {message}";
    }

    private async void OnUnlockTraderFull(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we } vm || we.SelectedTrader is not { } trader) return;

        var missing = trader.MissingFlags;
        if (missing.Count == 0)
        {
            vm.StatusMessage = $"{trader.Name} is already fully unlocked in this world.";
            return;
        }

        var target = we.IsMetadataSave ? "WorldSave_Facility.sav" : "this save";
        var confirmed = await ViewUtils.ConfirmAsync(this,
            $"Unlock all of {trader.Name}?",
            $"This sets {missing.Count} world flag(s) in {target}:\n\n{string.Join("\n", missing)}\n\n" +
            "These are story/progression flags — anything else gated by them (quests, doors, spawns) " +
            "also unlocks. " +
            (we.IsMetadataSave
                ? "The Facility save is written immediately (a .bak is kept)."
                : "Changes are staged; press SAVE to write."),
            "UNLOCK", "Cancel");
        if (!confirmed) return;

        var (_, message) = await we.UnlockTraderAsync(trader);
        vm.StatusMessage = $"{trader.Name}: {message}";
    }

    private void OnCloseChapterDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.SelectedChapter = null;
    }

    private void OnUnlockChapter(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we } vm || we.SelectedChapter is not { } chapter) return;
        var added = we.UnlockChaptersThrough(chapter);
        vm.StatusMessage = added == 0
            ? $"All story flags through \"{chapter.Title}\" are already set."
            : we.IsMetadataSave
                ? $"Wrote {added} story flag(s) to WorldSave_Facility.sav (backup kept)."
                : $"Added {added} story flag(s) through \"{chapter.Title}\" - press SAVE to write.";
    }

    private void OnCloseDoorDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.SelectedDoor = null;
    }

    private async void OnDoorOnlineMapClicked(object? sender, EventArgs e)
        => await Launcher.Default.OpenAsync("https://gamemappers.com/abiotic-factor-map/");

    private void OnCloseWorldRecipeDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.GlobalRecipeBrowser.SelectedRecipe = null;
    }

    private void OnCloseFlagDetail(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { WorldEditor: { } we }) we.SelectedFlag = null;
    }

    private async void OnEnableFlagPrereqs(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we } vm || we.SelectedFlag is not { } flag) return;
        var missing = we.MissingPrerequisitesFor(flag.RawName);
        if (missing.Count == 0) return;

        var confirmed = await ViewUtils.ConfirmAsync(this,
            "Set prerequisites?",
            $"\"{flag.FriendlyName}\" needs {missing.Count} earlier flag(s):\n\n{string.Join("\n", missing)}\n\n" +
            "These are story/progression flags - content gated by them also unlocks. Additive only; staged until SAVE.",
            "SET THEM", "Cancel");
        if (!confirmed) return;

        var added = we.EnablePrerequisitesForSelectedFlag();
        vm.StatusMessage = $"Set {added} prerequisite flag(s) - \"{flag.FriendlyName}\" can now be enabled. Press SAVE to write.";
    }

    // ---------- item palette ----------

    private void OnPaletteItemSelected(object? sender, TappedEventArgs e)
        => SlotInteractions.PaletteItemSelected(ViewUtils.Vm(this), sender);

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.PaletteItemTapped(ViewUtils.Vm(this), sender);

    private void OnPaletteDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.PaletteDragStarting(ViewUtils.Vm(this), sender, e);
}
