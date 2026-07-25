using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// The containment schema, asserted against the real fixture worlds.
///
/// The facts under test, all established by the research probes
/// (<c>tests/AbioticEditor.Probes/ContainmentUnitProbe.cs</c> and
/// <c>ContainmentBlueprintProbe.cs</c>):
/// <list type="bullet">
///   <item>A containment unit is an ordinary deployable of class
///     <c>Deployed_LeyakContainment_C</c> in a <em>region</em> save's <c>DeployedObjectMap</c>,
///     keyed by GUID. Occupied and empty units look identical apart from the link below.</item>
///   <item>The metadata save's <c>LeyakContainmentIDs</c> (Map&lt;Name,Str&gt;) is the only
///     record of which unit holds which creature. Keyed by creature, so a creature can be in at
///     most one unit - which makes "swap" a value swap and "add" a key set.</item>
///   <item>The unit keeps its own copy of what it holds as the index into the blueprint's
///     two-entry <c>LeyakContainmentData</c> array, in <c>EDynamicProperty::Generic3</c>
///     (0 = Leyak, 1 = Krasue), and its stability in <c>Generic1</c>.</item>
/// </list>
/// </summary>
public class ContainmentUnitTests
{
    private static IEnumerable<string> WorldFolders()
    {
        if (Fixtures.ServerWorldsDir is not null) yield return Fixtures.ServerWorldsDir;
        if (Fixtures.CascadeDir is not null) yield return Fixtures.CascadeDir;
    }

    private static string? MetadataPath(string folder)
    {
        var path = Path.Combine(folder, "WorldSave_MetaData.sav");
        return File.Exists(path) ? path : null;
    }

    // ---------- catalog ----------

    [Fact]
    public void Catalog_ListsExactlyTheTwoContainableCreatures_InBlueprintOrder()
    {
        // Deployed_LeyakContainment's LeyakContainmentData array has exactly two entries:
        // [0] DT_NPCList row "Leyak" (fed food_greyeb), [1] row "Krasue" (fed food_milk).
        Assert.Equal(2, ContainmentCreatureCatalog.Containable.Count);
        Assert.Equal("Leyak", ContainmentCreatureCatalog.Containable[0].Row);
        Assert.Equal(0, ContainmentCreatureCatalog.Containable[0].Index);
        Assert.Equal("Krasue", ContainmentCreatureCatalog.Containable[1].Row);
        Assert.Equal(1, ContainmentCreatureCatalog.Containable[1].Index);

        Assert.Equal(0, ContainmentCreatureCatalog.IndexOf("leyak"));
        Assert.Equal(1, ContainmentCreatureCatalog.IndexOf("KRASUE"));
        Assert.Equal(-1, ContainmentCreatureCatalog.IndexOf("Wraith"));
        Assert.Equal("Krasue", ContainmentCreatureCatalog.RowAtIndex(1));
        Assert.Null(ContainmentCreatureCatalog.RowAtIndex(7));

        Assert.Equal("food_greyeb", ContainmentCreatureCatalog.StabilityItem("Leyak"));
        Assert.Equal("food_milk", ContainmentCreatureCatalog.StabilityItem("Krasue"));
        Assert.Null(ContainmentCreatureCatalog.StabilityItem("Wraith"));
    }

    [Fact]
    public void Catalog_RecognisesTheUnitDeployableClass()
    {
        Assert.True(ContainmentCreatureCatalog.IsUnitClass(
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment.Deployed_LeyakContainment_C"));
        Assert.False(ContainmentCreatureCatalog.IsUnitClass(
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_Bobblehead_Leyak.Deployed_Bobblehead_Leyak_C"));
        Assert.False(ContainmentCreatureCatalog.IsUnitClass(null));
    }

    // ---------- reading ----------

    [Fact]
    public void Reader_FindsUnitsInTheRegionSave_NotTheMetadataSave()
    {
        var folder = WorldFolders().FirstOrDefault();
        if (folder is null) return; // no fixture

        var facility = Path.Combine(folder, "WorldSave_Facility.sav");
        var metadata = MetadataPath(folder);
        if (!File.Exists(facility) || metadata is null) return;

        var units = WorldSaveReader.ReadContainmentUnits(WorldSaveReader.ReadFromFile(facility).Raw, "WorldSave_Facility.sav");
        Assert.NotEmpty(units);
        Assert.All(units, unit => Assert.Equal("WorldSave_Facility.sav", unit.RegionSaveFileName));
        Assert.All(units, unit => Assert.False(string.IsNullOrEmpty(unit.Id)));
        // The units carry a real position, which is what lets the editor say where each one is.
        Assert.Contains(units, unit => unit.X != 0 || unit.Y != 0 || unit.Z != 0);

        // The metadata save holds the assignment map but no units of its own.
        var metaUnits = WorldSaveReader.ReadContainmentUnits(WorldSaveReader.ReadFromFile(metadata).Raw);
        Assert.Empty(metaUnits);
    }

    [Fact]
    public void Reader_UnitStoredIndexMatchesTheCreatureTheMetadataSaveAssigns()
    {
        var checkedAny = false;
        foreach (var folder in WorldFolders())
        {
            var metadata = MetadataPath(folder);
            if (metadata is null) continue;

            var survey = ContainmentDirectory.Survey(metadata);
            foreach (var unit in survey.OccupiedUnits)
            {
                checkedAny = true;
                Assert.NotNull(unit.StoredCreatureIndex);
                Assert.Equal(ContainmentCreatureCatalog.IndexOf(unit.Creature), unit.StoredCreatureIndex);
                Assert.False(unit.StoredCreatureDisagrees);
                // Stability is the 0..100 slot, never a sentinel.
                Assert.NotNull(unit.Stability);
                Assert.InRange(unit.Stability!.Value, 0, ContainmentCreatureCatalog.MaxStability);
            }
        }
        if (!checkedAny) return; // fixtures absent
    }

    [Fact]
    public void Survey_JoinsUnitsToAssignments_AndSpansTheWholeWorldFolder()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var survey = ContainmentDirectory.Survey(metadata);
        Assert.Empty(survey.UnreadableSaves);
        Assert.NotEmpty(survey.Units);

        // The dedicated-server fixture holds a Leyak and a Krasue, both in the Facility save.
        Assert.Equal(2, survey.OccupiedUnits.Count);
        Assert.Contains(survey.OccupiedUnits, unit => string.Equals(unit.Creature, "Leyak", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(survey.OccupiedUnits, unit => string.Equals(unit.Creature, "Krasue", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(survey.OrphanedAssignments);
        // Both creatures are held, so neither is free to add.
        Assert.Empty(survey.FreeCreatures);
    }

    [Fact]
    public void Survey_LegacyWorldHoldsOnlyTheLeyak_SoTheKrasueIsFree()
    {
        var folder = Fixtures.CascadeDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var survey = ContainmentDirectory.Survey(metadata);
        Assert.Single(survey.OccupiedUnits);
        Assert.Equal("Leyak", survey.OccupiedUnits[0].Creature, ignoreCase: true);
        Assert.Contains("Krasue", survey.FreeCreatures);
    }

    // ---------- writing: the metadata map ----------

    [Fact]
    public void SwappingTwoCreatures_ExchangesTheirUnits_AndRoundTrips()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var data = WorldSaveReader.ReadFromFile(metadata);
        var before = WorldSaveReader.ReadLeyakContainments(data.Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, before.Count);

        Assert.True(WorldSaveWriter.SetLeyakContainment(data, "Leyak", before["Krasue"]));
        Assert.True(WorldSaveWriter.SetLeyakContainment(data, "Krasue", before["Leyak"]));

        var after = ReadBackContainments(data);
        Assert.Equal(before["Krasue"], after["Leyak"]);
        Assert.Equal(before["Leyak"], after["Krasue"]);
        // A swap must not grow or shrink the map.
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public void AddingAFreeCreature_CreatesTheEntry_AndRoundTrips()
    {
        var folder = Fixtures.CascadeDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var data = WorldSaveReader.ReadFromFile(metadata);
        var before = WorldSaveReader.ReadLeyakContainments(data.Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Krasue", before.Keys, StringComparer.OrdinalIgnoreCase);

        const string unitId = "EF4165AF41877C1CB4D5BA988FCEB568";
        Assert.True(WorldSaveWriter.SetLeyakContainment(data, "Krasue", unitId));

        var after = ReadBackContainments(data);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(unitId, after["Krasue"]);
        // The pre-existing entry is untouched.
        Assert.Equal(before["Leyak"], after["Leyak"]);
    }

    [Fact]
    public void AddingToAWorldThatNeverContainedAnything_CreatesTheWholeMap()
    {
        // The map is delta-serialized away entirely on a world where nothing was ever caught,
        // so "put the Leyak in a unit" has to be able to mint the property from scratch.
        var metadata = FindMetadataWithoutContainmentMap();
        if (metadata is null) return;

        var data = WorldSaveReader.ReadFromFile(metadata);
        Assert.Null(data.Raw.Properties.FindByPrefix("LeyakContainmentIDs"));

        Assert.True(WorldSaveWriter.SetLeyakContainment(data, "Leyak", "0123456789ABCDEF0123456789ABCDEF"));

        var after = ReadBackContainments(data);
        Assert.Single(after);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", after["Leyak"]);
    }

    [Fact]
    public void ReleasingThenReAddingACreature_LeavesTheSameEntry()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var data = WorldSaveReader.ReadFromFile(metadata);
        var original = WorldSaveReader.ReadLeyakContainments(data.Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        Assert.True(WorldSaveWriter.RemoveLeyakContainment(data, "Krasue"));
        Assert.True(WorldSaveWriter.SetLeyakContainment(data, "Krasue", original["Krasue"]));

        var after = ReadBackContainments(data);
        Assert.Equal(original.Count, after.Count);
        Assert.Equal(original["Krasue"], after["Krasue"]);
        Assert.Equal(original["Leyak"], after["Leyak"]);
    }

    // ---------- writing: the unit's own record ----------

    [Fact]
    public void SettingAUnitsStoredCreature_PatchesGeneric3_AndRoundTrips()
    {
        var folder = Fixtures.ServerWorldsDir;
        var facility = folder is null ? null : Path.Combine(folder, "WorldSave_Facility.sav");
        if (facility is null || !File.Exists(facility)) return;

        var data = WorldSaveReader.ReadFromFile(facility);
        var units = WorldSaveReader.ReadContainmentUnits(data.Raw, "WorldSave_Facility.sav");
        var leyakUnit = units.FirstOrDefault(unit => unit.StoredCreatureIndex == 0);
        Assert.NotNull(leyakUnit);

        Assert.True(WorldSaveWriter.SetContainmentUnitCreatureIndex(data, leyakUnit!.Id, 1, stability: 55));

        var rewritten = ReadBack(data);
        var after = WorldSaveReader.ReadContainmentUnits(rewritten, "WorldSave_Facility.sav")
            .Single(unit => unit.Id == leyakUnit.Id);
        Assert.Equal(1, after.StoredCreatureIndex);
        Assert.Equal("Krasue", after.StoredCreature);
        Assert.Equal(55, after.Stability);
        // The unit did not move.
        Assert.Equal(leyakUnit.X, after.X, 3);
        Assert.Equal(leyakUnit.Y, after.Y, 3);
    }

    [Fact]
    public void SettingStoredCreature_RejectsANonContainmentDeployable()
    {
        var folder = Fixtures.ServerWorldsDir;
        var facility = folder is null ? null : Path.Combine(folder, "WorldSave_Facility.sav");
        if (facility is null || !File.Exists(facility)) return;

        var data = WorldSaveReader.ReadFromFile(facility);
        var unitIds = WorldSaveReader.ReadContainmentUnits(data.Raw).Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        var other = data.Deployables.FirstOrDefault(deployable => !unitIds.Contains(deployable.Id));
        Assert.NotNull(other);

        Assert.False(WorldSaveWriter.SetContainmentUnitCreatureIndex(data, other!.Id, 1));
        Assert.False(WorldSaveWriter.SetContainmentUnitCreatureIndex(data, "not-a-guid", 1));
    }

    // ---------- byte-exactness ----------

    [Fact]
    public void UntouchedMetadataSave_RewritesByteForByte()
    {
        foreach (var folder in WorldFolders())
        {
            var metadata = MetadataPath(folder);
            if (metadata is null) continue;

            var original = File.ReadAllBytes(metadata);
            var data = WorldSaveReader.ReadFromFile(metadata);
            using var buffer = new MemoryStream();
            data.Raw.WriteTo(buffer);
            Assert.True(original.AsSpan().SequenceEqual(buffer.ToArray()),
                $"{Path.GetFileName(metadata)} did not round-trip byte-for-byte");
        }
    }

    [Fact]
    public void SwappingContainments_ChangesOnlyTheMap_LeavingEverythingElseIntact()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        var baseline = WorldSaveReader.ReadFromFile(metadata);
        var baselineFlags = baseline.Flags.ToArray();
        var baselineStory = baseline.StoryProgressionRow;

        var data = WorldSaveReader.ReadFromFile(metadata);
        var before = WorldSaveReader.ReadLeyakContainments(data.Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        WorldSaveWriter.SetLeyakContainment(data, "Leyak", before["Krasue"]);
        WorldSaveWriter.SetLeyakContainment(data, "Krasue", before["Leyak"]);

        using var buffer = new MemoryStream();
        data.Raw.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(buffer);

        Assert.Equal(baselineFlags, reloaded.Flags.ToArray());
        Assert.Equal(baselineStory, reloaded.StoryProgressionRow);
        // A pure value swap keeps the file the same size: both GUIDs are 32 characters.
        Assert.Equal(new FileInfo(metadata).Length, buffer.Length);
    }

    // ---------- cross-file sync ----------

    [Fact]
    public void SyncUnitRecords_IsANoOpWhenEveryUnitAlreadyAgrees()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        using var world = new TempWorldCopy(folder!);
        var assignments = WorldSaveReader.ReadLeyakContainments(WorldSaveReader.ReadFromFile(world.MetadataPath).Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var result = ContainmentDirectory.SyncUnitRecords(world.MetadataPath, assignments);

        Assert.Equal(0, result.UnitsUpdated);
        Assert.Empty(result.FilesWritten);
        Assert.Empty(result.UnitsSkipped);
        Assert.False(File.Exists(Path.Combine(world.Folder, "WorldSave_Facility.sav.bak")));
    }

    [Fact]
    public void SyncUnitRecords_RewritesTheRegionSaveWhenASwapMakesUnitsDisagree()
    {
        var folder = Fixtures.ServerWorldsDir;
        var metadata = folder is null ? null : MetadataPath(folder);
        if (metadata is null) return;

        using var world = new TempWorldCopy(folder!);
        var before = ContainmentDirectory.Survey(world.MetadataPath);
        var leyakUnit = before.OccupiedUnits.Single(unit => string.Equals(unit.Creature, "Leyak", StringComparison.OrdinalIgnoreCase));
        var krasueUnit = before.OccupiedUnits.Single(unit => string.Equals(unit.Creature, "Krasue", StringComparison.OrdinalIgnoreCase));

        // Swap the metadata map, exactly as the editor does.
        var meta = WorldSaveReader.ReadFromFile(world.MetadataPath);
        Assert.True(WorldSaveWriter.SetLeyakContainment(meta, "Leyak", krasueUnit.Id));
        Assert.True(WorldSaveWriter.SetLeyakContainment(meta, "Krasue", leyakUnit.Id));
        WorldSaveWriter.WriteToFile(meta, world.MetadataPath);

        // Now the units disagree with the map, and the sync repairs both.
        var stale = ContainmentDirectory.Survey(world.MetadataPath);
        Assert.Equal(2, stale.OccupiedUnits.Count(unit => unit.StoredCreatureDisagrees));

        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Leyak"] = krasueUnit.Id,
            ["Krasue"] = leyakUnit.Id,
        };
        var result = ContainmentDirectory.SyncUnitRecords(world.MetadataPath, assignments);

        Assert.Equal(2, result.UnitsUpdated);
        Assert.Empty(result.UnitsSkipped);
        Assert.Equal(["WorldSave_Facility.sav"], result.FilesWritten);
        Assert.True(File.Exists(Path.Combine(world.Folder, "WorldSave_Facility.sav.bak")));

        var repaired = ContainmentDirectory.Survey(world.MetadataPath);
        Assert.All(repaired.OccupiedUnits, unit => Assert.False(unit.StoredCreatureDisagrees));
        Assert.Equal("Krasue", repaired.Units.Single(unit => unit.Id == leyakUnit.Id).Creature, ignoreCase: true);
        Assert.Equal("Leyak", repaired.Units.Single(unit => unit.Id == krasueUnit.Id).Creature, ignoreCase: true);
    }

    // ---------- helpers ----------

    private static Dictionary<string, string> ReadBackContainments(WorldSaveData data)
        => WorldSaveReader.ReadLeyakContainments(ReadBack(data))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Serializes the mutated tree and parses it back, so every assertion is on
    /// what actually landed in the file rather than on the in-memory object graph.</summary>
    private static SaveGame ReadBack(WorldSaveData data)
    {
        using var buffer = new MemoryStream();
        data.Raw.WriteTo(buffer);
        buffer.Position = 0;
        return SaveGame.LoadFrom(buffer);
    }

    private static string? FindMetadataWithoutContainmentMap()
    {
        if (Fixtures.ClientSavedDir is null) return null;
        foreach (var path in Directory.EnumerateFiles(Fixtures.ClientSavedDir, "WorldSave_MetaData.sav", SearchOption.AllDirectories))
        {
            try
            {
                if (WorldSaveReader.ReadFromFile(path).Raw.Properties.FindByPrefix("LeyakContainmentIDs") is null) return path;
            }
            catch
            {
                // Unreadable fixture; keep looking.
            }
        }
        return null;
    }

    /// <summary>
    /// A throwaway copy of a world folder. The sync tests write real files (and their .bak
    /// siblings), so they must never touch the checked-in fixtures. Only the saves the sync
    /// path reads are copied, to keep the copy cheap.
    /// </summary>
    private sealed class TempWorldCopy : IDisposable
    {
        public TempWorldCopy(string sourceFolder)
        {
            Folder = Path.Combine(Path.GetTempPath(), "abiotic-containment-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "WorldSave_*.sav"))
            {
                File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)));
            }
            MetadataPath = Path.Combine(Folder, "WorldSave_MetaData.sav");
        }

        public string Folder { get; }
        public string MetadataPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of a temp folder.
            }
        }
    }
}
