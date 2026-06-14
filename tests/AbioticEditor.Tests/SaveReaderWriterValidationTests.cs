using System.IO;
using AbioticEditor.Core.Compare;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// End-to-end validation that the typed readers and writers form a faithful
/// read -> mutate-in-place -> re-serialize loop, exercised against the real fixture saves.
/// Two complementary properties are checked:
///
/// 1. REVERSIBILITY (byte identity): load a save, change a few values, then change them back
///    to the originals and re-serialize. The bytes must be identical to the file we started
///    with. This proves an edit followed by its inverse leaves no residue - the writer patches
///    values in place and never quietly restructures the tree for fields that were present.
///
/// 2. ISOLATION (surgical edit): make exactly one change, write it, reload from disk, and diff
///    the result against the original with <see cref="SaveComparer"/>. Only the intended leaf
///    may differ (or, for an edit that materializes delta-omitted siblings, the difference is
///    confined to that one entry's subtree); every other byte of the save is untouched.
///
/// These complement the existing per-feature "mutation persists" tests, which only confirm a
/// value survives a write - not that nothing ELSE moved.
/// </summary>
public class SaveReaderWriterValidationTests
{
    private readonly ITestOutputHelper _output;

    public SaveReaderWriterValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? PlayerSavePath()
    {
        if (Fixtures.CascadeDir is null) return null;
        var p = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(p) ? p : null;
    }

    private static string? FacilitySavePath()
    {
        if (Fixtures.CascadeDir is null) return null;
        var p = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        return File.Exists(p) ? p : null;
    }

    // ===================================================================================
    // 1. REVERSIBILITY - change a few values, change them back, expect identical bytes.
    // ===================================================================================

    [Fact]
    public void Player_ChangeSkillThenRestore_ReproducesOriginalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = PlayerSavePath();
        if (path is null) return;

        var original = File.ReadAllBytes(path);
        using var input = new MemoryStream(original);
        var data = PlayerSaveReader.ReadFromStream(input);

        // Capture a skill, mutate it, then put it back exactly as it was.
        var skill = data.Skills[2];
        PlayerSaveWriter.ApplySkills(data, new[]
        {
            skill with { Xp = skill.Xp + 4242f, XpMultiplier = skill.XpMultiplier + 0.25f },
        });
        PlayerSaveWriter.ApplySkills(data, new[] { skill });

        AssertReserializesTo(original, data.Raw, "player skill change+restore");
    }

    [Fact]
    public void Player_ChangeTraitsThenRestore_ReproducesOriginalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = PlayerSavePath();
        if (path is null) return;

        var original = File.ReadAllBytes(path);
        using var input = new MemoryStream(original);
        var data = PlayerSaveReader.ReadFromStream(input);

        var originalTraits = data.Traits.ToList();
        Assert.NotEmpty(originalTraits);

        // Add a trait, then restore the exact original list (same values, same order).
        PlayerSaveWriter.ApplyTraits(data, originalTraits.Append("Trait_FannyPack").ToList());
        PlayerSaveWriter.ApplyTraits(data, originalTraits);

        AssertReserializesTo(original, data.Raw, "player traits change+restore");
    }

    [Fact]
    public void World_ChangeFlagsThenRestore_ReproducesOriginalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = FacilitySavePath();
        if (path is null) return;

        var original = File.ReadAllBytes(path);
        using var input = new MemoryStream(original);
        var data = WorldSaveReader.ReadFromStream(input);

        var originalFlags = data.Flags.ToList();
        Assert.NotEmpty(originalFlags);

        // Append a flag, then restore the exact original list.
        Assert.True(WorldSaveWriter.ApplyFlags(data, originalFlags.Append("editor_reversibility_probe").ToList()));
        Assert.True(WorldSaveWriter.ApplyFlags(data, originalFlags));

        AssertReserializesTo(original, data.Raw, "world flags change+restore");
    }

    [Fact]
    public void World_ChangeDoorThenRestore_ReproducesOriginalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = FacilitySavePath();
        if (path is null) return;

        var original = File.ReadAllBytes(path);
        using var input = new MemoryStream(original);
        var data = WorldSaveReader.ReadFromStream(input);

        // IsDoorOpen is present for every security door (it drives lock state), so flipping it
        // and flipping it back is a pure in-place patch with no tag creation.
        var door = data.Doors.First(d => d.Kind == WorldDoorKind.Security && d.IsDoorOpen.HasValue);
        var flipped = door with { IsDoorOpen = !door.IsDoorOpen!.Value };
        WorldSaveWriter.ApplyDoors(data, new[] { flipped });
        WorldSaveWriter.ApplyDoors(data, new[] { door });

        AssertReserializesTo(original, data.Raw, "world door change+restore");
    }

    // ===================================================================================
    // 2. ISOLATION - one change, save, reload from disk, only that change differs.
    // ===================================================================================

    [Fact]
    public void Player_EditOneSkillXp_ChangesOnlyThatLeaf()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = PlayerSavePath();
        if (path is null) return;

        var temp = Path.Combine(Path.GetTempPath(), $"abf-iso-skill-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(path, temp);

            var data = PlayerSaveReader.ReadFromFile(temp);
            var skill = data.Skills[2];
            PlayerSaveWriter.ApplySkills(data, new[] { skill with { Xp = skill.Xp + 5000f } });
            PlayerSaveWriter.WriteToFile(data, temp);

            var diff = SaveComparer.CompareFiles(path, temp);
            DumpDiff(diff);

            // Exactly one leaf moved: skill index 2's XP. The multiplier was rewritten to the
            // same value (no diff) and nothing else in the save was touched.
            Assert.Equal(1, diff.ChangedCount);
            Assert.Equal(0, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            var leaf = diff.Differences.Single();
            Assert.Contains("Skills[2]", leaf.Path);
            Assert.Contains("CurrentSkillXP", leaf.Path);

            // And the value really landed on disk.
            var reread = PlayerSaveReader.ReadFromFile(temp);
            Assert.Equal(skill.Xp + 5000f, reread.Skills[2].Xp);
        }
        finally
        {
            CleanUp(temp);
        }
    }

    [Fact]
    public void World_AppendOneFlag_AddsOnlyThatLeaf()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = FacilitySavePath();
        if (path is null) return;

        var temp = Path.Combine(Path.GetTempPath(), $"abf-iso-flag-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(path, temp);

            var data = WorldSaveReader.ReadFromFile(temp);
            var originalCount = data.Flags.Count;
            const string sentinel = "editor_isolation_probe";
            Assert.True(WorldSaveWriter.ApplyFlags(data, data.Flags.Append(sentinel).ToList()));
            WorldSaveWriter.WriteToFile(data, temp);

            var diff = SaveComparer.CompareFiles(path, temp);
            DumpDiff(diff);

            // One leaf added (the new flag at the end of the array), nothing changed or removed.
            Assert.Equal(0, diff.ChangedCount);
            Assert.Equal(1, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            var leaf = diff.Differences.Single();
            Assert.Equal(SaveDiffKind.Added, leaf.Kind);
            Assert.Contains($"WorldFlags[{originalCount}]", leaf.Path);
            Assert.Equal(sentinel, leaf.Right);
        }
        finally
        {
            CleanUp(temp);
        }
    }

    [Fact]
    public void World_FlipOneDoor_ChangesOnlyThatLeaf()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = FacilitySavePath();
        if (path is null) return;

        var temp = Path.Combine(Path.GetTempPath(), $"abf-iso-door-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(path, temp);

            var data = WorldSaveReader.ReadFromFile(temp);
            var door = data.Doors.First(d => d.Kind == WorldDoorKind.Security && d.IsDoorOpen.HasValue);
            var flipped = !door.IsDoorOpen!.Value;
            WorldSaveWriter.ApplyDoors(data, new[] { door with { IsDoorOpen = flipped } });
            WorldSaveWriter.WriteToFile(data, temp);

            var diff = SaveComparer.CompareFiles(path, temp);
            DumpDiff(diff);

            // Only the one door's open flag moved.
            Assert.Equal(1, diff.ChangedCount);
            Assert.Equal(0, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            var leaf = diff.Differences.Single();
            Assert.Contains(door.Id, leaf.Path);
            Assert.Contains("IsDoorOpen", leaf.Path);

            var reread = WorldSaveReader.ReadFromFile(temp);
            Assert.Equal(flipped, reread.Doors.First(d => d.Id == door.Id).IsDoorOpen);
        }
        finally
        {
            CleanUp(temp);
        }
    }

    [Fact]
    public void World_EditOneContainerSlot_DiffConfinedToThatContainer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = FacilitySavePath();
        if (path is null) return;

        var temp = Path.Combine(Path.GetTempPath(), $"abf-iso-container-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(path, temp);

            var data = WorldSaveReader.ReadFromFile(temp);
            var target = data.Containers.First(c =>
                c.Inventories.Count > 0 && c.Inventories[0].Slots.Any(s => !s.IsEmpty));
            var slot = target.Inventories[0].Slots.First(s => !s.IsEmpty);
            var newCount = slot.Count + 11;

            var newSlots = target.Inventories[0].Slots
                .Select(s => s.Index == slot.Index ? s with { Count = newCount } : s)
                .ToList();
            var mutated = target with { Inventories = new[] { new WorldInventory(newSlots) } };
            WorldSaveWriter.ApplyContainers(data, new[] { mutated });
            WorldSaveWriter.WriteToFile(data, temp);

            var diff = SaveComparer.CompareFiles(path, temp);
            DumpDiff(diff);

            // ApplyContainers only touches the targeted container. Patching its inventory can
            // materialize delta-omitted sibling fields on the slots it rewrites, so we don't
            // demand a single leaf here - but EVERY difference must live inside this one
            // container's subtree (its DeployedObjectMap/CustomInventoryMap key).
            Assert.NotEmpty(diff.Differences);
            Assert.All(diff.Differences, d =>
                Assert.Contains(target.Id, d.Path));
            Assert.Contains(diff.Differences, d => d.Path.Contains("CurrentStack"));

            var reread = WorldSaveReader.ReadFromFile(temp);
            var rereadSlot = reread.Containers.First(c => c.Id == target.Id)
                .Inventories[0].Slots[slot.Index];
            Assert.Equal(newCount, rereadSlot.Count);
        }
        finally
        {
            CleanUp(temp);
        }
    }

    // ---------- helpers ----------

    private static void AssertReserializesTo(byte[] original, SaveGame save, string what)
    {
        using var ms = new MemoryStream();
        save.WriteTo(ms);
        var rewritten = ms.ToArray();

        if (!original.AsSpan().SequenceEqual(rewritten))
        {
            var offset = FirstDifference(original, rewritten);
            Assert.Fail(
                $"{what}: expected byte-identical output after change+restore, but they diverged. " +
                $"original={original.Length}B, rewritten={rewritten.Length}B, first diff at 0x{offset:X}");
        }
    }

    private void DumpDiff(SaveDiff diff)
    {
        _output.WriteLine($"diff: {diff.Summary}");
        foreach (var d in diff.Differences.Take(20))
        {
            _output.WriteLine($"  [{d.Kind}] {d.Path}: {d.Left ?? "(none)"} -> {d.Right ?? "(none)"}");
        }
    }

    private static long FirstDifference(byte[] a, byte[] b)
    {
        var min = Math.Min(a.Length, b.Length);
        for (var i = 0; i < min; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return min;
    }

    private static void CleanUp(string temp)
    {
        if (File.Exists(temp)) File.Delete(temp);
        var bak = temp + ".bak";
        if (File.Exists(bak)) File.Delete(bak);
    }
}
