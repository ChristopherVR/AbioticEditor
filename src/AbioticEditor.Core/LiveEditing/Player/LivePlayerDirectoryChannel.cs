namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Lists every player connected to the live game (not just the local one) and says whether this
/// process currently has authority over its own player - see <c>players.list</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c> for the mod-side implementation,
/// built on UE4SS's own <c>UEHelpers.GetAllPlayerStates()</c> (backed by the engine's
/// <c>AGameStateBase.PlayerArray</c>, so this works identically whether this process is hosting
/// or has joined someone else's game).
/// </summary>
public sealed class LivePlayerDirectoryChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the current player list and host/client authority status.</summary>
    public async Task<LivePlayerDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("players.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var players = wire.Players
            .Select(p => new LivePlayerSummary(p.Id, p.Name, p.IsLocal))
            .ToList();
        return new LivePlayerDirectory(players, wire.IsHost);
    }

    private sealed record DirectoryWire(IReadOnlyList<PlayerWire> Players, bool IsHost);
    private sealed record PlayerWire(string Id, string Name, bool IsLocal);
}

/// <summary>One connected player, as listed by <see cref="LivePlayerDirectoryChannel"/>.</summary>
/// <param name="Id">A stable id for this protocol (the game's own per-player id where available)
/// - pass this back as the <c>playerId</c> argument to vitals/skills channel calls to target this
/// player instead of the local one.</param>
/// <param name="Name">The player's in-game display name.</param>
/// <param name="IsLocal">Whether this is the player this process is running as.</param>
public sealed record LivePlayerSummary(string Id, string Name, bool IsLocal);

/// <summary>
/// The full result of <see cref="LivePlayerDirectoryChannel.GetAsync"/>: who is connected, and
/// whether editing here is expected to actually stick. <see cref="IsHost"/> mirrors
/// <c>AActor::HasAuthority()</c> on the local player's own pawn (the same check a real published
/// UE4SS mod for this game uses to decide whether a direct property write will be overwritten by
/// replication) - true for a locally hosted or singleplayer game, false when this process has
/// joined someone else's game as a client. Nothing this editor currently writes (vitals, skills)
/// needs authority to stick - that same reference mod calls those exact kinds of writes
/// unconditionally on any client - so this is surfaced for transparency today, not to gate
/// anything, and only matters if a future live-editable area touches a movement/physics property.
/// </summary>
public sealed record LivePlayerDirectory(IReadOnlyList<LivePlayerSummary> Players, bool IsHost);

/// <summary>Shared wire shape for "target this player instead of the local one" - the same shape
/// every live-editing command accepts when payload-carrying, and the entire payload when a
/// command carries nothing else (see <c>vitals.get</c>/<c>skills.get</c>).</summary>
internal sealed record PlayerIdWire(string PlayerId);
