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

    [Fact]
    public async Task LivePlayerSkillsChannel_GetAsync_deserializes_a_real_JSON_array()
    {
        await using var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));
        var skills = new AbioticEditor.Core.LiveEditing.Player.LivePlayerSkillsChannel(channel);

        var result = await skills.GetAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Index);
        Assert.Equal(100, result[0].Xp);
        Assert.Equal(1, result[0].XpMultiplier);
        Assert.Equal(1, result[1].Index);
        Assert.Equal(200, result[1].Xp);
        Assert.Equal(1.5f, result[1].XpMultiplier);
    }

    [Fact]
    public async Task LivePlayerSkillsChannel_SetAsync_sends_a_real_JSON_array()
    {
        await using var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));
        var skills = new AbioticEditor.Core.LiveEditing.Player.LivePlayerSkillsChannel(channel);

        // The fake agent's fallback echoes any non-special-cased command's payload straight back
        // as "ok:true"; SetAsync succeeding without throwing proves the request itself encoded
        // as a well-formed array the agent could parse (a malformed line would fault the channel).
        await skills.SetAsync([new AbioticEditor.Core.PlayerSaves.PlayerSkill(0, 999, 3)]);

        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    /// <summary>
    /// Round 76: transmog reads/writes through the SAME inventory.list/inventory.set commands as
    /// backpack/equip/hotbar (see docs/reference/live-editing-protocol.md), just a fourth "kind"
    /// value - this proves the client-side channel round-trips that kind untouched, since it
    /// never interprets "kind" itself.
    /// </summary>
    [Fact]
    public async Task LiveInventoryChannel_GetAsync_includes_the_transmog_kind()
    {
        await using var channel = await ConnectedChannelAsync();
        var inventory = new AbioticEditor.Core.LiveEditing.Player.LiveInventoryChannel(channel);

        var slots = await inventory.GetAsync();

        var transmog = Assert.Single(slots, slot => slot.Kind == "transmog");
        Assert.Equal(0, transmog.SlotIndex);
        Assert.Equal("suit_hazmat_casual", transmog.ItemId);
        Assert.False(transmog.IsEmpty);
        var backpack = Assert.Single(slots, slot => slot.Kind == "backpack");
        Assert.True(backpack.IsEmpty);
    }

    [Fact]
    public async Task LiveInventoryChannel_SetAsync_sends_a_transmog_edit()
    {
        await using var channel = await ConnectedChannelAsync();
        var inventory = new AbioticEditor.Core.LiveEditing.Player.LiveInventoryChannel(channel);

        // Same fallback-echo proof as LivePlayerSkillsChannel_SetAsync_sends_a_real_JSON_array:
        // succeeding without throwing proves the "transmog" kind encodes as a well-formed edit.
        await inventory.SetAsync([new AbioticEditor.Core.LiveEditing.Player.LiveInventoryEdit(
            "transmog", 0, ItemId: "suit_hazmat_casual", Stack: 1)]);

        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveWorldStateChannel_GetAsync_reads_clock_and_weather()
    {
        await using var channel = await ConnectedChannelAsync();
        var state = await new AbioticEditor.Core.LiveEditing.World.LiveWorldStateChannel(channel).GetAsync();

        Assert.True(state.IsHost);
        Assert.Equal(12, state.Day);
        Assert.Equal(13, state.Hour);
        Assert.Equal(30, state.Minute);
        Assert.False(state.IsNight);
        Assert.Equal("Fog", state.CurrentWeather);
        Assert.Equal(["None", "Fog", "RadLeak"], state.WeatherOptions);
    }

    [Fact]
    public async Task LiveWorldFlagsChannel_round_trips_flags()
    {
        await using var channel = await ConnectedChannelAsync();
        var flags = new AbioticEditor.Core.LiveEditing.World.LiveWorldFlagsChannel(channel);

        var directory = await flags.GetAsync();
        Assert.Equal(2, directory.Flags.Count);
        Assert.True(directory.Flags[0].IsSet);
        Assert.Equal("Manufacturing_West", directory.Flags[1].Name);
        Assert.False(directory.Flags[1].IsSet);

        await flags.SetAsync([new AbioticEditor.Core.LiveEditing.World.LiveWorldFlag("Manufacturing_West", true)]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveDoorsChannel_GetAsync_distinguishes_door_kinds()
    {
        await using var channel = await ConnectedChannelAsync();
        var directory = await new AbioticEditor.Core.LiveEditing.World.LiveDoorsChannel(channel).GetAsync();

        Assert.False(directory.IsHost);
        Assert.Equal(2, directory.Doors.Count);
        Assert.Equal(AbioticEditor.Core.LiveEditing.World.LiveDoorKind.Simple, directory.Doors[0].Kind);
        Assert.Equal(2, directory.Doors[0].State);
        Assert.Equal(100.5, directory.Doors[0].X);
        Assert.Equal(AbioticEditor.Core.LiveEditing.World.LiveDoorKind.Security, directory.Doors[1].Kind);
        Assert.True(directory.Doors[1].IsOpen);
    }

    [Fact]
    public async Task LiveContainersChannel_GetAsync_reads_slots()
    {
        await using var channel = await ConnectedChannelAsync();
        var containers = new AbioticEditor.Core.LiveEditing.World.LiveContainersChannel(channel);

        var directory = await containers.GetAsync();
        var crate = Assert.Single(directory.Containers);
        Assert.Equal("Deployed_StorageCrate_Makeshift_C", crate.Label);
        Assert.Equal(2, crate.Slots.Count);
        Assert.Equal(1, crate.OccupiedCount);
        Assert.Equal("scrap_metal", crate.Slots[0].ItemId);
        Assert.Equal(5, crate.Slots[0].Stack);
        Assert.True(crate.Slots[1].IsEmpty);

        await containers.SetAsync(crate.Id, [new AbioticEditor.Core.LiveEditing.World.LiveContainerSlotEdit(1, ItemId: "scrap_cloth", Stack: 2)]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveDroppedItemsChannel_lists_and_removes()
    {
        await using var channel = await ConnectedChannelAsync();
        var dropped = new AbioticEditor.Core.LiveEditing.World.LiveDroppedItemsChannel(channel);

        var directory = await dropped.GetAsync();
        var item = Assert.Single(directory.Items);
        Assert.Equal("scrap_cloth", item.ItemId);
        Assert.Equal(3, item.Stack);

        Assert.Equal(1, await dropped.RemoveAsync([item.Id]));
    }

    private async Task<TcpLiveGameChannel> ConnectedChannelAsync()
    {
        var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", _agent.Port, "correct-token"));
        return channel;
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

                if (cmd == "skills.get")
                {
                    // A real JSON array, exactly like the C++ agent's skills.get (see
                    // live-agent's StandaloneProtocolSmokeTest.cpp) - proves the array shape
                    // deserializes into IReadOnlyList<PlayerSkill> correctly, not just objects.
                    var skillsLine = "{\"Id\":\"" + id + "\",\"Ok\":true,\"Result\":["
                        + "{\"index\":0,\"xp\":100,\"xpMultiplier\":1},"
                        + "{\"index\":1,\"xp\":200,\"xpMultiplier\":1.5}]}";
                    await writer.WriteLineAsync(skillsLine.AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Round-75 world areas: one canned reading each, in the exact JSON the Lua mod's
                // json.lua emits (camelCase keys, __forceArray lists as real arrays).
                string? canned = cmd switch
                {
                    // Round 76: a fourth "transmog" kind riding the same inventory.list array as
                    // backpack/equip/hotbar (see LiveInventoryChannel_GetAsync_includes_the_transmog_kind).
                    "inventory.list" => "[{\"kind\":\"backpack\",\"slotIndex\":0,\"itemId\":\"Empty\",\"isEmpty\":true,\"stack\":0,\"durability\":0,\"maxDurability\":0},"
                        + "{\"kind\":\"transmog\",\"slotIndex\":0,\"itemId\":\"suit_hazmat_casual\",\"isEmpty\":false,\"stack\":1,\"durability\":0,\"maxDurability\":0}]",
                    "world.get" => "{\"isHost\":true,\"day\":12,\"timeSeconds\":48600,\"isNight\":false,\"paused\":false,"
                        + "\"currentWeather\":\"Fog\",\"weatherOptions\":[\"None\",\"Fog\",\"RadLeak\"]}",
                    "flags.list" => "{\"flags\":[{\"name\":\"Office_PowerOn\",\"isSet\":true},{\"name\":\"Manufacturing_West\",\"isSet\":false}],\"isHost\":true}",
                    "doors.list" => "{\"doors\":[{\"id\":\"SimpleDoor_ParentBP_C /Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_7\",\"label\":\"SimpleDoor_ParentBP_C\",\"kind\":\"simple\",\"state\":2,\"isOpen\":false,\"oneWayUnlocked\":false,\"disabled\":false,\"x\":100.5,\"y\":-20,\"z\":3},"
                        + "{\"id\":\"SecurityDoor_C /Game/Maps/Facility.Facility:PersistentLevel.SecurityDoor_C_2\",\"label\":\"SecurityDoor_C\",\"kind\":\"security\",\"state\":1,\"isOpen\":true,\"oneWayUnlocked\":false,\"disabled\":false,\"x\":0,\"y\":0,\"z\":0}],\"isHost\":false}",
                    "containers.list" => "{\"containers\":[{\"id\":\"Deployed_StorageCrate_Makeshift_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_StorageCrate_Makeshift_C_3\",\"label\":\"Deployed_StorageCrate_Makeshift_C\",\"x\":1,\"y\":2,\"z\":3,"
                        + "\"slots\":[{\"slotIndex\":0,\"itemId\":\"scrap_metal\",\"isEmpty\":false,\"stack\":5,\"durability\":0,\"maxDurability\":0},{\"slotIndex\":1,\"itemId\":\"Empty\",\"isEmpty\":true,\"stack\":0,\"durability\":0,\"maxDurability\":0}]}],\"isHost\":true}",
                    "dropped.list" => "{\"items\":[{\"id\":\"Abiotic_Item_Dropped_C /Game/Maps/Facility.Facility:PersistentLevel.Abiotic_Item_Dropped_C_9\",\"itemId\":\"scrap_cloth\",\"stack\":3,\"x\":4,\"y\":5,\"z\":6}],\"isHost\":true}",
                    "dropped.remove" => "{\"removed\":1}",
                    _ => null,
                };
                if (canned is not null)
                {
                    var cannedLine = "{\"Id\":\"" + id + "\",\"Ok\":true,\"Result\":" + canned + "}";
                    await writer.WriteLineAsync(cannedLine.AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // "echo"/"skills.set" (and anything else): hand the payload straight back as the result.
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
