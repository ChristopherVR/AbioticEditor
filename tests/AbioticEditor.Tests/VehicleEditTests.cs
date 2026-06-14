using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>Vehicles (VehicleMap): read, edit state/transform, and on-board inventory.</summary>
public class VehicleEditTests
{
    private static string? FacilityWithVehicles()
    {
        if (Fixtures.ServerWorldsDir is null) return null;
        var p = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_Facility.sav");
        return File.Exists(p) ? p : null;
    }

    [Fact]
    public void Vehicles_are_read_with_class_state_transform_and_region()
    {
        var facility = FacilityWithVehicles();
        if (facility is null) return; // fixture absent: skip gracefully

        var vehicles = WorldSaveReader.ReadFromFile(facility).Vehicles;

        Assert.NotEmpty(vehicles);
        Assert.All(vehicles, v => Assert.False(string.IsNullOrEmpty(v.VehicleClass)));
        Assert.Contains(vehicles, v => v.DisplayName == "Forklift");
        // Region is parsed from the spawn actor path.
        Assert.All(vehicles, v => Assert.False(string.IsNullOrEmpty(v.Region)));
        // At least one vehicle has a recorded (non-origin) transform.
        Assert.Contains(vehicles, v => v.X != 0 || v.Y != 0 || v.Z != 0);
    }

    [Fact]
    public void Editing_state_and_transform_round_trips()
    {
        var facility = FacilityWithVehicles();
        if (facility is null) return;

        var dir = Directory.CreateTempSubdirectory("veh-edit");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(facility, copy);

            var data = WorldSaveReader.ReadFromFile(copy);
            var v = data.Vehicles[0];

            var updated = v with { Driveable = !v.Driveable, Destroyed = !v.Destroyed, X = 111, Y = 222, Z = 333 };
            WorldSaveWriter.ApplyVehicles(data, new[] { updated });
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Vehicles.First(r => r.Id == v.Id);
            Assert.Equal(!v.Driveable, reloaded.Driveable);
            Assert.Equal(!v.Destroyed, reloaded.Destroyed);
            Assert.Equal(111, reloaded.X, 3);
            Assert.Equal(222, reloaded.Y, 3);
            Assert.Equal(333, reloaded.Z, 3);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Vehicle_inventory_is_exposed_as_a_container_and_edits_round_trip()
    {
        var facility = FacilityWithVehicles();
        if (facility is null) return;

        var dir = Directory.CreateTempSubdirectory("veh-inv");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(facility, copy);

            var data = WorldSaveReader.ReadFromFile(copy);
            // A vehicle container with at least one occupied slot.
            var container = data.Containers.FirstOrDefault(c => c.Source == WorldContainerSource.Vehicle
                && c.Inventories.Any(inv => inv.Slots.Any(s => !s.IsEmpty && s.ItemId != "Empty")));
            if (container is null) return; // no stocked vehicle in this fixture: skip

            var inv = container.Inventories.First(i => i.Slots.Any(s => !s.IsEmpty && s.ItemId != "Empty"));
            var newSlots = inv.Slots.ToList();
            var slotIndex = newSlots.FindIndex(s => !s.IsEmpty && s.ItemId != "Empty");
            var slot = newSlots[slotIndex];
            var newCount = slot.Count + 7;

            newSlots[slotIndex] = slot with { Count = newCount };
            var newInv = new WorldInventory(newSlots);
            var newInventories = container.Inventories
                .Select(i => ReferenceEquals(i, inv) ? newInv : i).ToList();
            var updated = container with { Inventories = newInventories };

            WorldSaveWriter.ApplyContainers(data, new[] { updated });
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Containers
                .First(c => c.Source == WorldContainerSource.Vehicle && c.Id == container.Id);
            Assert.Equal(newCount, reloaded.Inventories[0].Slots[slotIndex].Count);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
