using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s companions slice: implements the
/// same <see cref="IPlayerCompanionsSession"/> boundary <c>PlayerCompanionsTab.razor</c> already
/// binds to, reusing the exact same <see cref="CarriedPetEdit"/> row type. Every occupied slot the
/// live agent reports is filtered down to actual pets here (<see cref="PetItemCatalog.IsPetItem"/>,
/// or the Companion equipment slot regardless of whether the catalog recognises the row - the same
/// rule <c>PlayerSaveReader.ReadCarriedPetsFrom</c> uses), since the Lua side has no game-data
/// catalog of its own.
/// </summary>
public sealed class LivePlayerCompanionsSession : IPlayerCompanionsSession
{
    private readonly LiveCompanionsChannel _channel;
    private string? _playerId;
    private List<CarriedPetEdit> _pets = [];

    private LivePlayerCompanionsSession(LiveCompanionsChannel channel, string? playerId)
    {
        _channel = channel;
        _playerId = playerId;
    }

    public static async Task<LivePlayerCompanionsSession> ConnectAsync(
        LiveCompanionsChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var session = new LivePlayerCompanionsSession(channel, playerId);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public IReadOnlyList<CarriedPetEdit> CarriedPets => _pets;
    public string SessionKey => _playerId ?? "local";
    public bool SupportsWorldIntegration => false;
    public bool AppliesImmediately => true;

    /// <summary>Always false: every row shown was either just read from the game or already
    /// applied by <see cref="ApplyPetAsync"/>. This is what the periodic live refresh loop checks
    /// before calling <see cref="RefreshAsync"/> so a refresh never clobbers an edit still in
    /// flight.</summary>
    public bool IsDirty => false;
    public string? Status { get; private set; }
    public void MarkChanged() { }
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Revert() { }

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the running game, and after every
    /// mutation below applies - lets a bound UI (the COMPANIONS tab) redraw without polling this
    /// object itself.</summary>
    public event Action? Changed;

    /// <summary>Re-reads every carried pet from the running game, discarding local UI state for
    /// any row not currently mid-edit (there is nothing staged to lose - see <see cref="AppliesImmediately"/>).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _channel.ListAsync(_playerId, cancellationToken).ConfigureAwait(false);
        _pets = rows
            .Where(row => PetItemCatalog.IsPetItem(row.ItemId) || (row.Kind == "equip" && row.SlotIndex == 12))
            .Select(row => new CarriedPetEdit(new CarriedPet(
                LiveCompanionsChannel.FromWireKind(row.Kind), row.SlotIndex, row.ItemId,
                string.IsNullOrEmpty(row.Name) ? null : row.Name, row.Health, row.MaxHealth,
                row.Xp, row.MutationProgress, row.PetMutation)))
            .ToList();
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Writes <paramref name="pet"/>'s current field values to its slot immediately.</summary>
    public async Task ApplyPetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        await _channel.SetAsync(LiveCompanionsChannel.ToWireKind(pet.Slot), pet.Index, pet.ToCarriedPet(), _playerId, cancellationToken)
            .ConfigureAwait(false);
        pet.AcceptCurrentAsBaseline();
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Clears <paramref name="pet"/>'s slot immediately and drops it from
    /// <see cref="CarriedPets"/> - there is no undo, unlike the file session's staged removal.
    ///
    /// Round 78 (reported live: "removing a pet from a player leaves the pet standing next to
    /// them, unable to be picked up"): the Companion equipment slot
    /// (<see cref="CarriedPet.IsCompanionSlot"/>) used to be refused outright, because clearing it
    /// only ever wrote the inventory slot struct back to "Empty" and left the game's own live
    /// follower actor - the one it visibly spawns for that slot - standing there, desynced from
    /// its now-empty backing item. <c>LiveClassPropsProbe</c>'s class dump found the fix instead:
    /// Pest/Skink-family NPCs carry their own <c>FollowingOwner</c> reference, so
    /// <c>companions.lua</c> can now find the actual matching follower and destroy it
    /// (<c>K2_DestroyActor</c>, the same standard actor-destroy call the reference
    /// CheatConsoleCommands mod's own "deleteobject" command already uses) before clearing the
    /// slot - see that file's own remarks. Round 79 re-checked whether Peccary/Lamogi could be
    /// added too, against the installed game's own class data: confirmed (not guessed) that
    /// neither family exposes <c>FollowingOwner</c> or any other owner-identity field anywhere in
    /// their class hierarchy, so this stays a Pest/Skink-only match - a real, verified limit of
    /// the current game build. <see cref="LiveClearResult.DespawnedFollower"/> says whether a
    /// match was found, so <see cref="Status"/> says so rather than claiming success it can't back
    /// up. A pet merely carried in the hotbar/backpack (not the active follower) has no such live
    /// actor, so clearing those slots is unaffected either way.</summary>
    public async Task RemovePetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        var result = await _channel.ClearAsync(LiveCompanionsChannel.ToWireKind(pet.Slot), pet.Index, _playerId, cancellationToken)
            .ConfigureAwait(false);
        _pets.Remove(pet);
        Status = pet.IsCompanionSlot && !result.DespawnedFollower
            ? "Removed live - but this pet's live follower couldn't be matched to despawn automatically " +
              "(only Pest- and Skink-family companions can be, confirmed against the game's own class " +
              "data); if it's still following you in-game, dismiss it there too."
            : "Removed live - this took effect in the running game immediately.";
        Changed?.Invoke();
    }

    /// <summary>Switches which connected player this session reads/acts on and re-reads immediately.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
