namespace AbioticEditor.Core.LiveEditing;

/// <summary>Lifecycle of a connection to a running game's live-agent mod.</summary>
public enum LiveConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted,
}

/// <summary>Where to reach a running game's live-agent mod, and the shared token it expects.</summary>
/// <param name="Host">Hostname or IP: "127.0.0.1" for a locally-hosted game, or a remote
/// dedicated server's address the caller controls (has installed the mod on themselves).</param>
/// <param name="Port">TCP port the live-agent mod is listening on.</param>
/// <param name="Token">Shared secret the mod was configured with, checked on connect.</param>
public sealed record LiveConnectionInfo(string Host, int Port, string Token);

/// <summary>Thrown when the live agent rejects a request (bad token, unknown command, or the
/// command's own reported failure) rather than the transport itself failing.</summary>
public sealed class LiveAgentException(string message) : Exception(message);
