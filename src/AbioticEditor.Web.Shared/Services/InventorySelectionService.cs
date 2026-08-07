using AbioticEditor.Web.Models;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Shares the inventory slot selected in the centre editor with the shell's right-hand
/// slot editor. The slot instance remains owned by <see cref="PlayerSaveSession"/>, so
/// there is only one staged edit model and one save/revert path.
/// </summary>
public sealed class InventorySelectionService
{
    public InventorySelection? Current { get; private set; }

    /// <summary>
    /// Catalog item whose read-only encyclopedia card the right-hand pane shows. Mirrors the
    /// native MainViewModel.ShowItemEncyclopedia used by surfaces that show items outside a
    /// slot (the GATEPal bait chips, tapped dropped items): it selects a catalog DETAIL, not
    /// an editable slot, and is independent of <see cref="Current"/> like the native palette
    /// selection is independent of the active slot.
    /// </summary>
    public string? EncyclopediaItemId { get; private set; }

    /// <summary>
    /// Skill milestone whose detail card the right-hand pane shows. Mirrors the native
    /// SlotSidebarView's dedicated skill-milestone detail panel (ShowMilestoneDetail):
    /// tapping a milestone chip on the SKILLS tab surfaces the perk, its effect and the
    /// unlock requirement in the sidebar, not inline in the tab.
    /// </summary>
    public SkillMilestoneSelection? Milestone { get; private set; }

    /// <summary>
    /// Arbitrary detail content for the right-hand pane, the web equivalent of the native
    /// SlotSidebarView's typed detail panels (quest flag, door, trader, story chapter, ...).
    /// The owning tab supplies the markup; the pane only hosts it. <see cref="DetailKey"/>
    /// identifies the shown subject so a second tap on the same row can close it.
    /// </summary>
    public RenderFragment? DetailContent { get; private set; }
    public string? DetailKey { get; private set; }

    /// <summary>Whether the pane has anything to surface (an editable slot or a detail card).</summary>
    public bool HasAnySelection => Current is not null || EncyclopediaItemId is not null || Milestone is not null || DetailContent is not null;

    /// <summary>
    /// Surfaces tab-supplied detail content in the right-hand pane (native SlotSidebarView
    /// detail panels). Re-showing the same <paramref name="key"/> refreshes the content;
    /// callers wanting tap-to-toggle should check <see cref="DetailKey"/> first.
    /// </summary>
    public void ShowDetail(string key, RenderFragment content)
    {
        DetailKey = key;
        DetailContent = content;
        EncyclopediaItemId = null;
        Milestone = null;
        Changed?.Invoke();
    }

    /// <summary>Closes the tab-supplied detail card, leaving any slot selection intact.</summary>
    public void CloseDetail()
    {
        if (DetailContent is null) return;
        DetailContent = null;
        DetailKey = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// The surface whose tab currently warrants the sidebar ITEM CATALOG even with no slot
    /// selected. Mirrors the native <c>MainViewModel.ShowItemPalette</c>: the palette shows
    /// on the player INVENTORY and TRANSMOG tabs whenever a player save is open, and its
    /// quick-give fallback places items without requiring a selection first.
    /// </summary>
    public PaletteContext? Palette { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Raised when a slot's item identity actually changes (an item is placed, swapped, cleared
    /// or taken from the catalog), as opposed to a refinement like nudging a count. On a phone
    /// the workbench uses this to close the slot editor sheet so the result is visible on the
    /// list underneath; refining a count or durability deliberately does not raise it, so the
    /// sheet stays put while you keep tuning the same slot.
    /// </summary>
    public event Action? ItemCommitted;

    /// <summary>Signals that an item was placed, swapped, cleared or taken. See <see cref="ItemCommitted"/>.</summary>
    public void NotifyItemCommitted() => ItemCommitted?.Invoke();

    /// <summary>Announces (or clears, with null) the ambient palette surface for the active tab.</summary>
    public void SetPaletteContext(PaletteContext? context)
    {
        if (ReferenceEquals(Palette, context)) return;
        if (Palette is not null && context is not null
            && ReferenceEquals(Palette.Session, context.Session) && Palette.Area == context.Area) return;
        Palette = context;
        Changed?.Invoke();
    }

    public void Select(InventorySelection selection)
    {
        Current = selection;
        Changed?.Invoke();
    }

    /// <summary>Surfaces an item's encyclopedia card. No-op without an id, like native.</summary>
    public void ShowEncyclopedia(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        EncyclopediaItemId = itemId;
        Milestone = null;
        DetailContent = null;
        DetailKey = null;
        Changed?.Invoke();
    }

    /// <summary>Surfaces a skill milestone's detail card (native ShowMilestoneDetail).</summary>
    public void ShowMilestone(SkillMilestoneSelection milestone)
    {
        Milestone = milestone;
        EncyclopediaItemId = null;
        DetailContent = null;
        DetailKey = null;
        Changed?.Invoke();
    }

    /// <summary>Closes the milestone detail card, leaving any slot selection intact.</summary>
    public void CloseMilestone()
    {
        if (Milestone is null) return;
        Milestone = null;
        Changed?.Invoke();
    }

    /// <summary>Closes the encyclopedia card, leaving any slot selection intact. Used when
    /// the tab that opened it is left (native: the card lives inside the item palette, so
    /// it disappears with the palette).</summary>
    public void CloseEncyclopedia()
    {
        if (EncyclopediaItemId is null) return;
        EncyclopediaItemId = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Current is null && EncyclopediaItemId is null && Milestone is null && DetailContent is null) return;
        Current = null;
        EncyclopediaItemId = null;
        Milestone = null;
        DetailContent = null;
        DetailKey = null;
        Changed?.Invoke();
    }
}

/// <summary>The milestone the sidebar's skill detail panel shows (native SelectedMilestone).</summary>
public sealed record SkillMilestoneSelection(PlayerSkillEdit Skill, SkillMilestone Milestone);

public sealed record InventorySelection(
    string Area,
    PlayerInventorySlotEdit Slot,
    IReadOnlyList<PlayerInventorySlotEdit> Slots,
    IReadOnlyList<string> ItemVocabulary,
    IReadOnlyList<WorldDeployable> Benches,
    IReadOnlyList<PlayerInventorySlotEdit> DismantleDestinations,
    bool ReuseSourceForDismantle,
    Func<Task> NotifyChanged,
    Func<(int First, int Second), Task> Swap,
    Func<(int Index, bool Downgrade), Task> Upgrade,
    Func<PlayerInventorySlotEdit, bool>? CanUpgrade,
    Func<PlayerInventorySlotEdit, bool>? CanDowngrade)
{
    /// <summary>The player inventory area the slot lives in; null for non-player surfaces
    /// (world container groups), which validate like Main/storage slots.</summary>
    public PlayerInventoryArea? PlayerArea { get; init; }

    /// <summary>The equipment/transmog role of the selected position (HEAD, SHIELD, ...),
    /// or null. Drives the sidebar palette's FITS SLOT filter and drop validation.</summary>
    public string? Role { get; init; }

    /// <summary>
    /// Native quick-give fallback for when the selected slot is occupied: the owning
    /// surface picks the destination (first empty backpack slot, then hotbar; hotbar only
    /// for pets; first empty container slot for world containers). Null when the surface
    /// has no sensible fallback.
    /// </summary>
    public Func<bool, QuickGiveTarget?>? QuickGiveFallback { get; init; }

    /// <summary>Selects a sibling slot on the owning surface (native vm.SelectSlot), so a
    /// palette give/drop can focus the slot it filled.</summary>
    public Action<PlayerInventorySlotEdit>? SelectSlot { get; init; }
}

/// <summary>A quick-give destination with the metadata its placement validation needs.</summary>
public sealed record QuickGiveTarget(PlayerInventorySlotEdit Slot, PlayerInventoryArea? Area, string? Role);

/// <summary>
/// The ambient sidebar-palette surface for the active editor tab (native ShowItemPalette):
/// which player session the palette gives items to, the tab it represents, and the
/// no-selection quick-give fallback (first empty backpack slot, hotbar for pets).
/// </summary>
public sealed record PaletteContext(
    PlayerSaveSession Session,
    PlayerInventoryArea Area,
    Func<bool, QuickGiveTarget?> QuickGiveFallback,
    Func<Task> NotifyChanged);
