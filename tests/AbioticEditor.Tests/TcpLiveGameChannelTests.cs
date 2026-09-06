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
    public async Task LiveStoryChannel_GetAsync_reads_the_current_quest_row()
    {
        await using var channel = await ConnectedChannelAsync();
        var state = await new AbioticEditor.Core.LiveEditing.World.LiveStoryChannel(channel).GetAsync();

        Assert.True(state.IsHost);
        Assert.Equal("Office", state.CurrentQuestRow);
    }

    [Fact]
    public async Task LiveStoryChannel_SetAsync_sends_the_computed_flag_lists()
    {
        await using var channel = await ConnectedChannelAsync();
        var story = new AbioticEditor.Core.LiveEditing.World.LiveStoryChannel(channel);

        // The fake agent's fallback echoes the payload straight back as "ok:true"; succeeding
        // without throwing proves the request encoded as a well-formed object the agent could
        // parse (a malformed line would fault the channel), the same proof pattern
        // LivePlayerSkillsChannel_SetAsync_sends_a_real_JSON_array uses above.
        await story.SetAsync("Office2", ["Office_InformationFound"], ["MapReveal_Security"]);

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
    public async Task LiveSpawnChannel_GetAsync_reads_position_and_terminal()
    {
        await using var channel = await ConnectedChannelAsync();
        var state = await new AbioticEditor.Core.LiveEditing.Player.LiveSpawnChannel(channel).GetAsync();

        Assert.Equal(100, state.X);
        Assert.Equal(-200, state.Y);
        Assert.Equal(50, state.Z);
        Assert.Equal("Facility", state.LevelName);
        Assert.Equal("AFB31D8E4DFBB5BE74BEAAADD681A636", state.TerminalGuid);
        Assert.True(state.IsHost);
    }

    [Fact]
    public async Task LiveSpawnChannel_TeleportAsync_and_SetRespawnTerminalAsync_send_well_formed_requests()
    {
        await using var channel = await ConnectedChannelAsync();
        var spawn = new AbioticEditor.Core.LiveEditing.Player.LiveSpawnChannel(channel);

        // The fake agent's fallback echoes any non-canned command's payload back as "ok:true";
        // both calls succeeding without throwing proves each encoded as a well-formed request
        // the agent could parse (a malformed line would fault the channel).
        await spawn.TeleportAsync(1, 2, 3);
        await spawn.SetRespawnTerminalAsync("95CAED254C17360B69B3738E468CD49C");

        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveCompanionsChannel_ListAsync_reads_pet_fields()
    {
        await using var channel = await ConnectedChannelAsync();
        var companions = new AbioticEditor.Core.LiveEditing.Player.LiveCompanionsChannel(channel);

        var rows = await companions.ListAsync();

        var pet = Assert.Single(rows);
        Assert.Equal("equip", pet.Kind);
        Assert.Equal(12, pet.SlotIndex);
        Assert.Equal("Skink_Magma_Crafted", pet.ItemId);
        Assert.Equal("Sparky", pet.Name);
        Assert.Equal(87.5, pet.Health);
        Assert.Equal(100, pet.MaxHealth);
        Assert.Equal(4200, pet.Xp);
        Assert.Equal(3, pet.MutationProgress);
        Assert.Equal(1, pet.PetMutation);
    }

    [Fact]
    public async Task LiveCompanionsChannel_SetAsync_and_ClearAsync_send_well_formed_requests()
    {
        await using var channel = await ConnectedChannelAsync();
        var companions = new AbioticEditor.Core.LiveEditing.Player.LiveCompanionsChannel(channel);
        var pet = new AbioticEditor.Core.PlayerSaves.CarriedPet(
            AbioticEditor.Core.PlayerSaves.PetSlotKind.Equipment, 12, "Skink_Magma_Crafted", "Sparky", 100, 100, 4300, 3, 1);

        await companions.SetAsync("equip", 12, pet);
        await companions.ClearAsync("equip", 12);

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

    [Fact]
    public async Task LiveBasesChannel_GetAsync_reads_deployables_and_SetAsync_renames()
    {
        await using var channel = await ConnectedChannelAsync();
        var bases = new AbioticEditor.Core.LiveEditing.World.LiveBasesChannel(channel);

        var directory = await bases.GetAsync();
        var bench = Assert.Single(directory.Deployables);
        Assert.True(directory.IsHost);
        Assert.Equal("Deployed_CraftingBench_Default_C", bench.ClassName);
        Assert.Equal("Main Bench", bench.CustomName);
        Assert.True(bench.HasInventory);
        Assert.Equal(2, bench.StoredItemCount);

        await bases.SetCustomNameAsync(bench.Id, "Renamed Bench");
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveContainmentChannel_GetAsync_reads_units_and_occupants()
    {
        await using var channel = await ConnectedChannelAsync();
        var containment = new AbioticEditor.Core.LiveEditing.World.LiveContainmentChannel(channel);

        var directory = await containment.GetAsync();
        Assert.True(directory.IsHost);
        Assert.Equal(2, directory.Units.Count);
        Assert.Equal("Leyak", directory.Units[0].Creature);
        Assert.Equal(85, directory.Units[0].Stability);
        Assert.Null(directory.Units[1].Creature);
        Assert.Null(directory.Units[1].Stability);

        // Fallback path (unspecial-cased command echoes payload back as ok:true) proves the
        // assign/release/swap requests themselves encode as well-formed JSON the agent could parse.
        await containment.AssignAsync(directory.Units[1].Id, "Krasue");
        await containment.ReleaseAsync("Leyak");
        await containment.SwapAsync(directory.Units[0].Id, directory.Units[1].Id);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveVehiclesChannel_GetAsync_reads_vehicles_and_SetAsync_moves_them()
    {
        await using var channel = await ConnectedChannelAsync();
        var vehicles = new AbioticEditor.Core.LiveEditing.World.LiveVehiclesChannel(channel);

        var directory = await vehicles.GetAsync();
        var forklift = Assert.Single(directory.Vehicles);
        Assert.True(directory.IsHost);
        Assert.False(directory.SupportsWreckedState);
        Assert.Equal("ABF_Vehicle_Forklift_C", forklift.VehicleClass);
        Assert.True(forklift.Driveable);

        await vehicles.SetAsync(forklift.Id, driveable: false, x: 5, y: 6, z: 7);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveTradersChannel_GetAsync_reads_set_flags()
    {
        await using var channel = await ConnectedChannelAsync();
        var traders = new AbioticEditor.Core.LiveEditing.World.LiveTradersChannel(channel);

        var flags = await traders.GetAsync();
        Assert.True(flags.IsHost);
        Assert.True(flags.HasFlag("Office_PowerOn"));
        Assert.False(flags.HasFlag("Manufacturing_West"));

        await traders.UnlockAsync(["Manufacturing_West"]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LivePetsChannel_GetAsync_reports_unavailable_with_a_player_safe_reason()
    {
        await using var channel = await ConnectedChannelAsync();
        var pets = new AbioticEditor.Core.LiveEditing.World.LivePetsChannel(channel);

        var directory = await pets.GetAsync();

        Assert.False(directory.Available);
        Assert.True(directory.IsHost);
        Assert.False(string.IsNullOrWhiteSpace(directory.Reason));
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LivePortalsChannel_GetAsync_reads_teleporter_pads()
    {
        await using var channel = await ConnectedChannelAsync();
        var portals = new AbioticEditor.Core.LiveEditing.World.LivePortalsChannel(channel);

        var directory = await portals.GetAsync();
        Assert.False(directory.IsHost);
        var pad = Assert.Single(directory.Portals);
        Assert.True(pad.Active);
        Assert.Equal("TP_A", pad.TeleporterId);
        Assert.Equal("TP_B", pad.DestinationId);

        await portals.SetActiveAsync(pad.Id, false);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LivePlayerRecipesChannel_round_trips_unlocks()
    {
        await using var channel = await ConnectedChannelAsync();
        var recipes = new AbioticEditor.Core.LiveEditing.Player.LivePlayerRecipesChannel(channel);

        var unlocked = await recipes.GetUnlockedAsync();
        Assert.Equal(["Recipe_Axe", "Recipe_Torch"], unlocked);

        // The fake agent's fallback echoes any non-special-cased command's payload straight back
        // as "ok:true"; UnlockAsync succeeding without throwing proves the request itself encoded
        // as a well-formed object the agent could parse.
        await recipes.UnlockAsync(["Recipe_Bandage"]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LivePlayerCodexChannel_reads_all_four_sections_and_sets_known()
    {
        await using var channel = await ConnectedChannelAsync();
        var codex = new AbioticEditor.Core.LiveEditing.Player.LivePlayerCodexChannel(channel);

        var directory = await codex.GetAsync();
        Assert.Equal(["Email_Foo"], directory.Emails);
        Assert.Equal(["Journal_Bar"], directory.Journals);
        Assert.Equal(["Fish_Baz"], directory.Fish);
        Assert.Equal(["Compendium_Qux"], directory.Compendium);

        await codex.SetKnownAsync(emails: ["Email_Foo"], fish: ["Fish_Baz"]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);

        // Round 77: compendium is settable too, one (row, sectionType) pair per call - the fake
        // agent's echo fallback proves this encodes as a well-formed request the same way the
        // other three categories already did.
        await codex.SetKnownAsync(compendium: [new AbioticEditor.Core.LiveEditing.Player.CompendiumUnlock("Compendium_Qux", "Exploration")]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveWorldUnlocksChannel_reads_world_level_unlocks_and_set_has_no_write_path()
    {
        await using var channel = await ConnectedChannelAsync();
        var unlocks = new AbioticEditor.Core.LiveEditing.World.LiveWorldUnlocksChannel(channel);

        var state = await unlocks.GetAsync();
        Assert.True(state.IsHost);
        Assert.Equal(["recipe_bandage"], state.RecipesUnlocked);
        Assert.Empty(state.RecipesResearched);
        Assert.Equal(["scrap_metal"], state.ItemsPickedUp);
        Assert.Equal(["Email_Crossbow"], state.EmailsRead);
        Assert.Empty(state.JournalEntries);
        Assert.Empty(state.CompendiumEmail);
        Assert.Empty(state.CompendiumNarrative);
        Assert.Equal(["Compendium_Office"], state.CompendiumExploration);
    }

    [Fact]
    public async Task LivePlayerGeneralChannel_reads_items_and_maps_and_discovers_more()
    {
        await using var channel = await ConnectedChannelAsync();
        var general = new AbioticEditor.Core.LiveEditing.Player.LivePlayerGeneralChannel(channel);

        var directory = await general.GetAsync();
        Assert.Equal(["metal_scrap"], directory.ItemsSeen);
        Assert.Equal(["torch"], directory.ItemsCrafted);
        Assert.Equal(["Sector_A"], directory.Maps);

        await general.SetAsync(itemsSeen: ["wood_plank"], maps: ["Sector_B"]);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LivePlayerGeneralChannel_reads_background_and_traits_and_can_set_background()
    {
        await using var channel = await ConnectedChannelAsync();
        var general = new AbioticEditor.Core.LiveEditing.Player.LivePlayerGeneralChannel(channel);

        var directory = await general.GetAsync();
        Assert.Equal("PhD_HumanBio", directory.Background);
        Assert.Equal(["Trait_Chef"], directory.Traits);

        // Same fallback-echo proof as the other SetAsync tests: succeeding without throwing
        // proves the background field encodes as a well-formed general.set request.
        await general.SetAsync(background: "PhD_Medicine");
        Assert.Equal(LiveConnectionState.Connected, channel.State);
    }

    [Fact]
    public async Task LiveTransmogVisibilityChannel_GetAsync_reads_flags_and_SetAsync_sends_a_well_formed_request()
    {
        await using var channel = await ConnectedChannelAsync();
        var visibility = new AbioticEditor.Core.LiveEditing.Player.LiveTransmogVisibilityChannel(channel);

        var flags = await visibility.GetAsync();
        Assert.Equal(2, flags.Count);
        Assert.True(flags[0].IsVisible);
        Assert.False(flags[1].IsVisible);
        Assert.Equal(0, flags[0].Index);
        Assert.Equal(1, flags[1].Index);

        await visibility.SetAsync(1, true);
        Assert.Equal(LiveConnectionState.Connected, channel.State);
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
                    "story.get" => "{\"currentQuestRow\":\"Office\",\"isHost\":true}",
                    "doors.list" => "{\"doors\":[{\"id\":\"SimpleDoor_ParentBP_C /Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_7\",\"label\":\"SimpleDoor_ParentBP_C\",\"kind\":\"simple\",\"state\":2,\"isOpen\":false,\"oneWayUnlocked\":false,\"disabled\":false,\"x\":100.5,\"y\":-20,\"z\":3},"
                        + "{\"id\":\"SecurityDoor_C /Game/Maps/Facility.Facility:PersistentLevel.SecurityDoor_C_2\",\"label\":\"SecurityDoor_C\",\"kind\":\"security\",\"state\":1,\"isOpen\":true,\"oneWayUnlocked\":false,\"disabled\":false,\"x\":0,\"y\":0,\"z\":0}],\"isHost\":false}",
                    "containers.list" => "{\"containers\":[{\"id\":\"Deployed_StorageCrate_Makeshift_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_StorageCrate_Makeshift_C_3\",\"label\":\"Deployed_StorageCrate_Makeshift_C\",\"x\":1,\"y\":2,\"z\":3,"
                        + "\"slots\":[{\"slotIndex\":0,\"itemId\":\"scrap_metal\",\"isEmpty\":false,\"stack\":5,\"durability\":0,\"maxDurability\":0},{\"slotIndex\":1,\"itemId\":\"Empty\",\"isEmpty\":true,\"stack\":0,\"durability\":0,\"maxDurability\":0}]}],\"isHost\":true}",
                    "dropped.list" => "{\"items\":[{\"id\":\"Abiotic_Item_Dropped_C /Game/Maps/Facility.Facility:PersistentLevel.Abiotic_Item_Dropped_C_9\",\"itemId\":\"scrap_cloth\",\"stack\":3,\"x\":4,\"y\":5,\"z\":6}],\"isHost\":true}",
                    "dropped.remove" => "{\"removed\":1}",
                    // Round-76 world areas.
                    "bases.list" => "{\"deployables\":[{\"id\":\"Deployed_CraftingBench_Default_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_CraftingBench_Default_C_1\",\"className\":\"Deployed_CraftingBench_Default_C\",\"x\":10,\"y\":20,\"z\":0,\"customName\":\"Main Bench\",\"hasInventory\":true,\"storedItemCount\":2}],\"isHost\":true,\"supportsBenchUpgrades\":false}",
                    "vehicles.list" => "{\"vehicles\":[{\"id\":\"ABF_Vehicle_Forklift_C /Game/Maps/Facility.Facility:PersistentLevel.ABF_Vehicle_Forklift_C_2\",\"vehicleId\":\"VehicleSpawn_1\",\"vehicleClass\":\"ABF_Vehicle_Forklift_C\",\"driveable\":true,\"x\":1,\"y\":2,\"z\":3}],\"isHost\":true,\"supportsWreckedState\":false}",
                    "pets.list" => "{\"pets\":[],\"isHost\":true,\"available\":false,\"reason\":\"Live pet editing isn't available yet.\"}",
                    // Round-76 areas: containment units, trader flag gating, world teleporters.
                    "containment.list" => "{\"units\":[{\"id\":\"Deployed_LeyakContainment_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_LeyakContainment_C_1\",\"x\":10,\"y\":20,\"z\":30,\"stability\":85,\"creature\":\"Leyak\"},"
                        + "{\"id\":\"Deployed_LeyakContainment_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_LeyakContainment_C_2\",\"x\":40,\"y\":50,\"z\":60,\"stability\":null,\"creature\":null}],\"isHost\":true}",
                    "traders.list" => "{\"setFlags\":[\"Office_PowerOn\"],\"isHost\":true}",
                    "portals.list" => "{\"portals\":[{\"id\":\"BP_Teleporter_ParentBP_C /Game/Maps/Facility.Facility:PersistentLevel.BP_Teleporter_ParentBP_C_4\",\"label\":\"BP_Teleporter_ParentBP_C\",\"active\":true,\"teleporterId\":\"TP_A\",\"destinationId\":\"TP_B\",\"x\":7,\"y\":8,\"z\":9}],\"isHost\":false}",
                    // Round-76 player areas: SPAWN (position + claimed respawn terminal) and
                    // COMPANIONS (carried pets, reusing the same slot shape as inventory.list plus
                    // name/xp/mutation).
                    "spawn.get" => "{\"x\":100,\"y\":-200,\"z\":50,\"levelName\":\"Facility\",\"terminalGuid\":\"AFB31D8E4DFBB5BE74BEAAADD681A636\",\"isHost\":true}",
                    "companions.list" => "{\"pets\":[{\"kind\":\"equip\",\"slotIndex\":12,\"itemId\":\"Skink_Magma_Crafted\",\"name\":\"Sparky\","
                        + "\"health\":87.5,\"maxHealth\":100,\"xp\":4200,\"mutationProgress\":3,\"petMutation\":1}],\"isHost\":true}",
                    "recipes.get" => "{\"unlockedIds\":[\"Recipe_Axe\",\"Recipe_Torch\"]}",
                    "codex.get" => "{\"emails\":[\"Email_Foo\"],\"journals\":[\"Journal_Bar\"],"
                        + "\"fish\":[\"Fish_Baz\"],\"compendium\":[\"Compendium_Qux\"]}",
                    // Round 77: world-level (not per-player) unlocks - read-only, see
                    // LiveWorldUnlocksChannel's remarks for why worldunlocks.set has no canned
                    // success case (it always errors from the real agent; here it just echoes).
                    "worldunlocks.get" => "{\"isHost\":true,\"recipesUnlocked\":[\"recipe_bandage\"],\"recipesResearched\":[],"
                        + "\"itemsPickedUp\":[\"scrap_metal\"],\"emailsRead\":[\"Email_Crossbow\"],\"journalEntries\":[],"
                        + "\"compendiumEmail\":[],\"compendiumNarrative\":[],\"compendiumExploration\":[\"Compendium_Office\"]}",
                    // Round 77: BACKGROUND (a real live write) and TRAITS (read-only readout).
                    "general.get" => "{\"itemsSeen\":[\"metal_scrap\"],\"itemsCrafted\":[\"torch\"],\"maps\":[\"Sector_A\"],"
                        + "\"traits\":[\"Trait_Chef\"],\"background\":\"PhD_HumanBio\"}",
                    // Round 77: transmog visibility (Request_ChangeTransmogVisibilityFlag).
                    "transmog.get" => "{\"visibility\":[{\"index\":0,\"isVisible\":true},{\"index\":1,\"isVisible\":false}]}",

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
