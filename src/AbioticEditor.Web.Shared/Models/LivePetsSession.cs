using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live PETS editing session, implementing the same <see cref="IWorldPetsSession"/> the file
/// session does so <c>WorldPetsTab</c> renders unchanged for either host. Round 77 replaced the
/// round-76 blanket "not available" with a partial one: only Pest- and Skink-family pets can be
/// matched to a stable id live (their own <c>Guid</c> field - see <c>areas/pets.lua</c>'s own
/// research comment); Peccary and Lamogi pets stay file-only. There is no live species change
/// (no confirmed despawn/respawn round trip for a living NPC - the file writer's class-change is
/// a plain field edit, but doing that live would desync the actor's actual blueprint class from
/// what the property claims) and no live removal - <see cref="SupportsSpeciesChange"/>/
/// <see cref="SupportsRemoval"/> are always false so the shared tab hides those controls.
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

    private void Apply(LivePetDirectory directory)
    {
        Pets = directory.Pets
            .Select(p => new WorldPet(p.Id, p.IsDead, p.NpcClass, p.X, p.Y, p.Z, p.CustomName, p.LimbHealth, p.Xp, State: null))
            .ToList();
        IsHost = directory.IsHost;
        IsAvailable = directory.Available;
        UnavailableReason = directory.Reason;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    bool IWorldPetsSession.AppliesImmediately => true;
    bool IWorldPetsSession.SupportsSpeciesChange => false;
    bool IWorldPetsSession.SupportsRemoval => false;

    async Task IWorldPetsSession.SetPetAsync(string id, bool isDead, string? npcClass, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken)
    {
        // npcClass is accepted by the shared interface but ignored here: the tab's species
        // dropdown is hidden (SupportsSpeciesChange is false), so this is always the pet's own
        // current class, never a real change request.
        await _channel.SetAsync(id, isDead, customName, xp, limbHealth, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    Task IWorldPetsSession.RemovePetAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "Removing a pet live isn't supported - there's no way to bring it back if that's wrong. Edit the save file instead.");

    Task<bool> IWorldPetsSession.RestorePetAsync(WorldPet pet, CancellationToken cancellationToken)
        => throw new NotSupportedException("Live pet removal cannot happen in the first place, so there is nothing to restore.");
}
