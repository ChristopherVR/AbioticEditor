using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class WorldSaveReaderTests
{
    private readonly ITestOutputHelper _output;

    public WorldSaveReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FacilitySavePath => Path.Combine(
        Fixtures.CascadeDir ?? string.Empty,
        "WorldSave_Facility.sav");

    [Fact]
    public void ReadWorldFacility_FindsContainers()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath))
        {
            _output.WriteLine($"missing fixture: {FacilitySavePath}");
            return;
        }

        var save = WorldSaveReader.ReadFromFile(FacilitySavePath);

        var deployed = save.Containers.Where(c => c.Source == WorldContainerSource.Deployed).ToList();
        var custom = save.Containers.Where(c => c.Source == WorldContainerSource.Custom).ToList();

        _output.WriteLine($"Deployed containers (with non-empty inventory): {deployed.Count}");
        _output.WriteLine($"Custom inventories:                              {custom.Count}");
        _output.WriteLine($"Total containers:                                {save.Containers.Count}");

        // From the schema dump: 197 deployables in Facility have a non-empty
        // ContainerInventories_ array, plus 2 entries in CustomInventoryMap.
        Assert.True(deployed.Count > 100, $"expected >100 deployed containers, got {deployed.Count}");
        Assert.True(custom.Count >= 1, $"expected >=1 custom inventory, got {custom.Count}");

        // Spot check: at least one container should have a non-empty class name and a
        // non-empty first inventory.
        var withClass = deployed.FirstOrDefault(c => !string.IsNullOrEmpty(c.ClassName));
        Assert.NotNull(withClass);
        Assert.NotEmpty(withClass!.Inventories);
        _output.WriteLine($"Sample container: {withClass.Id}  class={withClass.ClassName}  slots={withClass.Inventories[0].Slots.Count}");
    }

    [Fact]
    public void ReadWriteRoundTrip_ProducesIdenticalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var original = File.ReadAllBytes(FacilitySavePath);
        var save = WorldSaveReader.ReadFromFile(FacilitySavePath);

        using var ms = new MemoryStream();
        save.Raw.WriteTo(ms);
        var rewritten = ms.ToArray();

        Assert.Equal(original.Length, rewritten.Length);
        Assert.True(original.AsSpan().SequenceEqual(rewritten), "round-trip diverged");
    }

    [Fact]
    public void ReadFlags_FindsKnownEntries()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath))
        {
            _output.WriteLine($"missing fixture: {FacilitySavePath}");
            return;
        }

        var save = WorldSaveReader.ReadFromFile(FacilitySavePath);

        _output.WriteLine($"WorldFlags count: {save.Flags.Count}");
        foreach (var f in save.Flags.Take(5)) _output.WriteLine($"  {f}");

        // From the schema dump, Facility had ~109 flags including these.
        Assert.True(save.Flags.Count > 50, $"expected >50 flags, got {save.Flags.Count}");
        Assert.Contains("Office_NewGameStarted", save.Flags);
    }

    [Fact]
    public void MutateFlags_PersistsThroughWrite()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-world-flags-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(FacilitySavePath, tempPath);

            var data = WorldSaveReader.ReadFromFile(tempPath);
            var originalCount = data.Flags.Count;
            const string sentinel = "editor_test_flag";

            var newFlags = data.Flags.Append(sentinel).ToList();
            WorldSaveWriter.ApplyFlags(data, newFlags);
            WorldSaveWriter.WriteToFile(data, tempPath);

            var reread = WorldSaveReader.ReadFromFile(tempPath);
            Assert.Equal(originalCount + 1, reread.Flags.Count);
            Assert.Contains(sentinel, reread.Flags);
            // Ensure pre-existing flags weren't blown away.
            Assert.Contains("Office_NewGameStarted", reread.Flags);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReadDoors_FindsKnownStates()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var save = WorldSaveReader.ReadFromFile(FacilitySavePath);

        var simple = save.Doors.Where(d => d.Kind == WorldDoorKind.Simple).ToList();
        var security = save.Doors.Where(d => d.Kind == WorldDoorKind.Security).ToList();
        _output.WriteLine($"Simple doors: {simple.Count}   Security doors: {security.Count}");

        foreach (var d in simple.Take(3))
        {
            _output.WriteLine($"  simple   {d.Id}  state={d.DoorState}  yaw={d.Yaw:F2}  oneWay={d.OneWayUnlocked}  noReset={d.NoReset}");
        }
        foreach (var d in security.Take(3))
        {
            _output.WriteLine($"  security {d.Id}  isOpen={d.IsDoorOpen}  noReset={d.NoReset}");
        }

        Assert.True(simple.Count > 0, "expected at least one simple door");
        Assert.True(security.Count > 0, "expected at least one security door");
        Assert.All(simple, d => Assert.NotNull(d.DoorState));
        Assert.All(security, d => Assert.NotNull(d.IsDoorOpen));
    }

    [Fact]
    public void MutateDoorState_PersistsThroughWrite()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-world-doors-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(FacilitySavePath, tempPath);

            var data = WorldSaveReader.ReadFromFile(tempPath);

            var simple = data.Doors.First(d => d.Kind == WorldDoorKind.Simple);
            var security = data.Doors.First(d => d.Kind == WorldDoorKind.Security);
            _output.WriteLine($"Before: simple={simple.DoorState} yaw={simple.Yaw}  security isOpen={security.IsDoorOpen}");

            // Pick a different enum value than the current one. The game's
            // E_DoorStates enum uses NewEnumerator{N}; ::NewEnumerator1 is
            // distinct from the common ::NewEnumerator0 default.
            var nextState = simple.DoorState == "E_DoorStates::NewEnumerator1"
                ? "E_DoorStates::NewEnumerator2"
                : "E_DoorStates::NewEnumerator1";
            var newYaw = (simple.Yaw ?? 0) + 13.5;
            var mutatedSimple = simple with { DoorState = nextState, Yaw = newYaw };

            var flippedOpen = !(security.IsDoorOpen ?? false);
            var mutatedSecurity = security with { IsDoorOpen = flippedOpen };

            WorldSaveWriter.ApplyDoors(data, new[] { mutatedSimple, mutatedSecurity });
            WorldSaveWriter.WriteToFile(data, tempPath);

            var reread = WorldSaveReader.ReadFromFile(tempPath);
            var rereadSimple = reread.Doors.First(d => d.Id == simple.Id);
            var rereadSecurity = reread.Doors.First(d => d.Id == security.Id);

            Assert.Equal(nextState, rereadSimple.DoorState);
            Assert.Equal(newYaw, rereadSimple.Yaw!.Value, 3);
            Assert.Equal(flippedOpen, rereadSecurity.IsDoorOpen);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MutateContainer_PersistsThroughWrite()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-world-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(FacilitySavePath, tempPath);

            // Load, find a container with at least one non-empty slot, mutate it.
            var data = WorldSaveReader.ReadFromFile(tempPath);

            var target = data.Containers.FirstOrDefault(c =>
                c.Inventories.Count > 0 && c.Inventories[0].Slots.Any(s => !s.IsEmpty));
            Assert.NotNull(target);

            var targetId = target!.Id;
            var origSlot = target.Inventories[0].Slots.First(s => !s.IsEmpty);
            _output.WriteLine($"Mutating container {targetId}, slot[{origSlot.Index}] {origSlot.ItemId} x{origSlot.Count}");

            // Bump count and durability by a clear, observable amount.
            var mutatedSlot = origSlot with { Count = origSlot.Count + 7, Durability = origSlot.Durability + 0.5 };
            var mutatedSlots = target.Inventories[0].Slots
                .Select(s => s.Index == origSlot.Index ? mutatedSlot : s)
                .ToList();
            var mutatedContainer = target with
            {
                Inventories = new[] { new WorldInventory(mutatedSlots) }
            };

            WorldSaveWriter.ApplyContainers(data, new[] { mutatedContainer });
            WorldSaveWriter.WriteToFile(data, tempPath);

            // Re-read from disk and verify the mutation survived.
            var reread = WorldSaveReader.ReadFromFile(tempPath);
            var rereadContainer = reread.Containers.FirstOrDefault(c => c.Id == targetId);
            Assert.NotNull(rereadContainer);
            var rereadSlot = rereadContainer!.Inventories[0].Slots[origSlot.Index];

            Assert.Equal(origSlot.Count + 7, rereadSlot.Count);
            Assert.Equal(origSlot.Durability + 0.5, rereadSlot.Durability, 6);
            // Unchanged fields should still match the original.
            Assert.Equal(origSlot.ItemId, rereadSlot.ItemId);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
