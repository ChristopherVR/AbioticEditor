using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The live-editing counterpart to <c>SaveWorkspaceSessionService</c>: shared state the shell
/// (header, sidebar) reads so it can show the current live session correctly instead of leftover
/// file-mode chrome ("SAVE FILES" / "NO FOLDER LOADED" / "Select a save to begin editing.") while
/// <c>/live</c> is connected. <c>LiveConnect.razor</c> owns the actual connection and pushes
/// updates here; the shell only ever reads.
///
/// Registered by every host (matching how every other workspace-shell dependency is registered
/// in both <c>AbioticEditor.Web</c> and <c>AbioticEditor.Web.Wasm</c>'s <c>Program.cs</c>, even
/// though the WASM build never registers <see cref="ILiveEditingCapability"/> and so never
/// navigates to <c>/live</c> at all) so the shell's unconditional injection never fails to
/// resolve; on that host this simply always reports <see cref="IsConnected"/> false.
/// </summary>
public sealed class LiveSessionService
{
    public bool IsConnected { get; private set; }
    public LivePlayerDirectory? Directory { get; private set; }
    public string? SelectedPlayerId { get; private set; }

    /// <summary>Which single surface <c>/live</c> currently shows - a player's own tab strip, the
    /// world tabs, or an offline player's own save file - mirroring the file editor's own "one
    /// thing at a time" rule (never more than one save/session stacked). The sidebar reads this to
    /// highlight the right row; <c>LiveConnect.razor</c> reads it to decide which section to
    /// render.</summary>
    public LiveSurface Surface { get; private set; } = LiveSurface.Player;

    /// <summary>When <see cref="Surface"/> is <see cref="LiveSurface.World"/>: whether the sidebar's
    /// world-metadata row (story/traders/containment/entitlements) is showing rather than the
    /// currently loaded region (containers/flags/doors/dropped/wildlife/bases/vehicles/pets/story
    /// NPCs/teleporters/raw). See <c>WorkspaceShell</c>'s per-region sidebar rows (round 78).</summary>
    public bool WorldSurfaceIsMetadata { get; private set; }

    /// <summary>This connection's world folder on disk, found via
    /// <c>AbioticEditor.Core.WorldSaves.LiveWorldFolderLocator</c> once the local player's id is
    /// known - null until that lookup finishes or when it found nothing (a world this machine
    /// cannot see). The sidebar shows a static, mostly-disabled region list as a fallback while
    /// this is null - see <c>WorkspaceShell</c>'s <c>IsLiveWorldOpen</c>.</summary>
    public string? WorldFolder { get; private set; }

    /// <summary>The running game's current streaming level token (e.g. <c>Facility_MFWest</c>),
    /// polled from <c>world.info</c> - see <c>LiveWorldInfoChannel</c>. Null until the first poll
    /// lands or when no local controller is loaded (main menu).</summary>
    public string? CurrentRegionToken { get; private set; }

    public event Action? Changed;

    /// <summary>Records which world folder this connection's saves live in - a no-op when nothing
    /// changed, so polling this every couple of seconds does not spam <see cref="Changed"/>.</summary>
    public void SetWorldFolder(string? folder)
    {
        if (string.Equals(WorldFolder, folder, StringComparison.OrdinalIgnoreCase)) return;
        WorldFolder = folder;
        Changed?.Invoke();
    }

    /// <summary>Records the running game's current region token - a no-op when unchanged, same
    /// reasoning as <see cref="SetWorldFolder"/>.</summary>
    public void SetCurrentRegion(string? levelToken)
    {
        if (string.Equals(CurrentRegionToken, levelToken, StringComparison.OrdinalIgnoreCase)) return;
        CurrentRegionToken = levelToken;
        Changed?.Invoke();
    }

    /// <summary>Raised by <see cref="RequestPlayerSwitchAsync"/>; <c>LiveConnect.razor</c> is the
    /// only subscriber (it owns the actual vitals/skills sessions), so the sidebar (which has no
    /// access to those) can still ask it to switch who is being edited - the same
    /// publish/subscribe shape <see cref="Changed"/> uses, just going the other direction.</summary>
    public event Func<string, Task>? PlayerSwitchRequested;

    /// <summary>Raised by <see cref="RequestWorldSurfaceAsync"/> - a sidebar region/metadata row
    /// asking <c>LiveConnect.razor</c> to switch away from whichever player is currently shown and
    /// render the world tabs instead. The <c>bool</c> is <see cref="WorldSurfaceIsMetadata"/>'s new
    /// value. Same shape as <see cref="PlayerSwitchRequested"/>.</summary>
    public event Func<bool, Task>? WorldSurfaceRequested;

    /// <summary>Raised by <see cref="RequestOfflinePlayerFileAsync"/> - the sidebar's offline-player
    /// row asking <c>LiveConnect.razor</c> to show the player save it already staged through
    /// <c>SaveWorkspaceSessionService.SelectAsync</c> (see <c>WorkspaceShell</c>'s offline-player
    /// row handler) instead of a live player or the world tabs.</summary>
    public event Func<Task>? OfflinePlayerFileRequested;

    /// <summary>Raised by <see cref="RequestDisconnectAsync"/> - the sidebar header's DISCONNECT
    /// button asking <c>LiveConnect.razor</c> to run its own <c>DisconnectAsync</c>, now that the
    /// APPLY/REVERT/DISCONNECT bar it used to live in is gone from the page body.</summary>
    public event Func<Task>? DisconnectRequested;

    public void Connected(LivePlayerDirectory directory, string? selectedPlayerId)
    {
        IsConnected = true;
        Directory = directory;
        SelectedPlayerId = selectedPlayerId;
        Surface = LiveSurface.Player;
        Changed?.Invoke();
    }

    /// <summary>Refreshes the player list/host status without changing which player is selected.</summary>
    public void DirectoryUpdated(LivePlayerDirectory directory)
    {
        Directory = directory;
        Changed?.Invoke();
    }

    /// <summary>A player was selected (switched to, or reselected after viewing the world) -
    /// always implies the Player surface, the same way clicking a player save in the file sidebar
    /// always shows that player's tabs.</summary>
    public void PlayerSelected(string? playerId)
    {
        SelectedPlayerId = playerId;
        Surface = LiveSurface.Player;
        Changed?.Invoke();
    }

    /// <summary>A region or the world-metadata row was selected - mirrors <see
    /// cref="PlayerSelected"/> for the other surface. <see cref="SelectedPlayerId"/> is left
    /// alone: which player is loaded does not change, only which surface is on screen.</summary>
    public void WorldSurfaceSelected(bool isMetadata)
    {
        Surface = LiveSurface.World;
        WorldSurfaceIsMetadata = isMetadata;
        Changed?.Invoke();
    }

    /// <summary>An offline player's own save file was selected from the sidebar - mirrors <see
    /// cref="PlayerSelected"/>/<see cref="WorldSurfaceSelected"/> for the third surface. The file
    /// itself is already staged on <c>SaveWorkspaceSessionService</c> by the caller before this
    /// runs (see <c>WorkspaceShell</c>'s offline-player row handler).</summary>
    public void OfflinePlayerFileSelected()
    {
        Surface = LiveSurface.OfflinePlayerFile;
        Changed?.Invoke();
    }

    public void Disconnected()
    {
        IsConnected = false;
        Directory = null;
        SelectedPlayerId = null;
        Surface = LiveSurface.Player;
        WorldSurfaceIsMetadata = false;
        WorldFolder = null;
        CurrentRegionToken = null;
        Changed?.Invoke();
    }

    /// <summary>Asks whoever owns the live connection to switch to <paramref name="playerId"/>.
    /// A no-op (not an error) when nothing is connected to hear it - the sidebar can only ever
    /// offer this on a row it already knows is live, but the connection could still have dropped
    /// a moment earlier.</summary>
    public Task RequestPlayerSwitchAsync(string playerId) =>
        PlayerSwitchRequested is { } handler ? handler.Invoke(playerId) : Task.CompletedTask;

    /// <summary>Asks whoever owns the live connection to show the world tabs instead of a player's,
    /// with the region tab set (<paramref name="isMetadata"/> false) or the metadata tab set
    /// (true). Same no-op-when-unheard rule as <see cref="RequestPlayerSwitchAsync"/>.</summary>
    public Task RequestWorldSurfaceAsync(bool isMetadata) =>
        WorldSurfaceRequested is { } handler ? handler.Invoke(isMetadata) : Task.CompletedTask;

    /// <summary>Asks whoever owns the live connection to show the offline player file already
    /// staged on <c>SaveWorkspaceSessionService</c>. Same no-op-when-unheard rule as <see
    /// cref="RequestPlayerSwitchAsync"/>.</summary>
    public Task RequestOfflinePlayerFileAsync() =>
        OfflinePlayerFileRequested is { } handler ? handler.Invoke() : Task.CompletedTask;

    /// <summary>Asks whoever owns the live connection to disconnect. Same no-op-when-unheard rule
    /// as <see cref="RequestPlayerSwitchAsync"/> - the sidebar's DISCONNECT button only ever shows
    /// while a connection exists, but it could have dropped a moment earlier.</summary>
    public Task RequestDisconnectAsync() =>
        DisconnectRequested is { } handler ? handler.Invoke() : Task.CompletedTask;

    private (ILiveGameChannel Channel, string Token)? _verifiedConnection;

    /// <summary>
    /// Hands over a connection the mode-select dialog already proved works (a real TCP connect
    /// plus a real game-state read, not just a token file existing) so the page that navigates
    /// in next can adopt it directly instead of reconnecting from scratch and showing its own
    /// "looking for a game" screen for a game already known to answer. Replaces (disconnecting)
    /// any earlier handoff nobody ever collected.
    /// </summary>
    public void HandOffVerifiedConnection(ILiveGameChannel channel, string token)
    {
        if (_verifiedConnection is { } stale) _ = stale.Channel.DisconnectAsync();
        _verifiedConnection = (channel, token);
    }

    /// <summary>Collects (and clears) the handed-off connection, or null when there is none -
    /// a direct/bookmarked visit to <c>/live</c> never went through the dialog at all.</summary>
    public (ILiveGameChannel Channel, string Token)? TakeVerifiedConnection()
    {
        var pending = _verifiedConnection;
        _verifiedConnection = null;
        return pending;
    }

    /// <summary>
    /// Raised when <c>/live</c> has nowhere left to send the player except "ask where the game
    /// is running again" (a lost connection, a direct/bookmarked visit with no answer yet) -
    /// <c>MainLayout</c> reopens the mode-select dialog in response, because that question has
    /// exactly one home (the dialog) and must never render as a bare page of its own again.
    /// </summary>
    public event Action? ModeSelectRequested;

    public void RequestModeSelect() => ModeSelectRequested?.Invoke();
}

/// <summary>The mutually-exclusive surfaces <c>/live</c> can show - see <see
/// cref="LiveSessionService.Surface"/>.</summary>
public enum LiveSurface
{
    Player,
    World,

    /// <summary>An offline (not currently connected) player's own save file, opened for ordinary
    /// file editing on <c>SaveWorkspaceSessionService</c> while everything else stays live -
    /// round 78's "offline players" sidebar rows.</summary>
    OfflinePlayerFile,
}
