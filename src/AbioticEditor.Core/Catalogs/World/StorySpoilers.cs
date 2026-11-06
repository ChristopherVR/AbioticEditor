namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Whether a main-quest chapter is still ahead of a given world, so the story checklist can keep
/// the names, summaries and card art of chapters the player has not reached sealed behind the
/// spoiler guard (the same guard the TRADERS roster uses for traders you have not met).
///
/// <para>"Reached" is read from the save itself, never from a hand-kept list: the chapter row the
/// metadata save stores, and the chapter trigger flags the world already carries. Because the
/// chapters are strictly linear, the furthest of those two marks everything up to it as reached,
/// even when an intermediate trigger flag was never persisted.</para>
/// </summary>
public static class StorySpoilers
{
    /// <summary>
    /// The index of the furthest chapter this world has reached: the later of the stored chapter
    /// row and the furthest chapter whose trigger flag is set. Returns -1 when neither says anything.
    /// </summary>
    /// <param name="storedRow">The metadata save's <c>StoryProgressionRow</c>, or null.</param>
    /// <param name="hasWorldFlag">Whether the world carries a given flag.</param>
    public static int FurthestReachedIndex(string? storedRow, Func<string, bool> hasWorldFlag)
    {
        ArgumentNullException.ThrowIfNull(hasWorldFlag);
        return Math.Max(
            StoryProgressionCatalog.IndexOf(storedRow),
            StoryProgressionCatalog.FurthestReachedIndex(hasWorldFlag));
    }

    /// <summary>
    /// True when the chapter at <paramref name="index"/> is past the point this world has reached,
    /// and so should be shown as classified. The opening chapter is never hidden: every new game
    /// starts there, so it spoils nothing. An index outside the catalog is never hidden either.
    /// </summary>
    public static bool IsFutureChapter(int index, string? storedRow, Func<string, bool> hasWorldFlag)
    {
        ArgumentNullException.ThrowIfNull(hasWorldFlag);
        if (index <= 0 || index >= StoryProgressionCatalog.Chapters.Count) return false;
        if (StoryProgressionCatalog.Chapters[index].TriggerFlag is { } trigger && hasWorldFlag(trigger)) return false;
        return index > FurthestReachedIndex(storedRow, hasWorldFlag);
    }
}
