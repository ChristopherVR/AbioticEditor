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
            vm.StatusMessage = Services.LocalizationResourceManager.Instance["Slot_MsgTickAtLeastOne"];
            return;
        }

        var target = we.IsMetadataSave
            ? "WorldSave_Facility.sav"
            : Services.LocalizationResourceManager.Instance["Slot_ThisSave"];
        var confirmed = await ViewUtils.ConfirmAsync(this,
            Services.LocalizationResourceManager.Instance.Format("Slot_UnlockItemsTitle", trader.SelectedStockFlags.Count, trader.Name),
            Services.LocalizationResourceManager.Instance.Format("Slot_UnlockItemsBody", flags.Count, target, string.Join("\n", flags)) +
            (we.IsMetadataSave
                ? Services.LocalizationResourceManager.Instance["Slot_FacilityWrittenNow"]
                : Services.LocalizationResourceManager.Instance["Slot_ChangesStaged"]),
            Services.LocalizationResourceManager.Instance["Slot_Unlock"],
            Services.LocalizationResourceManager.Instance["Common_Cancel"]);
        if (!confirmed) return;

        var (_, message) = await we.UnlockTraderFlagsAsync(trader, flags);
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_TraderStatus", trader.Name, message);
    }

    private async void OnUnlockTraderFull(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { WorldEditor: { } we } vm || we.SelectedTrader is not { } trader) return;

        var missing = trader.MissingFlags;
        if (missing.Count == 0)
        {
            vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgTraderFullyUnlocked", trader.Name);
            return;
        }

        var target = we.IsMetadataSave
            ? "WorldSave_Facility.sav"
            : Services.LocalizationResourceManager.Instance["Slot_ThisSave"];
        var confirmed = await ViewUtils.ConfirmAsync(this,
            Services.LocalizationResourceManager.Instance.Format("Slot_UnlockAllTitle", trader.Name),
            Services.LocalizationResourceManager.Instance.Format("Slot_UnlockAllBody", missing.Count, target, string.Join("\n", missing)) +
            (we.IsMetadataSave
                ? Services.LocalizationResourceManager.Instance["Slot_FacilityWrittenNow"]
                : Services.LocalizationResourceManager.Instance["Slot_ChangesStaged"]),
            Services.LocalizationResourceManager.Instance["Slot_Unlock"],
            Services.LocalizationResourceManager.Instance["Common_Cancel"]);
        if (!confirmed) return;

        var (_, message) = await we.UnlockTraderAsync(trader);
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_TraderStatus", trader.Name, message);
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
            ? Services.LocalizationResourceManager.Instance.Format("Slot_MsgChapterAlreadySet", chapter.Title)
            : we.IsMetadataSave
                ? Services.LocalizationResourceManager.Instance.Format("Slot_MsgChapterWroteFacility", added)
                : Services.LocalizationResourceManager.Instance.Format("Slot_MsgChapterAdded", added, chapter.Title);
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
            Services.LocalizationResourceManager.Instance["Slot_SetPrereqsTitle"],
            Services.LocalizationResourceManager.Instance.Format("Slot_SetPrereqsBody", flag.FriendlyName, missing.Count, string.Join("\n", missing)),
            Services.LocalizationResourceManager.Instance["Slot_SetThem"],
            Services.LocalizationResourceManager.Instance["Common_Cancel"]);
        if (!confirmed) return;

        var added = we.EnablePrerequisitesForSelectedFlag();
        vm.StatusMessage = Services.LocalizationResourceManager.Instance.Format("Slot_MsgPrereqsSet", added, flag.FriendlyName);
    }

    // ---------- item palette ----------

    private void OnPaletteItemSelected(object? sender, TappedEventArgs e)
        => SlotInteractions.PaletteItemSelected(ViewUtils.Vm(this), sender);

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e)
        => SlotInteractions.PaletteItemTapped(ViewUtils.Vm(this), sender);

    private void OnPaletteDragStarting(object? sender, DragStartingEventArgs e)
        => SlotInteractions.PaletteDragStarting(ViewUtils.Vm(this), sender, e);
}
