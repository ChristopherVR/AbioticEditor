using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s spawn slice: implements the same
/// <see cref="IPlayerSpawnSession"/> boundary <c>PlayerSpawnTab.razor</c> already binds to. Unlike
/// the file session there is nothing to stage - <see cref="Respawn"/> is a live snapshot, and the
/// two mutators below (<see cref="TeleportAsync"/>, <see cref="ClaimRespawnTerminalAsync"/>) each
/// push one deliberate change to the running game immediately, then re-read it. Editing
/// <see cref="Respawn"/>'s fields by hand never moves anyone - only pressing one of those two does.
/// </summary>
public sealed class LivePlayerSpawnSession : IPlayerSpawnSession
{
    private readonly LiveSpawnChannel _channel;
    private string? _playerId;
    // What the game last reported, so IsDirty can tell an in-progress pick (a bed, a terminal, a
    // typed coordinate) from nothing to protect - see IsDirty's own remarks.
    private PlayerRespawnEdit _lastKnownRespawn;

    private LivePlayerSpawnSession(LiveSpawnChannel channel, string? playerId)
    {
        _channel = channel;
        _playerId = playerId;
        Respawn = new PlayerRespawnEdit(0, 0, 0, null, null);
        _lastKnownRespawn = Respawn.Clone();
    }

    public static async Task<LivePlayerSpawnSession> ConnectAsync(
        LiveSpawnChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var session = new LivePlayerSpawnSession(channel, playerId);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public PlayerRespawnEdit Respawn { get; private set; }
    public string SessionKey => _playerId ?? "local";
    public bool SupportsWorldIntegration => false;
    public bool SupportsLiveActions => true;
    public (double X, double Y, double Z)? LivePosition { get; private set; }

    /// <summary>
    /// True while <see cref="Respawn"/> differs from what the game last reported - a bed, region
    /// or terminal pick, or a typed coordinate, that has not been applied yet via
    /// <see cref="TeleportAsync"/>/<see cref="ClaimRespawnTerminalAsync"/>. The host's own live
    /// refresh loop (<c>LiveConnect.razor</c>'s <c>RefreshActiveAreaAsync</c>) skips a session
    /// while this is true, the same way it already skips one mid-push - without it, that loop's
    /// periodic <see cref="RefreshAsync"/> replaced <see cref="Respawn"/> with the character's
    /// current position every couple of seconds, snapping a pick back before TELEPORT ME HERE
    /// ever got a chance to send it anywhere.
    /// </summary>
    public bool IsDirty => Respawn.IsDifferentFrom(_lastKnownRespawn);
    public string? Status { get; private set; }

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the character's position/respawn
    /// point and after every mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    /// <summary>Immediate-apply session: nothing to mark, the tab's own action buttons drive
    /// every real write (see <see cref="TeleportAsync"/>/<see cref="ClaimRespawnTerminalAsync"/>).</summary>
    public void MarkChanged() { }

    /// <summary>No page-level "Apply" for this tab (see <c>LiveConnect.razor</c>'s comment on why
    /// only the staged vitals/skills tabs get one) - a no-op so the shared interface is still
    /// satisfied.</summary>
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Nothing is staged, so there is nothing to revert.</summary>
    public void Revert() { }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so moving around in the running game never updates the position shown here.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Re-reads the connected character's position and claimed respawn terminal.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        LivePosition = (state.X, state.Y, state.Z);
        Respawn = new PlayerRespawnEdit(state.X, state.Y, state.Z, state.LevelName, state.TerminalGuid);
        _lastKnownRespawn = Respawn.Clone();
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Moves the character to <see cref="Respawn"/>'s current X/Y/Z immediately (a real
    /// teleport - <see cref="LiveSpawnChannel.TeleportAsync"/>), then re-reads the position.</summary>
    public async Task TeleportAsync(CancellationToken cancellationToken = default)
    {
        await _channel.TeleportAsync(Respawn.X, Respawn.Y, Respawn.Z, _playerId, cancellationToken).ConfigureAwait(false);
        Status = "Teleported - this moved the character in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Claims <see cref="Respawn"/>'s current <see cref="PlayerRespawnEdit.TerminalGuid"/>
    /// as the respawn point immediately, then re-reads it.</summary>
    public async Task ClaimRespawnTerminalAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Respawn.TerminalGuid)) return;
        await _channel.SetRespawnTerminalAsync(Respawn.TerminalGuid, _playerId, cancellationToken).ConfigureAwait(false);
        Status = "Respawn point set - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Switches which connected player this session reads/acts on, discarding nothing
    /// (there was never anything staged) and re-reading the new target immediately.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
