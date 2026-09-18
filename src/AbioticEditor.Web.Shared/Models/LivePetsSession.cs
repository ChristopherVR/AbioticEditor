using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live PETS editing session, implementing the same <see cref="IWorldPetsSession"/> the file
/// session does so <c>WorldPetsTab</c> renders unchanged for either host. Round 77 replaced the
/// round-76 blanket "not available" with a partial one: Pest- and Skink-family pets are matched
/// to a stable id live (their own <c>Guid</c> field - see <c>areas/pets.lua</c>'s own research
/// comment). Round 105 stopped omitting Peccary/Lamogi (and any other tamed creature with no
/// <c>Guid</c>) entirely: they are now listed too, found by the game's own
/// <c>AbioticFunctionLibrary::IsTamedPet</c> check rather than a hardcoded class list, with
/// <see cref="WorldPet.Matched"/> false - see that record's remarks for exactly what stays
/// editable on those rows (health/alive-state, never name/XP). There is no live species change
/// (the game's own <c>GameMode.SpawnPet</c> needs an <c>FTransform</c> this project has no safe
/// construction precedent for - see <see cref="LivePetsChannel"/>'s remarks) -
/// <see cref="SupportsSpeciesChange"/> is always false so the shared tab hides that control.
/// Round 78 added real removal (<see cref="SupportsRemoval"/>, always true here, matched or not)
/// via <see cref="LivePetsChannel.RemoveAsync"/> - see that class's remarks.
/// </summary>
public sealed class LivePetsSession : IWorldPetsSession
{
    private readonly LivePetsChannel _channel;

    private LivePetsSession(LivePetsChannel channel, LivePetDirectory directory)
    {
        _channel = channel;
        Apply(directory);
    }

    public static async Task<LivePetsSession> ConnectAsync(
        LivePetsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LivePetsSession(channel, directory);
    }

    public IReadOnlyList<WorldPet> Pets { get; private set; } = [];
    public bool IsHost { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Always false: every row shown was either just read from the game or already
    /// applied by <see cref="SetPetAsync"/>/removal, so there is never a client-side staged copy.
    /// This is what the periodic live refresh loop checks before calling <see cref="RefreshAsync"/>
    /// so a refresh never clobbers an edit still in flight.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation below (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    private void Apply(LivePetDirectory directory)
    {
        Pets = directory.Pets
            .Select(p => new WorldPet(p.Id, p.IsDead, p.NpcClass, p.X, p.Y, p.Z, p.CustomName, p.LimbHealth, p.Xp,
                State: null, Matched: p.Matched))
            .ToList();
        IsHost = directory.IsHost;
        IsAvailable = directory.Available;
        UnavailableReason = directory.Reason;
        Changed?.Invoke();
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a pet change made in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    bool IWorldPetsSession.AppliesImmediately => true;
    bool IWorldPetsSession.SupportsSpeciesChange => false;
    bool IWorldPetsSession.SupportsRemoval => true;

    async Task IWorldPetsSession.SetPetAsync(string id, bool isDead, string? npcClass, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken)
    {
        // npcClass is accepted by the shared interface but ignored here: the tab's species
        // dropdown is hidden (SupportsSpeciesChange is false), so this is always the pet's own
        // current class, never a real change request.
        var result = await _channel.SetAsync(id, isDead, customName, xp, limbHealth, cancellationToken).ConfigureAwait(false);
        // Round 78: a field that couldn't be applied (most commonly: raising the level of a pet
        // that has never earned real XP, which can't be fabricated live - see pets.lua's own
        // remarks) is a WARNING, not a thrown exception, so the fields that DID apply (health,
        // name, dead) are never thrown away along with it, and the tab always refreshes to show
        // what actually happened instead of going stale.
        Status = result.Warnings.Count == 0 ? null : string.Join(" ", result.Warnings);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a pet by destroying its live actor - see <see cref="LivePetsChannel.RemoveAsync"/>'s
    /// remarks. There is no undo once this returns, unlike the file session's staged removal.</summary>
    async Task IWorldPetsSession.RemovePetAsync(string id, CancellationToken cancellationToken)
    {
        await _channel.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
        Status = "Removed live - this despawned the pet in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    Task<bool> IWorldPetsSession.RestorePetAsync(WorldPet pet, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "Removing a pet live can't be undone - its actor is already gone. Edit the save file instead if this was a mistake.");
}
