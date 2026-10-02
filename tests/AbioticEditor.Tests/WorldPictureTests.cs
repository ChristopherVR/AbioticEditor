using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The pictures the world lists ship with for things the kind-per-class pictures used to miss:
/// resource nodes the game spawns at run time, each tram on its own, player-placed things
/// (teleporter pads, sconce lamps) and the player-built devices power outlets belong to.
/// </summary>
public sealed class WorldPictureTests
{
    private const string Root = "_content/AbioticEditor.Web.Shared/thumbs";

    [Fact]
    public void Resource_nodes_spawned_at_run_time_have_pictures()
    {
        Assert.Equal($"{Root}/resource-nodes/ResourceNode_WoodCrate_Reactors_C.webp",
            WorldThumbnails.For("resource-nodes", "/Game/Maps/Facility.Facility:PersistentLevel.ResourceNode_WoodCrate_Reactors_C_2147001528"));
        Assert.NotNull(WorldThumbnails.For("resource-nodes", "/Game/Maps/Facility.Facility:PersistentLevel.Resource_Micronode_KrasueEssence_C_2147177321"));
    }

    [Fact]
    public void Each_tram_shows_its_own_picture_and_others_fall_back_to_the_model()
    {
        Assert.Equal($"{Root}/trams-each/Tram_ParentBP_C_1.webp",
            WorldThumbnails.For("trams", "/Game/Maps/Facility.Facility:PersistentLevel.Tram_ParentBP_C_1"));
        Assert.Equal($"{Root}/trams-each/Tram_Default_C_3.webp",
            WorldThumbnails.For("trams", "/Game/Maps/Facility.Facility:PersistentLevel.Tram_Default_C_3"));
        Assert.Equal($"{Root}/trams/Tram_Default_C.webp",
            WorldThumbnails.For("trams", "/Game/Maps/Facility.Facility:PersistentLevel.Tram_Default_C_99"));
    }

    [Fact]
    public void Each_elevator_shows_its_own_picture_even_where_names_repeat_across_levels()
    {
        // Facility's Elevator 4 is an open platform deep in a shaft; the kind picture is a closed car.
        Assert.Equal($"{Root}/elevators-each/Facility__Elevator_ParentBP_C_3.webp",
            WorldThumbnails.For("elevators", "/Game/Maps/Facility.Facility:PersistentLevel.Elevator_ParentBP_C_3"));
        Assert.Equal($"{Root}/elevators-each/Facility_Dam__Elevator_ParentBP_C_3.webp",
            WorldThumbnails.For("elevators", "/Game/Maps/Facility_Dam.Facility_Dam:PersistentLevel.Elevator_ParentBP_C_3"));
        // The 3D view names a level actor <map>:<actor>.
        Assert.Equal($"{Root}/elevators-each/Facility_Dam__Elevator_ParentBP_C_3.webp",
            WorldThumbnails.For("elevators", "Facility_Dam:Elevator_ParentBP_C_3"));
        // An elevator without its own picture falls back to its kind's.
        Assert.Equal($"{Root}/elevators/Elevator_ParentBP_C.webp",
            WorldThumbnails.For("elevators", "/Game/Maps/Facility_Nowhere.Facility_Nowhere:PersistentLevel.Elevator_ParentBP_C_42"));
        Assert.NotNull(WorldThumbnails.PlaceOf("/Game/Maps/Facility.Facility:PersistentLevel.Elevator_ParentBP_C_3"));
    }

    [Fact]
    public void Kind_pictures_are_sharp_enough_for_the_detail_pane()
    {
        // The detail pane shows a picture at up to 240 px, 480 real pixels on a 2x screen.
        var root = UiSource.Resolve("wwwroot", "thumbs");
        foreach (var kind in new[] { "doors", "buttons", "containers", "deployables", "destructibles", "elevators", "elevators-each", "power-sockets", "resource-nodes", "trams", "trams-each" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, kind), "*.webp"))
            {
                var (width, height) = WebpSize(File.ReadAllBytes(file));
                Assert.True(width >= 512 && height >= 512, $"{kind}/{Path.GetFileName(file)} is {width}x{height}");
            }
        }
    }

    /// <summary>The canvas size of a WebP file (its VP8X, VP8L or VP8 header).</summary>
    private static (int Width, int Height) WebpSize(byte[] b)
    {
        var chunk = System.Text.Encoding.ASCII.GetString(b, 12, 4);
        return chunk switch
        {
            "VP8X" => (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16)),
            "VP8L" => (1 + ((b[21] | b[22] << 8) & 0x3FFF), 1 + ((b[22] >> 6 | b[23] << 2 | b[24] << 10) & 0x3FFF)),
            _ => (b[26] | (b[27] & 0x3F) << 8, b[28] | (b[29] & 0x3F) << 8),
        };
    }

    [Fact]
    public void Placed_things_show_their_own_picture()
    {
        Assert.Equal($"{Root}/deployables/Deployed_TeleporterPad_C.webp", WorldThumbnails.ForPlaced("Deployed_TeleporterPad_C"));
        Assert.Equal($"{Root}/deployables/Deployed_Lamp_Sconce_C.webp",
            WorldThumbnails.ForPlaced("/Game/Blueprints/DeployedObjects/Misc/Deployed_Lamp_Sconce.Deployed_Lamp_Sconce_C"));
        // Containers keep their own folder.
        Assert.Equal($"{Root}/containers/Container_Locker_C.webp", WorldThumbnails.ForPlaced("Container_Locker_C"));
        Assert.Null(WorldThumbnails.ForPlaced("Deployed_NoSuchThing_C"));
        Assert.Null(WorldThumbnails.ForPlaced(null));
    }

    [Fact]
    public void Power_outlets_on_built_devices_show_the_device()
    {
        const string device = "0123456789ABCDEF0123456789abcdef";
        Assert.Equal(device, WorldThumbnails.PowerOutletOwner(device + "2"));
        Assert.Null(WorldThumbnails.PowerOutletOwner(device));
        Assert.Null(WorldThumbnails.PowerOutletOwner("/Game/Maps/X.X:PersistentLevel.PowerSocket_ParentBP_C_4"));
        Assert.Null(WorldThumbnails.PowerOutletOwner(null));
        foreach (var cls in new[] { "Deployed_PlugStrip_C", "Deployed_Plugboard_C", "Deployed_Battery_T1_C", "Deployed_Battery_T2_C", "Deployed_Battery_T3_C" })
            Assert.NotNull(WorldThumbnails.ForPlaced(cls));

        // Wall sockets in the level keep their kind picture and their place shot.
        Assert.NotNull(WorldThumbnails.For("power-sockets", "/Game/Maps/X.X:PersistentLevel.PowerSocket_ParentBP_C_4"));
    }

    [Fact]
    public void Every_new_picture_is_really_shipped()
    {
        var root = UiSource.Resolve("wwwroot", "thumbs");
        foreach (var kind in new[] { "deployables", "trams-each", "elevators-each" })
        {
            var dir = Path.Combine(root, kind);
            Assert.True(Directory.Exists(dir), $"{kind} pictures are checked in");
            foreach (var file in Directory.GetFiles(dir, "*.webp"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var url = kind switch
                {
                    "deployables" => WorldThumbnails.ForPlaced(name),
                    "trams-each" => WorldThumbnails.For("trams", $"/Game/Maps/Facility.Facility:PersistentLevel.{name}"),
                    _ => WorldThumbnails.For("elevators", name[..name.IndexOf("__", StringComparison.Ordinal)] is var map
                        ? $"/Game/Maps/{map}.{map}:PersistentLevel.{name[(map.Length + 2)..]}" : null),
                };
                Assert.Equal($"{Root}/{kind}/{name}.webp", url);
            }
        }
    }
}
