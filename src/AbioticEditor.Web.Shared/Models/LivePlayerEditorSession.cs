using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live-editing's <see cref="IPlayerEditorSession"/>: a thin, mutable aggregate of whichever
/// per-area live sessions <c>LiveConnect.razor</c> has connected so far, so <c>PlayerEditor.razor</c>
/// can host live editing's player tabs exactly the way it already hosts the file editor's -
/// see that interface's own doc comment for why this exists at all.
///
/// Each area connects lazily, the first time its own tab is opened (see
/// <c>LiveConnect.razor.EnsureAreaConnectedAsync</c>) rather than all at once - a real game crash
/// was once traced to firing every area's connect the instant any connection succeeded, so this
/// must never be "eagerly" populated. <c>LiveConnect.razor</c> sets each property below only once
/// the matching area has actually connected, gating the corresponding tab's render behind
/// <c>PlayerEditor.razor</c>'s <c>LoadingTabId</c> until then - every member here still needs a
/// safe fallback regardless, for the moment between a tab opening and its connect completing.
/// </summary>
public sealed class LivePlayerEditorSession : IPlayerEditorSession
{
    public IPlayerVitalsSession? VitalsSession { get; set; }
    public IPlayerSkillsSession? SkillsSession { get; set; }
    public IPlayerSpawnSession? SpawnSession { get; set; }
    public IPlayerCompanionsSession? CompanionsSession { get; set; }
    public IPlayerTransmogSession? InventorySession { get; set; }
    public IPlayerRecipesSession? RecipesSession { get; set; }
    public IPlayerCodexSession? CodexSession { get; set; }
    public IPlayerGeneralSession? GeneralSession { get; set; }

    // ---- IPlayerVitalsSession / IPlayerInventorySession (both declare Vitals identically) ----
    public PlayerVitals Vitals => VitalsSession?.Vitals ?? InventorySession?.Vitals ?? new PlayerVitals();

    // ---- IsDirty/Status/SaveAsync/Revert: IPlayerVitalsSession, IPlayerSkillsSession and
    // IPlayerSpawnSession all declare these identically, so one implementation answers all three.
    // Nothing in PlayerEditor.razor's own rendering actually reads these through this composite
    // today (PlayerVitalsTab takes only Session.Vitals; LiveConnect.razor's own debounced
    // auto-save and background refresh - see ScheduleAutoSave/RefreshActiveVitalsOrSkillsAsync -
    // read/call the underlying _vitals/_skills fields directly instead, since only those two
    // areas need it) - kept pointed at SkillsSession as the one area whose own tab
    // (PlayerSkillsTab) does read Session.IsDirty/Status directly.
    public bool IsDirty => SkillsSession?.IsDirty ?? false;
    public string? Status => SkillsSession?.Status ?? InventorySession?.Status;
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
        SkillsSession?.SaveAsync(cancellationToken) ?? ValueTask.CompletedTask;
    public void Revert() => SkillsSession?.Revert();

    // ---- IPlayerSkillsSession ----
    public IReadOnlyList<PlayerSkillEdit> Skills => SkillsSession?.Skills ?? [];
    public void MaxAllSkills() => SkillsSession?.MaxAllSkills();

    // ---- MarkChanged: shared verbatim by IPlayerSkillsSession/IPlayerSpawnSession/
    // IPlayerCompanionsSession/IPlayerInventorySession/IPlayerRecipesSession/IPlayerCodexSession -
    // every live area already applies immediately and treats this as a no-op refresh signal, so
    // fanning it out to all of them keeps whichever one the active tab actually cares about correct.
    public void MarkChanged()
    {
        SkillsSession?.MarkChanged();
        SpawnSession?.MarkChanged();
        CompanionsSession?.MarkChanged();
        InventorySession?.MarkChanged();
        RecipesSession?.MarkChanged();
        CodexSession?.MarkChanged();
    }

    // ---- IPlayerSpawnSession ----
    public PlayerRespawnEdit Respawn => SpawnSession?.Respawn ?? InventorySession?.Respawn ?? new PlayerRespawnEdit(0, 0, 0, null, null);
    public string SessionKey => SpawnSession?.SessionKey ?? CompanionsSession?.SessionKey ?? string.Empty;
    public bool SupportsWorldIntegration => false;
    public bool SupportsLiveActions => SpawnSession?.SupportsLiveActions ?? false;
    public (double X, double Y, double Z)? LivePosition => SpawnSession?.LivePosition;
    // Without these two, PlayerSpawnTab.razor's TELEPORT ME HERE / SET AS MY RESPAWN POINT
    // buttons - bound to THIS facade, never directly to LivePlayerSpawnSession, see the class
    // remarks - silently did nothing: they used to reach the real session through a
    // `Session is LivePlayerSpawnSession` type check, which is never true for this wrapper.
    public Task TeleportAsync(CancellationToken cancellationToken = default) =>
        SpawnSession?.TeleportAsync(cancellationToken) ?? Task.CompletedTask;
    public Task ClaimRespawnTerminalAsync(CancellationToken cancellationToken = default) =>
        SpawnSession?.ClaimRespawnTerminalAsync(cancellationToken) ?? Task.CompletedTask;

    // ---- IPlayerCompanionsSession ----
    public IReadOnlyList<CarriedPetEdit> CarriedPets => CompanionsSession?.CarriedPets ?? [];
    public bool AppliesImmediately => true;
    public Task ApplyPetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default) =>
        CompanionsSession?.ApplyPetAsync(pet, cancellationToken) ?? Task.CompletedTask;
    public Task RemovePetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default) =>
        CompanionsSession?.RemovePetAsync(pet, cancellationToken) ?? Task.CompletedTask;
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        CompanionsSession?.RefreshAsync(cancellationToken) ?? Task.CompletedTask;

    // ---- IPlayerInventorySession / IPlayerTransmogSession ----
    public IReadOnlyList<PlayerInventorySlotEdit> Equipment => InventorySession?.Equipment ?? [];
    public IReadOnlyList<PlayerInventorySlotEdit> Hotbar => InventorySession?.Hotbar ?? [];
    public IReadOnlyList<PlayerInventorySlotEdit> Backpack => InventorySession?.Backpack ?? [];
    public IReadOnlyList<PlayerInventorySlotEdit> Transmog => InventorySession?.Transmog ?? [];
    public IReadOnlyList<string> ItemVocabulary => InventorySession?.ItemVocabulary ?? [];
    public ItemUpgradeCatalog ItemUpgrades => InventorySession?.ItemUpgrades ?? ItemUpgradeCatalog.Empty;
    public bool SupportsCompleteItemWrites => InventorySession?.SupportsCompleteItemWrites ?? false;
    public string? SteamIdentifier => null;
    public string Path => string.Empty;
    public IReadOnlyList<TransmogVisibilityEdit> TransmogVisibility => InventorySession?.TransmogVisibility ?? [];
    public Task SetTransmogVisibilityAsync(int index, bool isVisible) =>
        InventorySession?.SetTransmogVisibilityAsync(index, isVisible) ?? Task.CompletedTask;
    public bool TryGetInventorySlot(PlayerInventoryArea area, int index, out InventoryItemSlot slot)
    {
        if (InventorySession is { } inventory) return inventory.TryGetInventorySlot(area, index, out slot);
        slot = new InventoryItemSlot(index, null, 0, 0, 0, 0, 0, null, false, null, null);
        return false;
    }
    public bool TrySetInventorySlot(PlayerInventoryArea area, int index, InventoryItemSlot slot) =>
        InventorySession?.TrySetInventorySlot(area, index, slot) ?? false;
    public ValueTask PushSlotAsync(PlayerInventoryArea area, PlayerInventorySlotEdit slot, CancellationToken cancellationToken = default) =>
        InventorySession?.PushSlotAsync(area, slot, cancellationToken) ?? ValueTask.CompletedTask;
    public ValueTask<bool> TrySwapInventorySlotsAsync(PlayerInventoryArea firstArea, int firstIndex,
        PlayerInventoryArea secondArea, int secondIndex, CancellationToken cancellationToken = default) =>
        InventorySession?.TrySwapInventorySlotsAsync(firstArea, firstIndex, secondArea, secondIndex, cancellationToken) ?? ValueTask.FromResult(false);
    public ValueTask SortInventorySlotsAsync(PlayerInventoryArea area, CancellationToken cancellationToken = default) =>
        InventorySession?.SortInventorySlotsAsync(area, cancellationToken) ?? ValueTask.CompletedTask;
    public ValueTask<bool> TryApplyItemUpgradeAsync(PlayerInventoryArea area, int index, bool downgrade, CancellationToken cancellationToken = default) =>
        InventorySession?.TryApplyItemUpgradeAsync(area, index, downgrade, cancellationToken) ?? ValueTask.FromResult(false);
    // Without these two overrides, PlayerInventoryTab.razor's Session parameter (bound to THIS
    // facade, never directly to LiveInventorySession - see the class remarks) would see the
    // IPlayerInventorySession interface's own default members (false / no-op) instead of the real
    // live session's, since default interface members are not delegated automatically just because
    // InventorySession itself overrides them. That silently disabled the live DROP ITEM path this
    // facade exists to expose.
    public bool SupportsLiveDrop => InventorySession?.SupportsLiveDrop ?? false;
    public ValueTask<bool> TryDropSlotLiveAsync(PlayerInventoryArea area, int index, CancellationToken cancellationToken = default) =>
        InventorySession?.TryDropSlotLiveAsync(area, index, cancellationToken) ?? ValueTask.FromResult(false);
    // Same facade-delegation trap as the two overrides above: PlayerInventoryTab.razor's Session
    // parameter is bound to THIS facade, so without an explicit forward it would only ever see
    // IPlayerInventorySession's own default no-op event, never the live event the real
    // LiveInventorySession already raises after a cross-tab write (a container-to-player transfer
    // started from WorldContainersTab). Reported live as "moving an item into a player's inventory
    // doesn't show up on their inventory screen" - the write reached the game, this tab just never
    // heard about it. Connects lazily: by the time PlayerInventoryTab subscribes, InventorySession
    // is already set (see the class remarks on connect ordering), so this forwards to whichever
    // instance is live at subscribe time.
    public event Action? Changed
    {
        add { if (InventorySession is not null) InventorySession.Changed += value; }
        remove { if (InventorySession is not null) InventorySession.Changed -= value; }
    }

    // ---- IPlayerRecipesSession ----
    public IReadOnlyList<PlayerRecipeEdit> Recipes => RecipesSession?.Recipes ?? [];
    public int UnlockedRecipeCount => RecipesSession?.UnlockedRecipeCount ?? 0;
    public int RecipeCount => RecipesSession?.RecipeCount ?? 0;
    public bool CanLock => RecipesSession?.CanLock ?? false;
    public void EnsureRecipeRows(IEnumerable<string> ids) => RecipesSession?.EnsureRecipeRows(ids);
    public Task SetUnlockedAsync(string recipeId, bool unlocked) =>
        RecipesSession?.SetUnlockedAsync(recipeId, unlocked) ?? Task.CompletedTask;
    public Task SetUnlockedManyAsync(IEnumerable<string> recipeIds) =>
        RecipesSession?.SetUnlockedManyAsync(recipeIds) ?? Task.CompletedTask;

    // ---- IPlayerCodexSession ----
    public IReadOnlyList<CodexRowEdit> Emails => CodexSession?.Emails ?? [];
    public IReadOnlyList<CodexRowEdit> Journals => CodexSession?.Journals ?? [];
    public IReadOnlyList<CodexRowEdit> Compendium => CodexSession?.Compendium ?? [];
    public IReadOnlyList<CodexRowEdit> Fish => CodexSession?.Fish ?? [];
    public bool CanUnsetKnown => CodexSession?.CanUnsetKnown ?? false;
    public bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null) =>
        CodexSession?.ApplyCodexVocabulary(vocabulary, localize) ?? false;
    public Task SetKnownAsync(CodexRowEdit row, bool known) => CodexSession?.SetKnownAsync(row, known) ?? Task.CompletedTask;
    public Task SetKnownManyAsync(IEnumerable<CodexRowEdit> rows) =>
        CodexSession?.SetKnownManyAsync(rows) ?? Task.CompletedTask;

    // ---- IPlayerGeneralSession ----
    public string? OwnerId => GeneralSession?.OwnerId;
    public bool IsSteamOwnerId => GeneralSession?.IsSteamOwnerId ?? false;
    public bool CanChangeOwnerId => false;
    public IPlayerDiscoverySection ItemsSeen => GeneralSession?.ItemsSeen ?? EmptyDiscovery;
    public IPlayerDiscoverySection ItemsCrafted => GeneralSession?.ItemsCrafted ?? EmptyDiscovery;
    public IPlayerDiscoverySection Maps => GeneralSession?.Maps ?? EmptyDiscovery;
    public string? Background => GeneralSession?.Background;
    public bool CanChangeBackground => GeneralSession?.CanChangeBackground ?? false;
    public Task SetBackgroundAsync(string? background) => GeneralSession?.SetBackgroundAsync(background) ?? Task.CompletedTask;
    IReadOnlyList<string> IPlayerGeneralSession.Traits => GeneralSession?.Traits ?? [];
    public bool CanEditTraits => GeneralSession?.CanEditTraits ?? false;
    public IPlayerAppearanceSession? Appearance => GeneralSession?.Appearance;
    public Task SetTraitAsync(string id, bool enabled, string? buffRowName = null) =>
        GeneralSession?.SetTraitAsync(id, enabled, buffRowName)
        ?? Task.FromException(new InvalidOperationException("The live character session is unavailable."));


    private static readonly IPlayerDiscoverySection EmptyDiscovery =
        new DelegateDiscoverySection(() => Array.Empty<string>(), false, _ => Task.CompletedTask);
}
