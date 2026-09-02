namespace AbioticEditor.Core.LiveEditing;

/// <summary>
/// A request/response channel to a running game process, reached through the
/// AbioticEditorLiveAgent mod. One request maps to exactly one response; this is the live
/// counterpart to reading/writing an FPropertyTag list off a loaded save, so live reader/writer
/// pairs (e.g. <see cref="Player.LivePlayerVitalsChannel"/>) depend on this instead of a file.
/// </summary>
public interface ILiveGameChannel : IAsyncDisposable
{
    LiveConnectionState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes, including an unrequested drop.</summary>
    event Action<LiveConnectionState>? StateChanged;

    Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>
    /// Sends one command with a JSON-serializable payload and returns the decoded response.
    /// Throws <see cref="LiveAgentException"/> when the agent itself reports failure (bad
    /// token, unknown command, the command's own validation failed), distinct from a transport
    /// failure (connection dropped, timed out), which throws normally (IOException etc.).
    /// </summary>
    Task<TResponse> RequestAsync<TResponse>(
        string command, object? payload, CancellationToken cancellationToken = default);
}
