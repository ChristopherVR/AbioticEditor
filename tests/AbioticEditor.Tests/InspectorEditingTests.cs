using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using UeSaveGame;

namespace AbioticEditor.Tests;

public sealed class InspectorEditingTests
{
    private static readonly double[] VehiclePosition = [10, 3, 20];
    [Fact]
    public void Changing_a_crop_refreshes_the_scene_without_changing_the_original_save()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var session = new WorldSaveSession(data, path);
        var before = session.PlacedObjects;
        var garden = session.MapFeature("garden-plots")!.Entries.First(e => e.Fields.Any(f => f.Id.StartsWith("crop:", StringComparison.Ordinal)));
        var crop = garden.Fields.First(f => f.Id.StartsWith("crop:", StringComparison.Ordinal));
        var replacement = crop.Value == "Plant_Corn" ? "Plant_Tomato" : "Plant_Corn";
        var revision = session.PlacedTransformsRevision;
        Assert.True(session.SetMapFeatureField("garden-plots", garden.Key, crop.Id, replacement).Changed);
        Assert.True(session.PlacedTransformsRevision > revision);
        Assert.NotSame(before, session.PlacedObjects);
        Assert.Contains(session.FindPlacedObject(garden.Key)!.Crops!, c => c.Row == replacement);
        Assert.Contains(before.Single(o => o.Key == garden.Key).Crops!, c => c.Row == crop.Value);
        Assert.Equal(crop.Value, new GardenPlotsFeature().Read(data.Raw).Single(e => e.Key == garden.Key).Fields.Single(f => f.Id == crop.Id).Value);
        session.Revert();
        Assert.Equal(before.Single(o => o.Key == garden.Key).Crops, session.FindPlacedObject(garden.Key)!.Crops);
    }

    [Fact]
    public void Default_breakable_state_is_readable_and_a_new_entry_round_trips_without_changing_its_donor()
    {
        var path = Path.Combine(Fixtures.ServerWorldsDir ?? "", "WorldSave_Facility_DF_Labs.sav");
        if (!File.Exists(path)) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var feature = new DestructibleMapFeature();
        var before = feature.Read(data.Raw).ToArray();
        const string key = "/Game/Maps/Facility_DF_Labs.Facility_DF_Labs:PersistentLevel.IceWall_BP_C_99999";
        Assert.Equal("false", feature.ReadActor(data.Raw, key)!.Fields.Single().Value);
        Assert.False(feature.SetField(data.Raw, key, "broken", "false").Changed);
        Assert.Equal(before.Length, feature.Read(data.Raw).Count);
        Assert.True(feature.SetField(data.Raw, key, "broken", "true").Changed);
        using var bytes = new MemoryStream();
        data.Raw.WriteTo(bytes);
        bytes.Position = 0;
        var reloaded = SaveGame.LoadFrom(bytes);
        Assert.Equal("true", feature.ReadActor(reloaded, key)!.Fields.Single().Value);
        foreach (var entry in before)
            Assert.Equal(entry.Fields, feature.ReadActor(reloaded, entry.Key)!.Fields);
        var props = WorldMapAccessor.FindEntry(reloaded, feature.MapName, key)!;
        var actor = WorldMapAccessor.GetSoftObjectPath(props, "ActorPath_");
        Assert.Equal("PersistentLevel.IceWall_BP_C_99999", actor!.Value.SubPath);
    }

    [Fact]
    public void Vehicles_draw_their_saved_model_and_do_not_count_as_staged_copies()
    {
        var vehicle = new WorldVehicle("forklift", null, "/Game/Blueprints/Vehicles/Forklift.Forklift_C", true, false,
            1000, 2000, 300, 0, 0, 0, 1, 0, false);
        var scene = Base3DScene.Build([], _ => null, vehicles: [vehicle]);
        var drawn = Assert.Single(scene.Objects);
        Assert.Equal(vehicle.VehicleClass, drawn.Cls);
        Assert.Equal(VehiclePosition, drawn.P);
        Assert.Equal(0, scene.CopyCount);
        Assert.Single(scene.Apply(new Base3DFilter()));
    }

    [Theory]
    [InlineData("handler error: Close the game. \\\n&#x20;", "Close the game.")]
    [InlineData("Recipe &amp; power required.\r\n", "Recipe & power required.")]
    public void Live_errors_are_plain_player_messages(string wire, string expected)
        => Assert.Equal(expected, new LiveAgentException(wire).Message);

    [Fact]
    public void Live_region_filter_matches_normalized_nested_package_names()
    {
        var live = new LiveSessionService();
        live.SetCurrentRegion("/Game/Maps/Sectors/UEDPIE_2_Facility_DF_Central.Facility_DF_Central:PersistentLevel");
        Assert.True(live.IsActorInCurrentRegion("Button_C /Game/Maps/Sectors/Facility_DF_Central.Facility_DF_Central:PersistentLevel.Button_C_1"));
        Assert.False(live.IsActorInCurrentRegion("/Game/Maps/Facility_DarkFusion.Facility_DarkFusion:PersistentLevel.Button_C_1"));
    }

    [Fact]
    public void Broad_facility_ownership_preserves_a_vehicles_explicit_dam_spawner_section()
    {
        var live = new LiveSessionService();
        live.SetCurrentRegion("Facility_Dam");
        const string path = "/Game/Maps/Facility_Dam.Facility_Dam:PersistentLevel.VehicleSpawner_C_1";
        Assert.True(live.IsActorInCurrentRegion(path, "Facility"));
        live.SetCurrentRegion("Facility_Office1");
        Assert.False(live.IsActorInCurrentRegion(path, "Facility"));
    }

    [Theory]
    [InlineData("NarrativeNPC_UnlostMage_C_1", "Mage_of_the_Unlost.png")]
    [InlineData("NarrativeNPC_HammeringHank_C_1", "T_Compendium_Hank.png")]
    [InlineData("NarrativeNPC_Exor_ParentBP_C_1", "Exor.png")]
    [InlineData("Isaiah Deal", "T_Compendium_IsaiahDeal.png")]
    public void Runtime_narrative_instances_have_portraits_even_without_a_cooked_name(string actor, string picture)
        => Assert.Equal(picture, Assert.Single(HologramPortraitCatalog.CandidatesFor(actor)));

    [Fact]
    public void Cooked_child_recall_button_follows_its_station_attachment_instead_of_level_origin()
    {
        using var assets = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        const string map = "/Game/Maps/Facility_Dam.Facility_Dam:PersistentLevel.";
        var station = assets.TryGetActorWorldTransform(map + "VehicleRecallStation_C_3");
        var button = assets.TryGetActorWorldTransform(map + "RecallButton_GEN_VARIABLE_Button_VehicleRecall_C_CAT_389");
        Assert.NotNull(station);
        Assert.NotNull(button);
        var distance = Math.Sqrt(Math.Pow(station.Value.X - button.Value.X, 2) + Math.Pow(station.Value.Y - button.Value.Y, 2) + Math.Pow(station.Value.Z - button.Value.Z, 2));
        Assert.InRange(distance, 0, 300);
        Assert.True(Math.Abs(button.Value.Z) > 100);
    }

    [Fact]
    public void Runtime_actors_use_the_game_reported_section_instead_of_the_persistent_outer()
    {
        var live = new LiveSessionService();
        live.SetCurrentRegion("Facility_DF_Central");
        const string path = "/Game/Maps/Facility.Facility:PersistentLevel.Deployed_ChemistryBench_C_1";
        Assert.True(live.IsActorInCurrentRegion(path, "Facility_DF_Central"));
        Assert.False(live.IsActorInCurrentRegion(path, "Facility_DarkFusion"));
    }

    [Theory]
    [InlineData("CharacterCorpse_OrderGrunt_C", "Order - Grunt")]
    [InlineData("CharacterCorpse_Human_BP_C", "Human")]
    public void Corpse_names_are_readable(string actor, string expected)
        => Assert.Equal(expected, AbioticEditor.Web.Components.Shared.PlainNames.Corpse(actor));

    [Fact]
    public async Task Live_pet_add_prefers_the_empty_companion_slot_and_guards_against_overwrite()
    {
        var channel = new PetChannel();
        await new LiveCompanionsChannel(channel).AddAsync("pet_skink", PetSlotKind.Equipment, "Sprout");
        Assert.Equal("equip", channel.Write.GetProperty("kind").GetString());
        Assert.Equal(12, channel.Write.GetProperty("slotIndex").GetInt32());
        Assert.True(channel.Write.GetProperty("requireEmpty").GetBoolean());
        Assert.Equal("Sprout", channel.Write.GetProperty("name").GetString());
        Assert.True(Guid.TryParseExact(channel.Write.GetProperty("assetId").GetString(), "N", out _));
        channel.Full = true;
        await Assert.ThrowsAsync<LiveAgentException>(() => new LiveCompanionsChannel(channel).AddAsync("pet_skink", PetSlotKind.Hotbar, null));
        Assert.Equal(1, channel.Writes);
    }

    [Fact]
    public void Pictures_cover_corpses_child_sockets_and_both_lamp_states()
    {
        Assert.NotNull(WorldThumbnails.For("corpses", "CharacterCorpse_Human_BP_C_5"));
        Assert.NotNull(WorldThumbnails.For("power-sockets", "PowerSocket_ChildOfActor_C_3"));
        Assert.NotEqual(WorldThumbnails.ForLampState("Deployed_Lamp_Sconce_C", false), WorldThumbnails.ForLampState("Deployed_Lamp_Sconce_C", true));
        Assert.NotNull(WorldThumbnails.For("buttons", "Button_NewVariant_C_1"));
    }

    [Theory]
    [InlineData("#lamp=0", false)]
    [InlineData("#lamp=1", true)]
    public void Lamp_state_reaches_the_model_provider(string variant, bool on)
    {
        const string cls = "/Game/B/Deployed_Lamp_Sconce.Deployed_Lamp_Sconce_C";
        var key = SceneModelHostService.ParseModelKey(cls + variant);
        Assert.Equal(cls, key.ClassPath);
        Assert.Equal(on, key.State!.LampOn);
    }

    [Fact]
    public async Task Runtime_spawner_locations_use_the_position_reported_by_the_game()
    {
        var session = await LiveNpcSpawnsFeatureSession.ConnectAsync(new LiveNpcSpawnsChannel(new PetChannel()));
        var spawner = Assert.Single(session.Spawners);
        var target = WorldFeaturePositions.Target(session, "npc-spawns", spawner.Id, spawner.Label);
        Assert.NotNull(target);
        Assert.Equal(new PlacedVector(123, 456, 789), target.At);
    }

    private sealed class PetChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public bool Full { get; set; }
        public int Writes { get; private set; }
        public JsonElement Write { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload = null, CancellationToken cancellationToken = default)
        {
            if (command == "npcspawns.list")
                return Task.FromResult(JsonSerializer.Deserialize<T>("""
                    {"isHost":true,"spawners":[{"id":"NPCSpawn_Peccary_C /Game/Maps/Facility.Facility:PersistentLevel.NPCSpawn_Peccary_C_24532","label":"Peccary","controllable":true,"x":123,"y":456,"z":789}]}
                    """, JsonOptions)!);
            if (command == "inventory.list")
                return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(new[]
                {
                    new { kind = "equip", slotIndex = 12, itemId = Full ? "pet_pest" : "Empty", isEmpty = !Full, stack = 0 },
                    new { kind = "hotbar", slotIndex = 0, itemId = "screwdriver", isEmpty = false, stack = 1 },
                }), JsonOptions)!);
            Assert.Equal("companions.set", command);
            Write = JsonSerializer.SerializeToElement(payload, JsonOptions);
            Writes++;
            return Task.FromResult(default(T)!);
        }
    }
}
