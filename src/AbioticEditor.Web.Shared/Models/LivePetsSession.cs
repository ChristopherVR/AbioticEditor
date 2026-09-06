using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live PETS "editing" session - implements <see cref="IWorldPetsSession"/> purely to report
/// that it isn't available. See <c>areas/pets.lua</c>'s own comment for the research finding:
/// tame/name/health data is exposed wildly inconsistently between creature families, with no
/// safe way to match a live actor back to a world save's <c>PetNPC</c> GUID. Every mutator here
/// throws <see cref="NotSupportedException"/>; the shared <c>WorldPetsTab</c> checks
/// <see cref="IsAvailable"/> first and never calls them.
/// </summary>
public sealed class LivePetsSession : IWorldPetsSession
{
    private LivePetsSession(LivePetDirectory directory)
    {
        IsHost = directory.IsHost;
        UnavailableReason = directory.Reason;
    }

    public static async Task<LivePetsSession> ConnectAsync(
        LivePetsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LivePetsSession(directory);
    }

    public IReadOnlyList<WorldPet> Pets => [];
    public bool IsHost { get; }
    public bool IsAvailable => false;
    public string? UnavailableReason { get; }

    bool IWorldPetsSession.AppliesImmediately => true;

    Task IWorldPetsSession.SetPetAsync(string id, bool isDead, string? npcClass, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken)
        => throw new NotSupportedException(UnavailableReason ?? "Live pet editing isn't available.");

    Task IWorldPetsSession.RemovePetAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException(UnavailableReason ?? "Live pet editing isn't available.");

    Task<bool> IWorldPetsSession.RestorePetAsync(WorldPet pet, CancellationToken cancellationToken)
        => throw new NotSupportedException(UnavailableReason ?? "Live pet editing isn't available.");
}
