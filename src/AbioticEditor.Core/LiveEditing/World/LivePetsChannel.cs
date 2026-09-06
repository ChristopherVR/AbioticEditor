namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live pet editing: reports whether it's available (it never is, today) - see
/// <c>pets.list</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/pets.lua</c>, whose
/// own comment explains why: tame/name/health data is exposed wildly inconsistently between
/// creature families in the game's own class layout, with no safe way to match a live actor
/// back to a world save's <c>PetNPC</c> GUID. There is deliberately no <c>pets.set</c> - only
/// <see cref="GetAsync"/> exists here, always returning an empty, unavailable directory.
/// </summary>
public sealed class LivePetsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LivePetDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("pets.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LivePetDirectory(wire.IsHost, wire.Available, wire.Reason);
    }

    private sealed record DirectoryWire(bool IsHost, bool Available, string? Reason);
}

/// <summary>Always reports <see cref="Available"/> false today; <see cref="Reason"/> is the
/// player-safe explanation the shared PETS tab shows instead of an empty list.</summary>
public sealed record LivePetDirectory(bool IsHost, bool Available, string? Reason);
