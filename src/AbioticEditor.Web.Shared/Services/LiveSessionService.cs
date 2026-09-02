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

    public event Action? Changed;

    /// <summary>Raised by <see cref="RequestPlayerSwitchAsync"/>; <c>LiveConnect.razor</c> is the
    /// only subscriber (it owns the actual vitals/skills sessions), so the sidebar (which has no
    /// access to those) can still ask it to switch who is being edited - the same
    /// publish/subscribe shape <see cref="Changed"/> uses, just going the other direction.</summary>
    public event Func<string, Task>? PlayerSwitchRequested;

    public void Connected(LivePlayerDirectory directory, string? selectedPlayerId)
    {
        IsConnected = true;
        Directory = directory;
        SelectedPlayerId = selectedPlayerId;
        Changed?.Invoke();
    }

    /// <summary>Refreshes the player list/host status without changing which player is selected.</summary>
    public void DirectoryUpdated(LivePlayerDirectory directory)
    {
        Directory = directory;
        Changed?.Invoke();
    }

    public void PlayerSelected(string? playerId)
    {
        SelectedPlayerId = playerId;
        Changed?.Invoke();
    }

    public void Disconnected()
    {
        IsConnected = false;
        Directory = null;
        SelectedPlayerId = null;
        Changed?.Invoke();
    }

    /// <summary>Asks whoever owns the live connection to switch to <paramref name="playerId"/>.
    /// A no-op (not an error) when nothing is connected to hear it - the sidebar can only ever
    /// offer this on a row it already knows is live, but the connection could still have dropped
    /// a moment earlier.</summary>
    public Task RequestPlayerSwitchAsync(string playerId) =>
        PlayerSwitchRequested is { } handler ? handler.Invoke(playerId) : Task.CompletedTask;
}
