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

    public async Task AddPetAsync(string itemRow, PetSlotKind kind, string? name, CancellationToken cancellationToken = default)
    {
        await _channel.AddAsync(itemRow, kind, name, _playerId, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the running game, and after every
    /// mutation below applies - lets a bound UI (the COMPANIONS tab) redraw without polling this
    /// object itself.</summary>
    public event Action? Changed;

    /// <summary>Re-reads every carried pet from the running game, discarding local UI state for
    /// any row not currently mid-edit (there is nothing staged to lose - see <see cref="AppliesImmediately"/>).</summary>
    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a pet change made in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _channel.ListAsync(_playerId, cancellationToken).ConfigureAwait(false);
        var existing = _pets.ToDictionary(p => (p.Slot, p.Index));
        _pets = rows
            .Where(row => PetItemCatalog.IsPetItem(row.ItemId) || (row.Kind == "equip" && row.SlotIndex == 12))
            .Select(row =>
            {
                var source = new CarriedPet(LiveCompanionsChannel.FromWireKind(row.Kind), row.SlotIndex, row.ItemId,
                    string.IsNullOrEmpty(row.Name) ? null : row.Name, row.Health, row.MaxHealth,
                    row.Xp, row.MutationProgress, row.PetMutation);
                if (!existing.TryGetValue((source.Slot, source.Index), out var pet)) return new CarriedPetEdit(source);
                pet.LoadReadback(source);
                return pet;
            })
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
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears <paramref name="pet"/>'s slot immediately and drops it from
    /// <see cref="CarriedPets"/> - there is no undo, unlike the file session's staged removal.
    ///
    /// The agent destroys the player's exact Companion actor first, falling back to a
    /// FollowingOwner match for older game builds. The inventory notification refreshes the
    /// backing equipment state, so picking up a later companion does not retain a stale slot.
    /// </summary>
    public async Task RemovePetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        var result = await _channel.ClearAsync(LiveCompanionsChannel.ToWireKind(pet.Slot), pet.Index, _playerId, cancellationToken)
            .ConfigureAwait(false);
        _pets.Remove(pet);
        Status = pet.IsCompanionSlot && !result.DespawnedFollower
            ? "Removed from inventory. No active companion actor was found; refresh to check the game state."
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
