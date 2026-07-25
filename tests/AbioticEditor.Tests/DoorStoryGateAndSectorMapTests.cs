using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the two things the Doors tab reads out of the cooked levels rather than the save:
/// which world flag opens a door (<see cref="DoorGateResolver"/>), and where the door sits on
/// the game's own drawn sector map (<see cref="SectorMapCalibration"/>).
/// </summary>
public class DoorStoryGateAndSectorMapTests
{
    /// <summary>
    /// Story control is a property of the placed door, never of its blueprint class - a survey
    /// of all 77 cooked sub-levels found 11 gated doors in the whole game. Labelling a class
    /// "Flag" therefore marked hundreds of ordinary doors as story controlled, so no class may
    /// carry that lock kind any more.
    /// </summary>
    [Fact]
    public void No_door_class_claims_to_be_story_controlled()
    {
        var offenders = DoorClassCatalog.KnownClasses.Values
            .Where(c => c.LockKind == "Flag")
            .Select(c => c.ClassName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Story_gated_doors_name_their_flag()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        // The two cell doors in the labs that the turret shutdown opens.
        var labs = DoorGateResolver.ForMap(provider, "Facility_Labs");
        Assert.Equal("LABS_TurretsDeactivated", labs["SlidingCellDoor_BP_C_13"].UnlockFlag);
        Assert.Equal("LABS_TurretsDeactivated", labs["SlidingCellDoor_BP_C_19"].UnlockFlag);

        // Containment mixes two different flags across its cell doors.
        var containment = DoorGateResolver.ForMap(provider, "Facility_Containment");
        Assert.Equal("LABS_ReachedCommandCenter", containment["SlidingCellDoor_BP_C_4"].UnlockFlag);
        Assert.Equal("LABS_TurretsDeactivated", containment["SlidingCellDoor_BP_C_1"].UnlockFlag);

        // The residence has the only stay-open gates in the game (a post-cutscene door).
        var residence = DoorGateResolver.ForMap(provider, "Facility_Residence");
        var cinematic = residence["SimpleDoor_ParentBP_C_3"];
        Assert.Null(cinematic.UnlockFlag);
        Assert.Equal("Res_HastaTria_EndCutscene", cinematic.RemainOpenFlag);
        Assert.Equal("Res_HastaTria_EndCutscene", cinematic.PrimaryFlag);
        Assert.True(cinematic.IsStoryGated);
    }

    [Fact]
    public void Ordinary_doors_have_no_gate()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        // The starting office is full of plain hinged doors and gates none of them.
        Assert.Empty(DoorGateResolver.ForMap(provider, "Facility_Office1"));
        Assert.Null(DoorGateResolver.Resolve(provider, "Facility_Labs", "SimpleDoor_ParentBP_C_0"));
    }

    [Fact]
    public void Unknown_map_returns_empty_not_throws()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        Assert.Empty(DoorGateResolver.ForMap(provider, "Facility_DoesNotExist_99"));
        Assert.Null(SectorMapCalibration.FitFor("Facility_DoesNotExist_99"));
        Assert.Null(SectorMapCalibration.FitFor(null));
    }

    /// <summary>
    /// Levels whose pamphlet is missing, unusable or that would not calibrate must stay out of
    /// the table so the UI falls back to the plain plot instead of pinning doors on artwork
    /// that does not depict them.
    /// </summary>
    [Theory]
    [InlineData("Facility_Containment")] // the game ships this pointing at the Office 1 artwork
    [InlineData("Facility_Security")]    // drawing reads "SITE MAP UNAVAILABLE"
    [InlineData("Facility_Residence")]   // drawing is a washed-out blank
    [InlineData("Facility_Office2")]     // no orientation put its lifts where the drawing does
    [InlineData("Facility_Dam")]
    [InlineData("Facility_MFMines")]     // no pamphlet at all
    public void Levels_without_a_usable_map_are_not_calibrated(string level)
        => Assert.Null(SectorMapCalibration.FitFor(level));

    [Theory]
    [InlineData("Facility_Office1", "Map_Office1")]
    [InlineData("Facility_Office3", "Map_Office3")]
    [InlineData("Facility_Labs", "Map_Lab")]
    [InlineData("Facility_MFWest", "Map_MF")]
    [InlineData("Facility_Pens", "Map_Pens")]
    [InlineData("Facility_DarkFusion", "Map_Reactors")]
    public void Calibrated_levels_point_at_their_pamphlet(string level, string row)
    {
        var fit = SectorMapCalibration.FitFor(level);
        Assert.NotNull(fit);
        Assert.Equal(row, fit!.PamphletRow);
        Assert.InRange(fit.Variant, 0, 7);
        Assert.True(fit.ScaleX > 0 && fit.ScaleY > 0);
    }

    /// <summary>
    /// Every calibrated pamphlet row must actually exist in the game's data, or the map would
    /// silently never render.
    /// </summary>
    [Fact]
    public void Calibrated_pamphlet_rows_exist_in_the_game_data()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var maps = SectorMapCatalog.LoadFrom(provider);
        Assert.NotEmpty(maps);
        foreach (var (level, fit) in SectorMapCalibration.CalibratedLevels)
        {
            var info = SectorMapCatalog.ForRow(maps, fit.PamphletRow);
            Assert.True(info is not null, $"{level}: pamphlet row {fit.PamphletRow} is missing");
            Assert.False(string.IsNullOrWhiteSpace(info!.TexturePath));
        }
    }

    /// <summary>
    /// The whole point of the fit: a level's real doors land inside the drawing rather than off
    /// the page. Allowed some slack because a sub-level always extends past what its pamphlet
    /// draws, and off-page doors are dropped by the UI rather than clamped.
    /// </summary>
    [Fact]
    public void Most_doors_land_on_the_drawing()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        foreach (var (level, fit) in SectorMapCalibration.CalibratedLevels)
        {
            var actors = DoorLocationResolver.ForMap(provider, level);
            if (actors.Count == 0) continue;

            var projected = actors.Values
                .Select(a => SectorMapCalibration.Project(fit, a))
                .ToList();
            var onPage = projected.Count(p => p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1);
            Assert.True(onPage > projected.Count * 0.6,
                $"{level}: only {onPage}/{projected.Count} actors land on the drawing");
        }
    }

    [Fact]
    public void Variant_rotations_are_reversible()
    {
        // Variant 0 is the identity; 2 is a half turn; 1 and 3 undo each other.
        Assert.Equal((3.0, 5.0), SectorMapCalibration.ApplyVariant(3, 5, 0));
        Assert.Equal((-3.0, -5.0), SectorMapCalibration.ApplyVariant(3, 5, 2));
        var (x, y) = SectorMapCalibration.ApplyVariant(3, 5, 1);
        Assert.Equal((3.0, 5.0), SectorMapCalibration.ApplyVariant(x, y, 3));
        // Bit 2 flips X before rotating.
        Assert.Equal((-3.0, 5.0), SectorMapCalibration.ApplyVariant(3, 5, 4));
    }
}
