using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The staged editing flow behind the CONTAINMENT tab: list every deployed unit, move a creature
/// between units, swap the two, catch a creature that is roaming free, and have SAVE write both
/// the metadata save's creature -> unit map and the region saves' own copy of what each unit holds.
/// </summary>
public sealed class ContainmentSessionTests
{
    /// <summary>
    /// A throwaway copy of a world folder. These tests write real saves, so they must never
    /// touch the checked-in fixtures.
    /// </summary>
    private sealed class TempWorld : IDisposable
    {
        private TempWorld(string folder)
        {
            Folder = folder;
            MetadataPath = Path.Combine(folder, "WorldSave_MetaData.sav");
        }

        public string Folder { get; }
        public string MetadataPath { get; }

        public static TempWorld? From(string? sourceFolder)
        {
            if (sourceFolder is null || !File.Exists(Path.Combine(sourceFolder, "WorldSave_MetaData.sav"))) return null;
            var folder = Path.Combine(Path.GetTempPath(), "abiotic-containment-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "WorldSave_*.sav"))
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
            return new TempWorld(folder);
        }

        public WorldSaveSession OpenMetadata()
            => new(WorldSaveReader.ReadFromFile(MetadataPath), MetadataPath);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task Session_lists_every_deployed_unit_not_just_the_occupied_ones()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        Assert.False(session.ContainmentUnitsLoaded);
        await session.LoadContainmentUnitsAsync();

        Assert.True(session.ContainmentUnitsLoaded);
        Assert.NotEmpty(session.ContainmentUnits);
        // Every unit resolves to the region save it stands in, so the tab can say where it is.
        Assert.All(session.ContainmentUnits, unit => Assert.EndsWith(".sav", unit.RegionSaveFileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(session.Containments.Count, session.ContainmentUnits.Count(unit => session.CreatureInUnit(unit.Id) is not null));
    }

    [Fact]
    public async Task Swapping_the_two_creatures_exchanges_their_units_and_survives_a_save()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();

        var leyakUnit = session.Containments.Single(pair => pair.Key.Equals("Leyak", StringComparison.OrdinalIgnoreCase)).Value;
        var krasueUnit = session.Containments.Single(pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.NotEqual(leyakUnit, krasueUnit);
        Assert.False(session.IsDirty);

        session.SwapContainmentUnits(leyakUnit, krasueUnit);

        Assert.True(session.IsDirty);
        Assert.Equal("Krasue", session.CreatureInUnit(leyakUnit), ignoreCase: true);
        Assert.Equal("Leyak", session.CreatureInUnit(krasueUnit), ignoreCase: true);

        await session.SaveAsync();
        Assert.False(session.IsDirty);

        // Both halves are on disk: the map in the metadata save...
        var reloaded = world.OpenMetadata();
        Assert.Equal(krasueUnit, reloaded.Containments.Single(pair => pair.Key.Equals("Leyak", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Equal(leyakUnit, reloaded.Containments.Single(pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase)).Value);

        // ...and each unit's own record of what it holds, in the region save.
        var survey = ContainmentDirectory.Survey(world.MetadataPath);
        Assert.All(survey.OccupiedUnits, unit => Assert.False(unit.StoredCreatureDisagrees));
        Assert.Equal("Krasue", survey.Units.Single(unit => unit.Id == leyakUnit).Creature, ignoreCase: true);
        Assert.Equal("Leyak", survey.Units.Single(unit => unit.Id == krasueUnit).Creature, ignoreCase: true);
        Assert.True(File.Exists(Path.Combine(world.Folder, "WorldSave_MetaData.sav.bak")));
        Assert.True(File.Exists(Path.Combine(world.Folder, "WorldSave_Facility.sav.bak")));
    }

    [Fact]
    public async Task Putting_a_creature_into_an_occupied_unit_turns_the_previous_occupant_loose()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();
        var leyakUnit = session.Containments.Single(pair => pair.Key.Equals("Leyak", StringComparison.OrdinalIgnoreCase)).Value;

        session.SetContainmentUnitOccupant(leyakUnit, "Krasue");

        // The Krasue moved in, so the Leyak has nowhere left and its old cell now holds the Krasue.
        Assert.Equal("Krasue", session.CreatureInUnit(leyakUnit), ignoreCase: true);
        Assert.DoesNotContain(session.Containments, pair => pair.Key.Equals("Leyak", StringComparison.OrdinalIgnoreCase));
        // The unit the Krasue came from is now empty.
        Assert.Single(session.Containments);
    }

    [Fact]
    public async Task Emptying_a_unit_releases_its_occupant()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();
        var krasueUnit = session.Containments.Single(pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase)).Value;

        session.SetContainmentUnitOccupant(krasueUnit, null);
        Assert.Null(session.CreatureInUnit(krasueUnit));
        Assert.DoesNotContain(session.Containments, pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase));

        await session.SaveAsync();
        var reloaded = world.OpenMetadata();
        Assert.DoesNotContain(reloaded.Containments, pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase));
        Assert.Single(reloaded.Containments);
    }

    [Fact]
    public async Task Catching_a_free_creature_in_an_empty_unit_writes_both_the_map_and_the_unit()
    {
        // The legacy world holds the Leyak only, so the Krasue is roaming and the editor should
        // be able to put it into the unit the Leyak is freed from.
        using var world = TempWorld.From(Fixtures.CascadeDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();
        var unit = session.ContainmentUnits.Single();
        Assert.Equal("Leyak", session.CreatureInUnit(unit.Id), ignoreCase: true);

        session.SetContainmentUnitOccupant(unit.Id, "Krasue");
        await session.SaveAsync();

        var survey = ContainmentDirectory.Survey(world.MetadataPath);
        var caught = survey.Units.Single();
        Assert.Equal("Krasue", caught.Creature, ignoreCase: true);
        // The unit's own record was rewritten to the Krasue's index, not left on the Leyak's.
        Assert.Equal(ContainmentCreatureCatalog.IndexOf("Krasue"), caught.StoredCreatureIndex);
        Assert.False(caught.StoredCreatureDisagrees);
        Assert.Contains("Leyak", survey.FreeCreatures);
    }

    [Fact]
    public async Task Revert_undoes_a_staged_move()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();
        var before = session.Containments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var leyakUnit = before["Leyak"];

        session.SetContainmentUnitOccupant(leyakUnit, "Krasue");
        Assert.True(session.IsDirty);

        session.Revert();

        Assert.False(session.IsDirty);
        Assert.Equal(before["Leyak"], session.Containments.Single(pair => pair.Key.Equals("Leyak", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Equal(before["Krasue"], session.Containments.Single(pair => pair.Key.Equals("Krasue", StringComparison.OrdinalIgnoreCase)).Value);
    }

    [Fact]
    public async Task Saving_with_no_containment_change_leaves_the_region_saves_alone()
    {
        using var world = TempWorld.From(Fixtures.ServerWorldsDir);
        if (world is null) return;

        var session = world.OpenMetadata();
        await session.LoadContainmentUnitsAsync();

        // A play-time edit is dirty but has nothing to do with containment.
        session.SetMinutesPassed((session.MinutesPassed ?? 0) + 5);
        Assert.True(session.IsDirty);
        await session.SaveAsync();

        Assert.False(File.Exists(Path.Combine(world.Folder, "WorldSave_Facility.sav.bak")));
    }

    // ---------- browser host (no disk to walk) ----------
    //
    // The browser's save "paths" are opaque handle identifiers, not real file-system paths
    // (ISaveFileSystem.HasLocalPaths is false there), so ContainmentDirectory.Survey's disk walk
    // finds nothing. These simulate that host with an in-memory ISaveFileSystem instead of the
    // real DesktopSaveFileSystem every other test in this file uses.

    /// <summary>A minimal in-memory stand-in for the browser's file system, with
    /// <see cref="HasLocalPaths"/> false so <see cref="WorldSaveSession"/> takes the same code
    /// path it does in a real browser tab.</summary>
    private sealed class FakeBrowserFileSystem(IReadOnlyDictionary<string, byte[]> files) : ISaveFileSystem
    {
        public bool HasLocalPaths => false;
        public bool CanWrite => true;

        public Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SaveFileEntry>>(
                files.Select(pair => new SaveFileEntry(pair.Key, pair.Key, Path.GetFileName(pair.Key), pair.Value.LongLength)).ToArray());

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
            => files.TryGetValue(path, out var bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException($"'{path}' is not one of this fake's files.", path);

        public Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(files.ContainsKey(path) ? files[path].Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);

        public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
            => ReadAllBytesAsync(path, cancellationToken);

        public Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
            => ReadAllBytesAsync(path, cancellationToken);

        public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double is read-only.");
    }

    [Fact]
    public async Task Browser_host_reads_sibling_region_saves_through_the_file_system_seam()
    {
        var worldFolder = Fixtures.ServerWorldsDir;
        var metadataDiskPath = worldFolder is null ? null : Path.Combine(worldFolder, "WorldSave_MetaData.sav");
        var facilityDiskPath = worldFolder is null ? null : Path.Combine(worldFolder, "WorldSave_Facility.sav");
        if (metadataDiskPath is null || facilityDiskPath is null
            || !File.Exists(metadataDiskPath) || !File.Exists(facilityDiskPath))
        {
            return;
        }

        // Browser-shaped identifiers ("folder/Name.sav"), never real paths - proving the survey
        // does not secretly lean on System.IO to find them.
        const string browserMetadataPath = "cascade-world/WorldSave_MetaData.sav";
        const string browserFacilityPath = "cascade-world/WorldSave_Facility.sav";
        var files = new FakeBrowserFileSystem(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [browserMetadataPath] = File.ReadAllBytes(metadataDiskPath),
            [browserFacilityPath] = File.ReadAllBytes(facilityDiskPath),
        });

        var data = WorldSaveReader.ReadFromFile(metadataDiskPath);
        var session = new WorldSaveSession(data, browserMetadataPath, files, [browserFacilityPath]);

        Assert.False(session.ContainmentUnitsLoaded);
        await session.LoadContainmentUnitsAsync();

        Assert.True(session.ContainmentUnitsLoaded);
        Assert.False(session.ContainmentScanUnavailable);
        Assert.Empty(session.ContainmentScanFailures);
        Assert.NotEmpty(session.ContainmentUnits);
        Assert.All(session.ContainmentUnits,
            unit => Assert.Equal("WorldSave_Facility.sav", unit.RegionSaveFileName, ignoreCase: true));

        // Matches what the desktop's disk-walking survey finds for the very same world.
        var diskSurvey = ContainmentDirectory.Survey(metadataDiskPath);
        Assert.Equal(diskSurvey.Units.Count, session.ContainmentUnits.Count);
        Assert.Equal(
            diskSurvey.Units.Select(unit => unit.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
            session.ContainmentUnits.Select(unit => unit.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Browser_host_with_no_known_sibling_saves_reports_the_scan_as_unavailable_not_empty()
    {
        var worldFolder = Fixtures.ServerWorldsDir;
        var metadataDiskPath = worldFolder is null ? null : Path.Combine(worldFolder, "WorldSave_MetaData.sav");
        if (metadataDiskPath is null || !File.Exists(metadataDiskPath)) return;

        const string browserMetadataPath = "cascade-world/WorldSave_MetaData.sav";
        var files = new FakeBrowserFileSystem(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [browserMetadataPath] = File.ReadAllBytes(metadataDiskPath),
        });

        var data = WorldSaveReader.ReadFromFile(metadataDiskPath);
        // No sibling paths handed in - nothing told this session where the region saves are.
        var session = new WorldSaveSession(data, browserMetadataPath, files);

        await session.LoadContainmentUnitsAsync();

        Assert.True(session.ContainmentUnitsLoaded);
        Assert.True(session.ContainmentScanUnavailable);
        Assert.Empty(session.ContainmentUnits);
    }
}
