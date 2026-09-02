using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        }
        catch
        {
            State = LiveConnectionState.Faulted;
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
        if (State != LiveConnectionState.Faulted) State = LiveConnectionState.Disconnected;
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

            var line = JsonSerializer.Serialize(envelope, JsonOptions);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);

            var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The live agent closed the connection.");
            var response = JsonSerializer.Deserialize<ResponseEnvelope>(responseLine, JsonOptions)
                ?? throw new IOException("The live agent sent an empty response.");

            if (!response.Ok)
                throw new LiveAgentException(response.Error ?? $"The live agent rejected '{command}'.");
            if (response.Result is not { } result)
                return default!;
            return result.Deserialize<TResponse>(JsonOptions)!;
        }
        catch (Exception) when (State == LiveConnectionState.Connected)
        {
            // A mid-request failure (dropped socket, malformed line) leaves the connection
            // unusable even though State still said Connected a moment ago - reflect that so
            // the next caller sees Faulted instead of silently hanging on a dead stream.
            State = LiveConnectionState.Faulted;
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
