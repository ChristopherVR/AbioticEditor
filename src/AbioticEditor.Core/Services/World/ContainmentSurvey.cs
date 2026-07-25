using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The world-wide picture of Leyak containment, assembled from two places that have to be read
/// together:
/// <list type="bullet">
///   <item>the metadata save's <c>LeyakContainmentIDs</c> map - creature row -> unit GUID, the
///     only record of <em>which</em> unit holds a creature (and, being keyed by creature, proof
///     that a creature can be in at most one unit at a time);</item>
///   <item>each region save's <c>DeployedObjectMap</c> - the units themselves, occupied or not.
///     Units are player-deployed only (no cooked level places one), so this is the complete
///     set.</item>
/// </list>
/// In every fixture world every unit sits in <c>WorldSave_Facility.sav</c>, but nothing stops a
/// player carrying one into another region, so the scan covers every sibling region save.
/// </summary>
/// <param name="Units">Every deployed containment unit found, occupied ones carrying the
/// creature the metadata save assigns them.</param>
/// <param name="OrphanedAssignments">Creature -> unit GUID entries whose unit was not found in
/// any region save (a unit destroyed outside the game's own release path, or a region save that
/// could not be read).</param>
/// <param name="UnreadableSaves">File names of sibling saves that failed to parse, so a caller
/// can say the survey may be incomplete instead of quietly under-reporting.</param>
public sealed record ContainmentSurvey(
    IReadOnlyList<WorldContainmentUnit> Units,
    IReadOnlyList<KeyValuePair<string, string>> OrphanedAssignments,
    IReadOnlyList<string> UnreadableSaves)
{
    /// <summary>Units with no creature assigned - the ones a creature can be moved into.</summary>
    public IReadOnlyList<WorldContainmentUnit> EmptyUnits => Units.Where(unit => !unit.IsOccupied).ToArray();

    /// <summary>Units currently holding a creature.</summary>
    public IReadOnlyList<WorldContainmentUnit> OccupiedUnits => Units.Where(unit => unit.IsOccupied).ToArray();

    /// <summary>Containable creature rows that are not in any unit right now.</summary>
    public IReadOnlyList<string> FreeCreatures => ContainmentCreatureCatalog.Containable
        .Select(entry => entry.Row)
        .Where(row => !Units.Any(unit => string.Equals(unit.Creature, row, StringComparison.OrdinalIgnoreCase))
                      && !OrphanedAssignments.Any(pair => string.Equals(pair.Key, row, StringComparison.OrdinalIgnoreCase)))
        .ToArray();
}

/// <summary>
/// Reads and writes the world-wide containment picture. Reading spans every region save next to
/// the metadata save; writing spans the metadata save (the creature -> unit map) plus whichever
/// region saves hold the units whose stored creature index changed.
/// </summary>
public static class ContainmentDirectory
{
    /// <summary>Region saves are every <c>WorldSave_*.sav</c> that is not the metadata save.</summary>
    private static IEnumerable<string> RegionSaves(string worldFolder)
        => Directory.EnumerateFiles(worldFolder, "WorldSave_*.sav")
            .Where(path => !string.Equals(Path.GetFileName(path), "WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase))
            // Facility first: it is where every unit in every fixture world lives, so callers
            // that stream results see the interesting save first.
            .OrderByDescending(path => string.Equals(Path.GetFileName(path), "WorldSave_Facility.sav", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Surveys the world folder holding <paramref name="metadataSavePath"/>. Reading every
    /// region save costs a few seconds on a large world, so callers should do this off the UI
    /// thread and cache the result.
    /// </summary>
    public static ContainmentSurvey Survey(string metadataSavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataSavePath);
        var folder = Path.GetDirectoryName(metadataSavePath);
        if (folder is null || !Directory.Exists(folder))
        {
            return new ContainmentSurvey([], [], []);
        }

        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var pair in WorldSaveReader.ReadLeyakContainments(WorldSaveReader.ReadFromFile(metadataSavePath).Raw))
            {
                assignments[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Diagnostics.EditorLog.Warn("Containment", $"Could not read {Path.GetFileName(metadataSavePath)}: {ex.Message}");
        }

        var units = new List<WorldContainmentUnit>();
        var unreadable = new List<string>();
        foreach (var regionPath in RegionSaves(folder))
        {
            try
            {
                var save = WorldSaveReader.ReadFromFile(regionPath).Raw;
                units.AddRange(WorldSaveReader.ReadContainmentUnits(save, Path.GetFileName(regionPath)));
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("Containment", $"Could not scan {Path.GetFileName(regionPath)}: {ex.Message}");
                unreadable.Add(Path.GetFileName(regionPath));
            }
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var joined = new List<WorldContainmentUnit>(units.Count);
        foreach (var unit in units)
        {
            var creature = assignments.FirstOrDefault(pair => string.Equals(pair.Value, unit.Id, StringComparison.OrdinalIgnoreCase)).Key;
            if (creature is not null) claimed.Add(creature);
            joined.Add(unit with { Creature = creature });
        }

        var orphans = assignments
            .Where(pair => !claimed.Contains(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        joined.Sort((a, b) =>
        {
            var byFile = string.Compare(a.RegionSaveFileName, b.RegionSaveFileName, StringComparison.OrdinalIgnoreCase);
            return byFile != 0 ? byFile : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });
        return new ContainmentSurvey(joined, orphans, unreadable);
    }

    /// <summary>What <see cref="SyncUnitRecords"/> did, so a caller can report it honestly.</summary>
    /// <param name="UnitsUpdated">Units whose stored creature index was rewritten.</param>
    /// <param name="UnitsSkipped">Units that could not be rewritten (unit missing from its save,
    /// or carrying no dynamic-property array to patch).</param>
    /// <param name="FilesWritten">Region save file names that were written (each with a .bak).</param>
    public sealed record SyncResult(int UnitsUpdated, IReadOnlyList<string> UnitsSkipped, IReadOnlyList<string> FilesWritten);

    /// <summary>
    /// Brings each unit's own record of what it holds (its <c>LeyakContainmentData</c> index)
    /// back in line with <paramref name="assignments"/> (creature row -> unit GUID), writing the
    /// affected region saves in place with the standard pre-write backup. The metadata save is
    /// not touched here - its map is written through the normal session save path.
    ///
    /// Only units whose stored index actually disagrees are rewritten, so a no-op sync writes
    /// nothing at all.
    /// </summary>
    public static SyncResult SyncUnitRecords(string metadataSavePath, IReadOnlyDictionary<string, string> assignments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataSavePath);
        ArgumentNullException.ThrowIfNull(assignments);

        var folder = Path.GetDirectoryName(metadataSavePath);
        if (folder is null || !Directory.Exists(folder)) return new SyncResult(0, [], []);

        // unit GUID -> the index that unit should store.
        var wanted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (creature, unitId) in assignments)
        {
            var index = ContainmentCreatureCatalog.IndexOf(creature);
            if (index >= 0 && !string.IsNullOrWhiteSpace(unitId)) wanted[unitId] = index;
        }
        if (wanted.Count == 0) return new SyncResult(0, [], []);

        var updated = 0;
        var skipped = new List<string>();
        var written = new List<string>();
        var remaining = new HashSet<string>(wanted.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var regionPath in RegionSaves(folder))
        {
            if (remaining.Count == 0) break;
            WorldSaveData data;
            try
            {
                data = WorldSaveReader.ReadFromFile(regionPath);
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("Containment", $"Could not open {Path.GetFileName(regionPath)}: {ex.Message}");
                continue;
            }

            var dirty = false;
            foreach (var unit in WorldSaveReader.ReadContainmentUnits(data.Raw, Path.GetFileName(regionPath)))
            {
                if (!wanted.TryGetValue(unit.Id, out var index)) continue;
                remaining.Remove(unit.Id);
                if (unit.StoredCreatureIndex == index) continue;

                if (WorldSaveWriter.SetContainmentUnitCreatureIndex(data, unit.Id, index))
                {
                    updated++;
                    dirty = true;
                }
                else
                {
                    skipped.Add(unit.Id);
                }
            }

            if (!dirty) continue;
            WorldSaveWriter.WriteToFile(data, regionPath);
            written.Add(Path.GetFileName(regionPath));
        }

        return new SyncResult(updated, skipped, written);
    }
}
