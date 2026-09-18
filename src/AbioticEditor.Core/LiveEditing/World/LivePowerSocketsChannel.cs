namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live power-socket reading: the "Power Sockets" feature's live twin (the same actors
/// <c>Core/WorldSaves/Services/WorldMapFeatures/PowerSocketMapFeature.cs</c> edits in the save's
/// <c>PowerSocketMap</c>). Lists every loaded socket actor found by a hierarchy sweep of
/// <c>PowerSocket_ParentBP_C</c> (every current and future subclass, no class name hardcoded on
/// this side or the Lua side) - see <c>powersockets.list</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/powersockets.lua</c> for the full
/// discovery and field-mapping citations.
///
/// <para><b>Read-only.</b> Confirmed against the coordinator's CUE4Parse class dump and
/// <c>PowerSocket_ParentBP_C</c>'s own blueprint bytecode: the offline feature's one editable
/// field, <c>hasTimer</c>, has no live-settable path at all. The only function that ever persists
/// a socket's state (<c>Update_SaveData</c>, called from <c>SavePowerSocketToWorldSave</c>)
/// unconditionally resets both <c>HasTimer_</c> and <c>TimerMode_</c> to <c>false</c>/<c>0</c> in
/// both branches of its own bytecode, so nothing this channel could write would survive the next
/// time anything triggers a save on that socket. <see cref="LivePowerSocket.HasTimer"/>/
/// <see cref="LivePowerSocket.TimerMode"/> are read-only for that reason, not because reading
/// failed; a null value there specifically means this actor's <c>LatestSaveData</c> struct could
/// not be read right now (an unfamiliar subclass, or the actor unloaded mid-request).</para>
/// </summary>
public sealed class LivePowerSocketsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LivePowerSocketDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("powersockets.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var sockets = (wire.Sockets ?? [])
            .Select(s => new LivePowerSocket(s.Id, s.Label, s.SocketId, s.PluggedInDevice, s.HasTimer, s.TimerMode,
                s.Powered, s.X, s.Y, s.Z))
            .ToList();
        return new LivePowerSocketDirectory(sockets, wire.IsHost);
    }

    private sealed record DirectoryWire(IReadOnlyList<SocketWire>? Sockets, bool IsHost);
    private sealed record SocketWire(string Id, string Label, string? SocketId, string? PluggedInDevice,
        bool? HasTimer, int? TimerMode, bool? Powered, double X, double Y, double Z);
}

/// <summary>One loaded power-socket actor. <paramref name="Id"/> is the game's full object name
/// for this exact actor; <paramref name="SocketId"/> is the actor's own <c>GetPowerSocketID()</c>
/// value (the same id space the save's <c>PowerSocket_&lt;hash&gt;</c> leaf stores - read-only
/// here regardless). <paramref name="PluggedInDevice"/> is a plain description of the currently
/// plugged-in device's own class ("nothing plugged in" when the socket is free) - resolved from
/// the live actor reference, not the save's asset-id string, so it needs no game-data catalog.
/// <paramref name="HasTimer"/>/<paramref name="TimerMode"/> are read-only (see the class remarks);
/// <paramref name="Powered"/> is a bonus read-only field off the confirmed <c>IsPowered()</c>
/// function. Every nullable field is null specifically when it could not be read off this actor
/// right now, never a guessed default.</summary>
public sealed record LivePowerSocket(string Id, string Label, string? SocketId, string? PluggedInDevice,
    bool? HasTimer, int? TimerMode, bool? Powered, double X, double Y, double Z);

/// <summary>Every loaded power socket plus whether this process has host authority (irrelevant
/// today since nothing here is settable, kept for shape symmetry with every other live area).</summary>
public sealed record LivePowerSocketDirectory(IReadOnlyList<LivePowerSocket> Sockets, bool IsHost);
