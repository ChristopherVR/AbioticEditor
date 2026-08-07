namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Cross-file helper: when the metadata save's story chapter is set, the matching
/// trigger flags live in the sibling <c>WorldSave_Facility.sav</c> (the main level).
/// This adds every missing trigger flag up to a chapter - additive only, with the
/// standard pre-write backup.
/// </summary>
/// <remarks>
/// Each operation comes in two halves. The <c>Plan*</c> methods take an already-read save and
/// work out the new flag list, touching no files; the methods named after them read the sibling
/// save, apply the plan and write it back. The split exists because the browser build has no
/// local file system to walk - it hands the bytes in itself - and because it makes the flag rules
/// testable without a save on disk.
/// </remarks>
public static class StoryFlagSync
{
    /// <summary>The result of working out a flag change, before anything is written.</summary>
    /// <param name="Flags">The full flag list to write, or null when there is nothing to do.</param>
    /// <param name="Count">How many flags were added or removed.</param>
    /// <param name="Message">Human-readable outcome, shown to the player either way.</param>
    public readonly record struct FlagPlan(IReadOnlyList<string>? Flags, int Count, string Message);

    /// <summary>The file name every operation here reads and writes.</summary>
    public const string FacilitySaveName = "WorldSave_Facility.sav";

    /// <summary>
    /// Works out which of <paramref name="flagsToAdd"/> are missing from an already-read Facility
    /// save. Additive only.
    /// </summary>
    public static FlagPlan PlanAddFlags(WorldSaveData facility, IReadOnlyCollection<string> flagsToAdd)
    {
        var flags = facility.Flags.ToList();
        var have = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var flag in flagsToAdd)
        {
            if (string.IsNullOrWhiteSpace(flag) || have.Contains(flag)) continue;
            flags.Add(flag);
            have.Add(flag);
            added++;
        }

        return added == 0
            ? new FlagPlan(null, 0, "All of those flags are already set in WorldSave_Facility.sav - nothing to do.")
            : new FlagPlan(flags, added, $"Added {added} world flag(s) to WorldSave_Facility.sav (backup kept).");
    }

    /// <summary>
    /// Works out the trigger flags missing from an already-read Facility save for every chapter up
    /// to and including <paramref name="chapterRow"/>.
    /// </summary>
    public static FlagPlan PlanSyncToChapter(WorldSaveData facility, string chapterRow)
    {
        var targetIndex = StoryProgressionCatalog.IndexOf(chapterRow);
        if (targetIndex < 0) return new FlagPlan(null, 0, $"Unknown chapter '{chapterRow}'.");

        var flags = facility.Flags.ToList();
        var have = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        for (var i = 0; i <= targetIndex; i++)
        {
            var flag = StoryProgressionCatalog.Chapters[i].TriggerFlag;
            if (flag is null || have.Contains(flag)) continue;
            flags.Add(flag);
            added++;
        }

        return added == 0
            ? new FlagPlan(null, 0, "Facility flags already match that chapter - nothing to do.")
            : new FlagPlan(flags, added, $"Added {added} story flag(s) to WorldSave_Facility.sav (backup kept).");
    }

    /// <summary>
    /// The revert counterpart of <see cref="PlanSyncToChapter"/>: works out which flags of every
    /// chapter AFTER <paramref name="chapterRow"/> to remove from an already-read Facility save,
    /// plus every granular flag <see cref="FlagGate.DependentsOf"/> finds built on top of them (per
    /// <see cref="QuestFlagDependencies"/>) AND every flag <see cref="FlagGate.FlagsPastChapter"/>
    /// finds whose region simply opens later (any-order steps within a region, e.g. the Dams pumps
    /// or Hydroplant survivors, that the curated graph deliberately doesn't order) - covering the
    /// case where a region was reached out of sequence (a tram/teleporter shortcut) rather than
    /// through the normal chapter progression. Flags with no story gate at all (side content,
    /// ambient/discovery flags) are left untouched.
    /// </summary>
    public static FlagPlan PlanClearForwardFlags(WorldSaveData facility, string chapterRow)
    {
        var targetIndex = StoryProgressionCatalog.IndexOf(chapterRow);
        if (targetIndex < 0) return new FlagPlan(null, 0, $"Unknown chapter '{chapterRow}'.");

        var forwardTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = targetIndex + 1; i < StoryProgressionCatalog.Chapters.Count; i++)
        {
            if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } flag)
            {
                forwardTriggers.Add(flag);
            }
        }

        var toRemove = new HashSet<string>(
            FlagGate.DependentsOf(forwardTriggers, facility.Flags), StringComparer.OrdinalIgnoreCase);
        toRemove.UnionWith(FlagGate.FlagsPastChapter(targetIndex, facility.Flags));
        var flags = facility.Flags.Where(f => !toRemove.Contains(f)).ToList();
        var removed = facility.Flags.Count - flags.Count;

        return removed == 0
            ? new FlagPlan(null, 0, "No chapter flags beyond the current chapter are set - nothing to clear.")
            : new FlagPlan(flags, removed,
                $"Cleared {removed} forward chapter/quest flag(s) from WorldSave_Facility.sav (backup kept).");
    }

    /// <summary>
    /// Adds arbitrary world flags (trader gating, stock unlocks...) to the Facility save
    /// next to <paramref name="metadataSavePath"/>. Additive only, written immediately
    /// with the standard pre-write backup. Returns how many flags were actually new.
    /// </summary>
    public static (int Added, string Message) AddFacilityFlags(
        string metadataSavePath, IReadOnlyCollection<string> flagsToAdd)
        => Apply(metadataSavePath, facility => PlanAddFlags(facility, flagsToAdd));

    /// <summary>The sibling <c>WorldSave_Facility.sav</c> path, or null when absent.</summary>
    public static string? SiblingFacilityPath(string metadataSavePath)
    {
        var folder = Path.GetDirectoryName(metadataSavePath);
        if (folder is null) return null;
        var facilityPath = Path.Combine(folder, FacilitySaveName);
        return File.Exists(facilityPath) ? facilityPath : null;
    }

    /// <summary>
    /// Ensures all chapter trigger flags up to (and including) <paramref name="chapterRow"/>
    /// exist in the Facility world save next to <paramref name="metadataSavePath"/>.
    /// Returns how many flags were added and a human-readable status.
    /// </summary>
    public static (int Added, string Message) SyncFacilityFlags(string metadataSavePath, string chapterRow)
        => Apply(metadataSavePath, facility => PlanSyncToChapter(facility, chapterRow));

    /// <summary>
    /// Removes the forward chapter/quest flags described by <see cref="PlanClearForwardFlags"/>
    /// from the Facility save next to <paramref name="metadataSavePath"/>.
    /// </summary>
    public static (int Removed, string Message) ClearForwardFlags(string metadataSavePath, string chapterRow)
        => Apply(metadataSavePath, facility => PlanClearForwardFlags(facility, chapterRow));

    /// <summary>Reads the sibling Facility save, runs a plan against it, and writes it back.</summary>
    private static (int Count, string Message) Apply(string metadataSavePath, Func<WorldSaveData, FlagPlan> plan)
    {
        var facilityPath = SiblingFacilityPath(metadataSavePath);
        if (facilityPath is null)
        {
            return (0, $"{FacilitySaveName} not found next to the metadata save.");
        }

        var data = WorldSaveReader.ReadFromFile(facilityPath);
        var result = plan(data);
        if (result.Flags is null) return (result.Count, result.Message);

        WorldSaveWriter.ApplyFlags(data, result.Flags);
        WorldSaveWriter.WriteToFile(data, facilityPath);
        return (result.Count, result.Message);
    }
}
