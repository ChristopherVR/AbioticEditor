using System.IO;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class WorldLocationIndexTests
{
    private static WorldSaveData? LoadFacility()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        return File.Exists(path) ? WorldSaveReader.ReadFromFile(path) : null;
    }

    [Fact]
    public void Fixture_index_never_places_a_marker_at_the_origin_and_reports_partial_coverage()
    {
        var data = LoadFacility();
        if (data is null) return;

        var index = WorldLocationIndex.Build("WorldSave_Facility", data);
        Assert.NotEmpty(index.Entries);

        foreach (var e in index.Entries)
        {
            if (e.HasPosition)
            {
                Assert.False(e.X == 0 && e.Y == 0 && e.Z == 0, $"origin marker for {e.Key}");
            }
            else
            {
                Assert.Null(e.X);
            }
            if (e.Kind == LocationKind.Unresolved) Assert.NotEqual(UnresolvedReason.None, e.Reason);
        }

        // No game assets in this call, so every door must be unresolved with a stated reason.
        var doors = index.Entries.Where(e => e.Category == LocatedCategory.Door).ToList();
        Assert.All(doors, d => Assert.Equal(UnresolvedReason.MissingGeometry, d.Reason));

        var report = index.Coverage();
        Assert.Equal(index.Entries.Count, report.Total);
        if (doors.Count > 0)
        {
            Assert.False(report.IsComplete);
            Assert.Contains("PARTIAL", report.ToText());
        }
    }

    [Fact]
    public void Door_placements_resolve_as_static_and_duplicate_names_stay_separate_per_level()
    {
        var doors = new[]
        {
            new WorldDoor("/Game/Maps/Facility_Office1.Facility_Office1:PersistentLevel.SimpleDoor_C_1", WorldDoorKind.Simple, null, null, null, null, null),
            new WorldDoor("/Game/Maps/Facility_Labs.Facility_Labs:PersistentLevel.SimpleDoor_C_1", WorldDoorKind.Simple, null, null, null, null, null),
            new WorldDoor("/Game/Maps/Facility_Labs.Facility_Labs:PersistentLevel.SimpleDoor_C_2", WorldDoorKind.Simple, null, null, null, null, null),
        };
        var data = new WorldSaveData(null!, Array.Empty<WorldContainer>(), Array.Empty<string>(), doors);

        var index = WorldLocationIndex.Build("WorldSave_Facility", data,
            (map, actor) => map == "Facility_Office1" && actor == "SimpleDoor_C_1" ? new DoorWorldLocation(10, 20, 30) : null);

        Assert.Single(index.FindByActorName("Facility_Office1", "SimpleDoor_C_1"));
        Assert.Single(index.FindByActorName("Facility_Labs", "SimpleDoor_C_1"));
        Assert.Contains("SimpleDoor_C_1", index.ActorNamesSharedAcrossLevels());

        var placed = index.FindByActorName("Facility_Office1", "SimpleDoor_C_1")[0];
        Assert.Equal(LocationKind.StaticPlacement, placed.Kind);
        Assert.True(placed.HasPosition);

        var missing = index.FindByActorName("Facility_Labs", "SimpleDoor_C_2")[0];
        Assert.Equal(LocationKind.Unresolved, missing.Kind);
        Assert.Equal(UnresolvedReason.ObsoletePath, missing.Reason);
        Assert.False(missing.HasPosition);
    }

    [Fact]
    public void Vehicle_container_is_carried_and_zero_position_is_unresolved()
    {
        var veh = new WorldVehicle("Veh_1", "v", "Cls_C", true, false, 5, 6, 7, 0, 0, 0, 1, 0, true, ContainerId: "Box_1");
        var box = new WorldContainer("Box_1", WorldContainerSource.Vehicle, "Box_C", Array.Empty<WorldInventory>());
        var npc = new WorldNpc("Npc_1", false, null);
        var data = new WorldSaveData(null!, new[] { box }, Array.Empty<string>(), Array.Empty<WorldDoor>(),
            npcs: new[] { npc }, vehicles: new[] { veh });

        var index = WorldLocationIndex.Build("R", data);
        var c = index.FindBySaveIdentity("Box_1")!;
        Assert.Equal(LocationKind.CarriedOrContained, c.Kind);
        Assert.Equal("Veh_1", c.OwnerActorPath);
        Assert.False(c.HasPosition);
        Assert.Equal(UnresolvedReason.NoSavedPosition, index.FindBySaveIdentity("Npc_1")!.Reason);
        Assert.Equal(LocationKind.LastSavedPosition, index.FindBySaveIdentity("Veh_1")!.Kind);
    }
}
