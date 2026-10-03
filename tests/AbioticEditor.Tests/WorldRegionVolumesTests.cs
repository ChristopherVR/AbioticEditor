using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class WorldRegionVolumesTests
{
    [Theory]
    [InlineData("Facility_Office4", -13571.052, 18099.629, 1647.4827)]
    [InlineData("Facility_Dam", -23300, 2400, 2800)]
    public void Cooked_brush_centres_belong_to_their_section(string region, double x, double y, double z)
    {
        Assert.True(WorldRegionVolumes.HasRegion(region));
        Assert.True(WorldRegionVolumes.Contains(region, new(x, y, z)));
        Assert.False(WorldRegionVolumes.Contains(region, new(x, y, z + 100000)));
    }

    [Fact]
    public void Persistent_actors_are_filtered_by_position_instead_of_the_Facility_outer()
    {
        var live = new LiveSessionService();
        live.SetCurrentRegion("Facility_Dam");
        const string actor = "/Game/Maps/Facility.Facility:PersistentLevel.Tram_ParentBP_C_7";
        Assert.True(live.IsActorInCurrentRegion(actor, "Facility", new(-23300, 2400, 2800)));
        Assert.False(live.IsActorInCurrentRegion(actor, "Facility", new(-13571.052, 18099.629, 1647.4827)));
        // A concrete spawner section remains authoritative even when the runtime outer is Facility.
        Assert.True(live.IsActorInCurrentRegion("/Game/Maps/Facility_Dam.Facility_Dam:PersistentLevel.VehicleSpawner_C_1", "Facility"));
    }

    [Fact]
    public void Rotated_office_brush_does_not_use_its_axis_aligned_envelope_as_membership()
    {
        var inside = new PlacedVector(-13571.052 + 500, 18099.629 + 500, 1647.4827);
        // This point lies in the rotated bounding envelope but outside its local cube.
        var outside = new PlacedVector(-13571.052 + 2500, 18099.629 - 2500, 1647.4827);
        Assert.False(WorldRegionVolumes.Contains("Facility_Office4", outside));
        Assert.True(WorldRegionVolumes.Contains("Facility_Office4", inside));
        Assert.False(WorldRegionVolumes.Contains("unknown", inside));
    }
}
