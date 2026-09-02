using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Exercises <see cref="TcpLiveGameChannel"/> against a fake in-process agent speaking the same
/// newline-delimited JSON protocol the real AbioticEditorLiveAgent mod implements, so the wire
/// protocol itself is verified even though the real mod cannot run in a test process.
/// </summary>
public sealed class TcpLiveGameChannelTests : IAsyncLifetime
{
    private FakeLiveAgent _agent = null!;

    public async Task InitializeAsync() => _agent = await FakeLiveAgent.StartAsync();
    public async Task DisposeAsync() => await _agent.DisposeAsync();

    [Fact]
    public async Task Connect_sends_hello_and_reaches_Connected()
    {
        await using var channel = new TcpLiveGameChannel();
        var states = new List<LiveConnectionState>();
        channel.StateChanged += states.Add;

        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));

        Assert.Equal(LiveConnectionState.Connected, channel.State);
        Assert.Equal([LiveConnectionState.Connecting, LiveConnectionState.Connected], states);
    }

    [Fact]
    public async Task Connect_with_wrong_token_throws_and_faults()
    {
        await using var channel = new TcpLiveGameChannel();

        await Assert.ThrowsAsync<LiveAgentException>(
            () => channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "wrong-token")));

        Assert.Equal(LiveConnectionState.Faulted, channel.State);
    }

    [Fact]
    public async Task RequestAsync_round_trips_a_typed_payload()
    {
        await using var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));

        var reply = await channel.RequestAsync<EchoPayload>("echo", new EchoPayload("hunger", 42.5));

        Assert.Equal("hunger", reply.Name);
        Assert.Equal(42.5, reply.Value);
    }

    [Fact]
    public async Task RequestAsync_surfaces_an_agent_rejected_command_as_LiveAgentException()
    {
        await using var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));

        var exception = await Assert.ThrowsAsync<LiveAgentException>(
            () => channel.RequestAsync<object?>("boom", payload: null));
        Assert.Equal("simulated agent failure", exception.Message);
    }

    private sealed record EchoPayload(string Name, double Value);

    /// <summary>A minimal stand-in for the real UE4SS agent: accepts one connection, checks the
    /// token on "hello", then answers "echo" by returning its payload unchanged and "boom" with
    /// a failure envelope, exactly the shapes <see cref="TcpLiveGameChannel"/> expects.</summary>
    private sealed class FakeLiveAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _acceptLoop;
        private readonly CancellationTokenSource _cts = new();

        private FakeLiveAgent(TcpListener listener)
        {
            _listener = listener;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<FakeLiveAgent> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeLiveAgent(listener));
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await ServeOneConnectionAsync(client, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private static async Task ServeOneConnectionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement;
                // TcpLiveGameChannel serializes with JsonSerializerDefaults.Web, so wire
                // property names are camelCase regardless of the C# record's PascalCase names.
                var id = root.GetProperty("id").GetString();
                var cmd = root.GetProperty("cmd").GetString();

                if (cmd == "hello")
                {
                    var token = root.GetProperty("payload").GetProperty("token").GetString();
                    if (token != "correct-token")
                    {
                        var rejected = "{\"Id\":\"" + id + "\",\"Ok\":false,\"Error\":\"bad token\"}";
                        await writer.WriteLineAsync(rejected.AsMemory(), cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    var accepted = "{\"Id\":\"" + id
                        + "\",\"Ok\":true,\"Result\":{\"ProtocolVersion\":1,\"AgentVersion\":\"test-fake\"}}";
                    await writer.WriteLineAsync(accepted.AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (cmd == "boom")
                {
                    var failed = "{\"Id\":\"" + id + "\",\"Ok\":false,\"Error\":\"simulated agent failure\"}";
                    await writer.WriteLineAsync(failed.AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // "echo" (and anything else): hand the payload straight back as the result.
                var payload = root.TryGetProperty("payload", out var p) ? p.GetRawText() : "null";
                var echoLine = "{\"Id\":\"" + id + "\",\"Ok\":true,\"Result\":" + payload + "}";
                await writer.WriteLineAsync(echoLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try { await _acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
}
