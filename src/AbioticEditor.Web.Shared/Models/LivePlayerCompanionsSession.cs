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
    /// applied by <see cref="ApplyPetAsync"/>.</summary>
    public bool IsDirty => false;
    public string? Status { get; private set; }
    public void MarkChanged() { }
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Revert() { }

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
        Status = "Refreshed from the running game.";
    }

    /// <summary>Writes <paramref name="pet"/>'s current field values to its slot immediately.</summary>
    public async Task ApplyPetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        await _channel.SetAsync(LiveCompanionsChannel.ToWireKind(pet.Slot), pet.Index, pet.ToCarriedPet(), _playerId, cancellationToken)
            .ConfigureAwait(false);
        pet.AcceptCurrentAsBaseline();
        Status = "Applied live - this took effect in the running game immediately.";
    }

    /// <summary>Clears <paramref name="pet"/>'s slot immediately and drops it from
    /// <see cref="CarriedPets"/> - there is no undo, unlike the file session's staged removal.</summary>
    public async Task RemovePetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        await _channel.ClearAsync(LiveCompanionsChannel.ToWireKind(pet.Slot), pet.Index, _playerId, cancellationToken)
            .ConfigureAwait(false);
        _pets.Remove(pet);
        Status = "Removed live - this took effect in the running game immediately.";
    }

    /// <summary>Switches which connected player this session reads/acts on and re-reads immediately.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
