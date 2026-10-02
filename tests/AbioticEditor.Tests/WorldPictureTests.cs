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
        foreach (var kind in new[] { "deployables", "trams-each" })
        {
            var dir = Path.Combine(root, kind);
            Assert.True(Directory.Exists(dir), $"{kind} pictures are checked in");
            foreach (var file in Directory.GetFiles(dir, "*.webp"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var url = kind == "deployables"
                    ? WorldThumbnails.ForPlaced(name)
                    : WorldThumbnails.For("trams", $"/Game/Maps/Facility.Facility:PersistentLevel.{name}");
                Assert.Equal($"{Root}/{kind}/{name}.webp", url);
            }
        }
    }
}
