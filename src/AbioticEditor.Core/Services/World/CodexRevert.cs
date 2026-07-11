using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Clears codex/email/journal rows the story hasn't reached anymore - the codex counterpart
/// of <see cref="StoryFlagSync.ClearForwardFlags"/>. A row is "forward" (and gets cleared)
/// when <see cref="FlagGate.RegionChapterForRowId"/> resolves it to a chapter whose trigger
/// flag isn't set among <c>reachedFlags</c>; unmapped rows (side content, no fixed story
/// gate) are always kept. Covers both the world-wide (metadata save) unlock arrays and each
/// player's own read/discovered lists - reverting a story chapter should stop spoiling
/// content from chapters the save no longer claims to have reached, in either place.
/// </summary>
public static class CodexRevert
{
    /// <summary>The metadata-save prefixes this covers (email/journal/compendium only - not
    /// item pickups, which don't reliably follow the story's area-prefix convention).</summary>
    public static readonly IReadOnlyList<string> GlobalCodexPrefixes =
    [
        "GlobalEmailsRead_", "GlobalJournalEntries_",
        "GlobalCompendiumEmail_", "GlobalCompendiumNarrative_", "GlobalCompendiumExploration_",
    ];

    /// <summary>True when a row's story gate (if any) is satisfied by <paramref name="reachedFlags"/>.</summary>
    public static bool IsReachable(string rowId, IReadOnlySet<string> reachedFlags)
    {
        var chapter = FlagGate.RegionChapterForRowId(rowId);
        return chapter?.TriggerFlag is null || reachedFlags.Contains(chapter.TriggerFlag);
    }

    /// <summary>Filters a row list down to what <paramref name="reachedFlags"/> allows.</summary>
    public static IReadOnlyList<string> ClearForwardRows(IReadOnlyList<string> rows, IReadOnlySet<string> reachedFlags)
        => rows.Where(r => IsReachable(r, reachedFlags)).ToList();

    /// <summary>
    /// The world-wide (metadata save) codex/email/journal arrays that need trimming for
    /// <paramref name="reachedFlags"/> - only prefixes that actually changed are present, so
    /// callers can tell "nothing to do" from "cleared down to nothing". Does not write; the
    /// caller applies each entry (directly via <see cref="WorldSaveWriter.ApplyGlobalUnlockArray"/>,
    /// or staged, matching how the rest of a world editor's pending edits are handled).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ClearForwardGlobalUnlocks(
        WorldSaveData metadata, IReadOnlySet<string> reachedFlags)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var prefix in GlobalCodexPrefixes)
        {
            var current = WorldSaveReader.ReadGlobalUnlockArray(metadata.Raw, prefix);
            var kept = ClearForwardRows(current, reachedFlags);
            if (kept.Count != current.Count) result[prefix] = kept;
        }
        return result;
    }

    /// <summary>
    /// Clears codex/email/journal rows beyond <paramref name="reachedFlags"/> on every
    /// <c>Player_*.sav</c> in the <c>PlayerData</c> folder next to <paramref name="metadataSavePath"/>.
    /// A cross-file write like <see cref="StoryFlagSync"/>'s (each changed player file is read,
    /// patched and written immediately with the standard pre-write backup) since these saves
    /// aren't otherwise open/staged by whatever is driving the revert.
    /// </summary>
    public static (int PlayersChanged, int RowsRemoved, string Message) ClearForwardPlayerUnlocks(
        string metadataSavePath, IReadOnlySet<string> reachedFlags)
    {
        var folder = Path.GetDirectoryName(metadataSavePath);
        var playerDir = folder is null ? null : Path.Combine(folder, "PlayerData");
        if (playerDir is null || !Directory.Exists(playerDir))
        {
            return (0, 0, "No PlayerData folder found next to the metadata save.");
        }

        var playersChanged = 0;
        var rowsRemoved = 0;
        foreach (var playerPath in Directory.EnumerateFiles(playerDir, "Player_*.sav"))
        {
            var data = PlayerSaveReader.ReadFromFile(playerPath);
            var emails = ClearForwardRows(data.EmailsRead, reachedFlags);
            var journals = ClearForwardRows(data.Journals, reachedFlags);
            var compEmail = ClearForwardRows(data.CompendiumEmail, reachedFlags);
            var compNarrative = ClearForwardRows(data.CompendiumNarrative, reachedFlags);
            var compExploration = ClearForwardRows(data.CompendiumExploration, reachedFlags);

            var removedHere = (data.EmailsRead.Count - emails.Count) + (data.Journals.Count - journals.Count)
                + (data.CompendiumEmail.Count - compEmail.Count) + (data.CompendiumNarrative.Count - compNarrative.Count)
                + (data.CompendiumExploration.Count - compExploration.Count);
            if (removedHere == 0) continue;

            PlayerSaveWriter.ApplyEmailsRead(data, emails);
            PlayerSaveWriter.ApplyJournals(data, journals);
            PlayerSaveWriter.ApplyCompendium(data, compEmail, compNarrative, compExploration);
            PlayerSaveWriter.WriteToFile(data, playerPath);

            playersChanged++;
            rowsRemoved += removedHere;
        }

        return (playersChanged, rowsRemoved, rowsRemoved == 0
            ? "No forward codex/email/journal entries to clear."
            : $"Cleared {rowsRemoved} codex/email/journal row(s) across {playersChanged} player save(s) (backups kept).");
    }
}
