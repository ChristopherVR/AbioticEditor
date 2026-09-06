namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live counterpart of the player save's respawn slice (<c>Core/Domain/Player/PlayerSaveData</c>'s
/// <c>RespawnX/Y/Z</c>, <c>RespawnLevelGuid</c>, <c>TerminalRespawnId</c>): reads the connected
/// character's actual current position and claimed respawn terminal from the running game, and
/// lets an explicit action move the character there (a real teleport) or claim a different
/// punch-card terminal as the respawn point - see <c>spawn.get</c>/<c>spawn.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/spawn.lua</c>.
///
/// Unlike a file edit, nothing here is ever staged: every write is one deliberate, immediate
/// action (see <c>docs/reference/live-editing-protocol.md</c> "Never move the player automatically").
/// </summary>
public sealed class LiveSpawnChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the connected character's current live position, active level name (for
    /// display) and claimed respawn terminal id, for <paramref name="playerId"/> (or the local
    /// player when omitted).</summary>
    public async Task<LiveSpawnState> GetAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<StateWire>("spawn.get", payload, cancellationToken).ConfigureAwait(false);
        return new LiveSpawnState(wire.X, wire.Y, wire.Z, wire.LevelName, wire.TerminalGuid, wire.IsHost);
    }

    /// <summary>Teleports the character to <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/>
    /// immediately (<c>AAbiotic_PlayerCharacter_C:TeleportPlayer</c>, the exact call
    /// <c>AFUtils.TeleportPlayerToPlayer</c>/<c>LocationsManager.LoadLocation</c> already use).</summary>
    public Task TeleportAsync(double x, double y, double z, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("spawn.set",
            new SetWire(new TeleportWire(x, y, z), null, playerId), cancellationToken);

    /// <summary>Claims a different punch-card terminal (a <c>RespawnTerminalCatalog</c> guid,
    /// see <c>Core/Catalogs/Player/RespawnTerminalCatalog.cs</c>) as this character's respawn
    /// point immediately, writing the controller's own
    /// <c>TerminalRespawnID</c> field directly (found in the game's own class layout; no
    /// reference-mod command exercises it, unlike teleport).</summary>
    public Task SetRespawnTerminalAsync(string terminalGuid, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("spawn.set",
            new SetWire(null, terminalGuid, playerId), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record StateWire(double X, double Y, double Z, string? LevelName, string? TerminalGuid, bool IsHost);
    private sealed record TeleportWire(double X, double Y, double Z);
    private sealed record SetWire(TeleportWire? Teleport, string? TerminalGuid, string? PlayerId);
}

/// <summary>The running game's read of one character's position and claimed respawn terminal, as
/// returned by <see cref="LiveSpawnChannel.GetAsync"/>.</summary>
/// <param name="LevelName">The controller's own <c>ActiveLevelName</c> (a display-only streaming
/// level name, not the save's region guid - live has no direct equivalent of
/// <c>RespawnLevelGuid</c>, only the terminal id).</param>
/// <param name="TerminalGuid">The claimed respawn terminal's <c>TerminalRespawnID</c>, or null when
/// none is set or it could not be read.</param>
public sealed record LiveSpawnState(double X, double Y, double Z, string? LevelName, string? TerminalGuid, bool IsHost);
