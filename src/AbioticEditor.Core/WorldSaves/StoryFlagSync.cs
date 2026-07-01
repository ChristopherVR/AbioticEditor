namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Cross-file helper: when the metadata save's story chapter is set, the matching
/// trigger flags live in the sibling <c>WorldSave_Facility.sav</c> (the main level).
/// This adds every missing trigger flag up to a chapter - additive only, with the
/// standard pre-write backup.
/// </summary>
public static class StoryFlagSync
{
    /// <summary>
    /// Adds arbitrary world flags (trader gating, stock unlocks...) to the Facility save
    /// next to <paramref name="metadataSavePath"/>. Additive only, written immediately
    /// with the standard pre-write backup. Returns how many flags were actually new.
    /// </summary>
    public static (int Added, string Message) AddFacilityFlags(
        string metadataSavePath, IReadOnlyCollection<string> flagsToAdd)
    {
        var facilityPath = SiblingFacilityPath(metadataSavePath);
        if (facilityPath is null)
        {
            return (0, "WorldSave_Facility.sav not found next to the metadata save.");
        }

        var data = WorldSaveReader.ReadFromFile(facilityPath);
        var flags = data.Flags.ToList();
        var have = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var flag in flagsToAdd)
        {
            if (string.IsNullOrWhiteSpace(flag) || have.Contains(flag)) continue;
            flags.Add(flag);
            have.Add(flag);
            added++;
        }

        if (added == 0)
        {
            return (0, "All of those flags are already set in WorldSave_Facility.sav - nothing to do.");
        }

        WorldSaveWriter.ApplyFlags(data, flags);
        WorldSaveWriter.WriteToFile(data, facilityPath);
        return (added, $"Added {added} world flag(s) to WorldSave_Facility.sav (backup kept).");
    }

    /// <summary>The sibling <c>WorldSave_Facility.sav</c> path, or null when absent.</summary>
    public static string? SiblingFacilityPath(string metadataSavePath)
    {
        var folder = Path.GetDirectoryName(metadataSavePath);
        if (folder is null) return null;
        var facilityPath = Path.Combine(folder, "WorldSave_Facility.sav");
        return File.Exists(facilityPath) ? facilityPath : null;
    }

    /// <summary>
    /// Ensures all chapter trigger flags up to (and including) <paramref name="chapterRow"/>
    /// exist in the Facility world save next to <paramref name="metadataSavePath"/>.
    /// Returns how many flags were added and a human-readable status.
    /// </summary>
    public static (int Added, string Message) SyncFacilityFlags(string metadataSavePath, string chapterRow)
    {
        var facilityPath = SiblingFacilityPath(metadataSavePath);
        if (facilityPath is null)
        {
            return (0, "WorldSave_Facility.sav not found next to the metadata save.");
        }

        var targetIndex = StoryProgressionCatalog.IndexOf(chapterRow);
        if (targetIndex < 0)
        {
            return (0, $"Unknown chapter '{chapterRow}'.");
        }

        var data = WorldSaveReader.ReadFromFile(facilityPath);
        var flags = data.Flags.ToList();
        var have = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        for (var i = 0; i <= targetIndex; i++)
        {
            var flag = StoryProgressionCatalog.Chapters[i].TriggerFlag;
            if (flag is null || have.Contains(flag)) continue;
            flags.Add(flag);
            added++;
        }

        if (added == 0)
        {
            return (0, "Facility flags already match that chapter - nothing to do.");
        }

        WorldSaveWriter.ApplyFlags(data, flags);
        WorldSaveWriter.WriteToFile(data, facilityPath);
        return (added, $"Added {added} story flag(s) to WorldSave_Facility.sav (backup kept).");
    }

    /// <summary>
    /// The revert counterpart of <see cref="SyncFacilityFlags"/>: removes the trigger flags of
    /// every chapter AFTER <paramref name="chapterRow"/> from the sibling Facility save, plus every
    /// granular flag <see cref="FlagGate.DependentsOf"/> finds built on top of them (per
    /// <see cref="QuestFlagDependencies"/>), so a story rollback stops showing later chapters -
    /// down to individual quest steps - as done. Flags with no curated dependency chain (side
    /// content, ambient/discovery flags) are left untouched, same as before.
    /// </summary>
    public static (int Removed, string Message) ClearForwardFlags(string metadataSavePath, string chapterRow)
    {
        var folder = Path.GetDirectoryName(metadataSavePath);
        if (folder is null)
        {
            return (0, "Could not resolve the save folder.");
        }

        var facilityPath = Path.Combine(folder, "WorldSave_Facility.sav");
        if (!File.Exists(facilityPath))
        {
            return (0, "WorldSave_Facility.sav not found next to the metadata save.");
        }

        var targetIndex = StoryProgressionCatalog.IndexOf(chapterRow);
        if (targetIndex < 0)
        {
            return (0, $"Unknown chapter '{chapterRow}'.");
        }

        var forwardTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = targetIndex + 1; i < StoryProgressionCatalog.Chapters.Count; i++)
        {
            if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } flag)
            {
                forwardTriggers.Add(flag);
            }
        }

        var data = WorldSaveReader.ReadFromFile(facilityPath);
        var toRemove = FlagGate.DependentsOf(forwardTriggers, data.Flags);
        var flags = data.Flags.Where(f => !toRemove.Contains(f)).ToList();
        var removed = data.Flags.Count - flags.Count;
        if (removed == 0)
        {
            return (0, "No chapter flags beyond the current chapter are set - nothing to clear.");
        }

        WorldSaveWriter.ApplyFlags(data, flags);
        WorldSaveWriter.WriteToFile(data, facilityPath);
        return (removed, $"Cleared {removed} forward chapter/quest flag(s) from WorldSave_Facility.sav (backup kept).");
    }
}
