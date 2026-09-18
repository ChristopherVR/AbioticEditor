using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Closes the "on-board vehicle storage is not editable live" gap: before this round
/// <see cref="LiveVehiclesSession"/> hardcoded <c>HasInventory: false</c>/<c>InventoryItemCount: 0</c>
/// for every vehicle (see the round-76/77 <c>live-editing-protocol.md</c> note this round
/// replaces). The game's own class layout (probed this round, fragment "ABF_Vehicle_ParentBP")
/// showed a vehicle's on-board cargo is a genuine <c>Deployed_Container_ParentBP_C</c> actor (the
/// <c>StorageContainer</c> ChildActorComponent's resolved child, e.g.
/// <c>Deployed_Container_ForkliftCargo_C</c> for the forklift) - the exact class the existing
/// <c>containers.list</c>/<c>containers.get</c>/<c>containers.set</c> handlers already sweep, so
/// no new slot-edit code path was needed anywhere: <c>vehicles.lua</c> only needed to report which
/// container id belongs to which vehicle. Kept in its own file (not
/// <c>WorldLiveAreaParityContractTests.cs</c>) per <see cref="WorldLiveButtonsAreaTests"/>'s own
/// stated reasoning: a change here should never collide with concurrent work on other live areas'
/// entries in that shared file.
/// </summary>
public sealed class WorldLiveVehicleStorageTests
{
    [Fact]
    public async Task LiveVehiclesChannel_GetAsync_reads_the_on_board_container_fields()
    {
        var channel = new FakeVehiclesChannel();
        channel.SetVehicle("forklift-1", "ABF_Vehicle_Forklift_C",
            containerId: "Deployed_Container_ForkliftCargo_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_Container_ForkliftCargo_C_1",
            hasInventory: true, inventoryItemCount: 3);

        var vehicles = new LiveVehiclesChannel(channel);
        var directory = await vehicles.GetAsync();

        var forklift = Assert.Single(directory.Vehicles);
        Assert.True(forklift.HasInventory);
        Assert.Equal(3, forklift.InventoryItemCount);
        Assert.Equal(
            "Deployed_Container_ForkliftCargo_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_Container_ForkliftCargo_C_1",
            forklift.ContainerId);
    }

    [Fact]
    public async Task LiveVehiclesChannel_GetAsync_defaults_missing_container_fields_to_no_storage()
    {
        // An older live-agent build (before this round) never sends containerId/hasInventory/
        // inventoryItemCount at all - the wire record's own defaults must degrade to "no storage"
        // instead of throwing, the same graceful-degradation contract every other live area here
        // follows for a field an older mod build does not know about yet.
        var channel = new FakeVehiclesChannel { OmitStorageFields = true };
        channel.SetVehicle("forklift-1", "ABF_Vehicle_Forklift_C", containerId: null, hasInventory: false, inventoryItemCount: 0);

        var vehicles = new LiveVehiclesChannel(channel);
        var directory = await vehicles.GetAsync();

        var forklift = Assert.Single(directory.Vehicles);
        Assert.False(forklift.HasInventory);
        Assert.Equal(0, forklift.InventoryItemCount);
        Assert.Null(forklift.ContainerId);
    }

    [Fact]
    public async Task LiveVehiclesSession_surfaces_the_container_id_onto_WorldVehicle_for_the_shared_tab()
    {
        var channel = new FakeVehiclesChannel();
        channel.SetVehicle("cart-1", "ABF_Vehicle_SecurityCart_C",
            containerId: "Deployed_Container_SecurityCartCargo_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_Container_SecurityCartCargo_C_1",
            hasInventory: true, inventoryItemCount: 1);

        var session = await LiveVehiclesSession.ConnectAsync(new LiveVehiclesChannel(channel));

        var cart = Assert.Single(session.Vehicles);
        Assert.True(cart.HasInventory);
        Assert.Equal(1, cart.InventoryItemCount);
        Assert.Equal(
            "Deployed_Container_SecurityCartCargo_C /Game/Maps/Facility.Facility:PersistentLevel.Deployed_Container_SecurityCartCargo_C_1",
            cart.ContainerId);

        // WorldVehiclesTab.OpenContainer falls back to vehicle.Id only when ContainerId is null
        // (the file session's own shape - see WorldVehicle's remarks); a live vehicle with real
        // on-board storage must never fall back, since its own actor id is not a container id.
        Assert.NotEqual(cart.Id, cart.ContainerId);
    }

    [Fact]
    public void WorldVehicle_ContainerId_defaults_to_null_so_every_existing_positional_call_site_still_compiles()
    {
        var vehicle = new WorldVehicle("v1", "VehicleSpawn_1", "ABF_Vehicle_Forklift_C", Driveable: true, Destroyed: false,
            X: 1, Y: 2, Z: 3, QuatX: 0, QuatY: 0, QuatZ: 0, QuatW: 1, InventoryItemCount: 0, HasInventory: false);

        Assert.Null(vehicle.ContainerId);
    }

    [Fact]
    public void WorldVehiclesTab_opens_the_vehicle_ContainerId_not_always_its_own_Id()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldVehiclesTab.razor");
        Assert.Contains("OnOpenContainer.InvokeAsync(vehicle.ContainerId ?? vehicle.Id)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_OnOpenContainer_for_the_live_vehicles_tab()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<WorldVehiclesTab Session=\"_vehicles\"", source, StringComparison.Ordinal);
        Assert.Contains("OnOpenContainer=\"OpenCareContainerAsync\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicles_lua_grounds_on_board_storage_in_the_game_s_own_StorageContainer_component()
    {
        var source = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "live-agent",
            "AbioticEditorLiveAgentLua", "Scripts", "areas", "vehicles.lua"));
        Assert.Contains("StorageContainer", source, StringComparison.Ordinal);
        Assert.Contains("ChildActor", source, StringComparison.Ordinal);
        Assert.Contains("Deployed_Container_ParentBP_C", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_vehicle_on_board_storage()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("StorageContainer", doc, StringComparison.Ordinal);
        Assert.Contains("containerId", doc, StringComparison.Ordinal);
    }

    private sealed class FakeVehiclesChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, VehicleState> _vehicles = new(StringComparer.Ordinal);

        /// <summary>Simulates an older live-agent build that never sends the new fields at all.</summary>
        public bool OmitStorageFields { get; init; }

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetVehicle(string id, string vehicleClass, string? containerId, bool hasInventory, int inventoryItemCount)
            => _vehicles[id] = new VehicleState(vehicleClass, containerId, hasInventory, inventoryItemCount);

        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            object? result = command switch
            {
                "vehicles.list" => new
                {
                    vehicles = _vehicles.Select(kv => OmitStorageFields
                        ? (object)new
                        {
                            id = kv.Key,
                            vehicleId = kv.Key,
                            vehicleClass = kv.Value.VehicleClass,
                            driveable = true,
                            wrecked = false,
                            x = 0d, y = 0d, z = 0d,
                        }
                        : new
                        {
                            id = kv.Key,
                            vehicleId = kv.Key,
                            vehicleClass = kv.Value.VehicleClass,
                            driveable = true,
                            wrecked = false,
                            x = 0d, y = 0d, z = 0d,
                            containerId = kv.Value.ContainerId,
                            hasInventory = kv.Value.HasInventory,
                            inventoryItemCount = kv.Value.InventoryItemCount,
                        }).ToList(),
                    isHost = true,
                    supportsWreckedState = true,
                },
                "vehicles.set" => null,
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(element.Deserialize<TResponse>(JsonOptions)!);
        }

        private sealed record VehicleState(string VehicleClass, string? ContainerId, bool HasInventory, int InventoryItemCount);
    }
}
