namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-teleporter editing: the "World Teleporters" feature's live twin (the same fixed,
/// level-placed teleporter pads <c>Core/WorldSaves/Features/PortalMapFeature.cs</c> edits in the
/// save's <c>PortalMap</c>, toggling whether each is active/unlocked). Lists every loaded
/// <c>BP_Teleporter_ParentBP_C</c> and lets a host flip <c>active</c> - see
/// <c>portals.list</c>/<c>portals.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/portals.lua</c>. No installed mod
/// exercises this actor class; this is the first live write to it.
/// </summary>
public sealed class LivePortalsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LivePortalDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("portals.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var portals = (wire.Portals ?? [])
            .Select(p => new LivePortal(p.Id, p.Label, p.Active, p.TeleporterId, p.DestinationId, p.X, p.Y, p.Z))
            .ToList();
        return new LivePortalDirectory(portals, wire.IsHost);
    }

    /// <summary>Sets whether the teleporter with <paramref name="id"/> is active/unlocked.</summary>
    public Task SetActiveAsync(string id, bool active, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("portals.set",
            new SetWire([new EditWire(id, active)]), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<PortalWire>? Portals, bool IsHost);
    private sealed record PortalWire(string Id, string Label, bool Active, string TeleporterId, string DestinationId, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Portals);
    private sealed record EditWire(string Id, bool? Active);
}

/// <summary>One loaded fixed teleporter pad. <paramref name="Id"/> is the game's full object name
/// for this exact actor; <paramref name="TeleporterId"/>/<paramref name="DestinationId"/> are its
/// own (level-baked) linking ids, read-only here.</summary>
public sealed record LivePortal(string Id, string Label, bool Active, string TeleporterId, string DestinationId,
    double X, double Y, double Z);

/// <summary>Every loaded teleporter pad plus whether this process has host authority to change them.</summary>
public sealed record LivePortalDirectory(IReadOnlyList<LivePortal> Portals, bool IsHost);
