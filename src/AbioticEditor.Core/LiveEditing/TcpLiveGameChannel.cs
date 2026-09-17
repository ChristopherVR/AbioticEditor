using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Core.LiveEditing;

/// <summary>
/// A single TCP connection to the AbioticEditorLiveAgent mod, speaking one newline-delimited
/// JSON request per line, one JSON response per line back. Works identically for a locally
/// hosted game ("127.0.0.1") and a remote dedicated server the caller controls - there is no
/// local/remote branch anywhere in here, only the host string differs.
///
/// Requests are serialized one at a time (a lock, not multiplexed by id): the editor UI only
/// ever has one live edit in flight, and keeping the wire protocol single-request-at-a-time
/// keeps the C++ agent side simple too.
/// </summary>
public sealed class TcpLiveGameChannel : ILiveGameChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A response line still on its way for a request whose caller stopped waiting
    /// (see SendAsync); collected before the next request is sent so replies stay in step.</summary>
    private Task<string?>? _owedResponse;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private long _nextRequestId;
    private LiveConnectionState _state = LiveConnectionState.Disconnected;

    public LiveConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public event Action<LiveConnectionState>? StateChanged;

    public async Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        await DisconnectAsync().ConfigureAwait(false);

        State = LiveConnectionState.Connecting;
        // Host and port only - never info.Token, which is the live-edit connection secret.
        EditorLog.Info("LiveAgent", $"Connecting to {info.Host}:{info.Port}.");
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(info.Host, info.Port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            _client = client;
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            // A "hello" round trip fails fast on a bad token or a mismatched protocol version,
            // instead of only surfacing on the first real command.
            var hello = await SendAsync<HelloResponse>("hello", new HelloRequest(info.Token, ProtocolVersion: 1),
                info.Token, cancellationToken).ConfigureAwait(false);
            if (hello.ProtocolVersion != 1)
                throw new LiveAgentException(
                    $"The live-agent mod speaks protocol version {hello.ProtocolVersion}, this editor speaks 1.");

            State = LiveConnectionState.Connected;
            EditorLog.Info("LiveAgent", $"Connected to {info.Host}:{info.Port} (agent {hello.AgentVersion}).");
        }
        catch (Exception exception)
        {
            State = LiveConnectionState.Faulted;
            EditorLog.Warn("LiveAgent", $"Connecting to {info.Host}:{info.Port} failed.", exception);
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        var wasConnected = _client is not null;
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
        _owedResponse = null;
        if (State != LiveConnectionState.Faulted) State = LiveConnectionState.Disconnected;
        if (wasConnected) EditorLog.Info("LiveAgent", "Disconnected.");
        return Task.CompletedTask;
    }

    public async Task<TResponse> RequestAsync<TResponse>(
        string command, object? payload, CancellationToken cancellationToken = default)
    {
        if (State != LiveConnectionState.Connected || _writer is null || _reader is null)
            throw new InvalidOperationException("Not connected to a live game.");
        return await SendAsync<TResponse>(command, payload, token: null, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="token">Only set on the initial "hello"; every later request rides the
    /// connection that "hello" already authenticated, so it is omitted (nothing re-sends a
    /// secret it does not need to on every single message).</param>
    private async Task<TResponse> SendAsync<TResponse>(
        string command, object? payload, string? token, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var writer = _writer ?? throw new InvalidOperationException("Not connected to a live game.");
            var reader = _reader ?? throw new InvalidOperationException("Not connected to a live game.");
            var id = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var envelope = new RequestEnvelope(id, command, token, payload is null
                ? null
                : JsonSerializer.SerializeToElement(payload, JsonOptions));

            // A caller that stopped waiting (a tab switched away, a component disposed) leaves
            // its response still on its way. Cancelling the socket read itself used to abort the
            // socket (Windows error 995) and fault the whole connection, and skipping the line
            // would have handed the next request the wrong answer. So the read is never
            // cancelled: the caller stops waiting for it, the line is collected here before
            // anything else is sent, and the connection stays in step.
            while (_owedResponse is { } owed)
            {
                await owed.WaitAsync(cancellationToken).ConfigureAwait(false);
                _owedResponse = null;
            }

            var line = JsonSerializer.Serialize(envelope, JsonOptions);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);

            ResponseEnvelope response;
            while (true)
            {
                var read = reader.ReadLineAsync(CancellationToken.None).AsTask();
                _owedResponse = read;
                var responseLine = await read.WaitAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("The live agent closed the connection.");
                _owedResponse = null;
                response = JsonSerializer.Deserialize<ResponseEnvelope>(responseLine, JsonOptions)
                    ?? throw new IOException("The live agent sent an empty response.");
                // Only a stale line from an abandoned request can carry another id; read on.
                if (response.Id is null || response.Id == id) break;
            }

            if (!response.Ok)
                throw new LiveAgentException(response.Error ?? $"The live agent rejected '{command}'.");
            if (response.Result is not { } result)
                return default!;
            return result.Deserialize<TResponse>(JsonOptions)!;
        }
        catch (Exception exception) when (State == LiveConnectionState.Connected
                                          && exception is not LiveAgentException and not OperationCanceledException)
        {
            // A mid-request failure (dropped socket, malformed line, a cancelled/timed-out read)
            // leaves the connection unusable even though State still said Connected a moment ago -
            // reflect that so the next caller sees Faulted instead of silently hanging on a dead
            // stream. A LiveAgentException is deliberately excluded: it means the round trip itself
            // completed fine (a well-formed response line came back, just with Ok:false, e.g. the
            // in-game script's own handler called error()) - that proves the connection is healthy,
            // not broken, so it must not fault the whole channel. Before this exclusion, ANY
            // in-game handler error (a container mid-destruction, a bad row id, anything a Lua
            // handler legitimately rejects with error()) would silently mark the entire live
            // connection Faulted, so every later request - including the sidebar's world.info
            // region poll - failed immediately with "Not connected to a live game" instead of its
            // own real error, until a full reconnect. That is the most likely explanation for a
            // world-area tab going "not available" and the sidebar's live region badge dropping out
            // right after it, in the same session, with no actual disconnect (see the containers.list
            // per-container pcall fix in main.lua for the specific error that used to trigger this).
            State = LiveConnectionState.Faulted;
            // The command name only, never `payload` - "hello"'s own payload carries the
            // connection token, and no command's payload belongs in a log file on disk.
            EditorLog.Warn("LiveAgent", $"'{command}' faulted the connection.", exception);
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    private sealed record RequestEnvelope(string Id, string Cmd, string? Token, JsonElement? Payload);
    private sealed record ResponseEnvelope(string Id, bool Ok, JsonElement? Result, string? Error);
    private sealed record HelloRequest(string Token, int ProtocolVersion);
    private sealed record HelloResponse(int ProtocolVersion, string AgentVersion);
}
