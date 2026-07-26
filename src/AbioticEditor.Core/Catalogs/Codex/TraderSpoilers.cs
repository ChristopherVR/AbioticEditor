using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.Codex;

/// <summary>
/// Whether a trader is still future content for a given save, so surfaces that name traders can
/// hide the ones the player has not met.
///
/// <para>This lives in Core because more than one screen names traders: the world editor's
/// TRADERS roster, and the recipe book, which says who sells a given item. The recipe book used
/// to print the name unconditionally, which meant a taco recipe cheerfully announced a trader
/// who only appears after the game is finished.</para>
/// </summary>
public static class TraderSpoilers
{
    /// <summary>
    /// True when this trader is past the point the save has reached, and so should be shown as
    /// classified. A trader with no curated lore entry has no known gate and is never hidden.
    /// </summary>
    /// <param name="traderId">Trader row id, e.g. <c>Jimmy</c>.</param>
    /// <param name="hasWorldFlag">Whether the save carries a given world flag.</param>
    public static bool IsFutureContent(string? traderId, Func<string, bool> hasWorldFlag)
    {
        ArgumentNullException.ThrowIfNull(hasWorldFlag);
        if (traderId is null || !TraderLore.ById.TryGetValue(traderId, out var lore)) return false;
        return !IsGateSatisfied(lore, hasWorldFlag);
    }

    /// <summary>
    /// A curated spoiler-gate flag counts as satisfied once the save carries it, or once the
    /// story has reached the chapter that flag triggers.
    /// </summary>
    public static bool IsGateSatisfied(TraderLore.Entry? lore, Func<string, bool> hasWorldFlag)
    {
        ArgumentNullException.ThrowIfNull(hasWorldFlag);
        if (lore?.SpoilerGateFlag is not { } gate) return true;
        if (hasWorldFlag(gate)) return true;

        var gateChapter = StoryProgressionCatalog.ChapterIndexForFlag(gate);
        return gateChapter >= 0 && StoryProgressionCatalog.FurthestReachedIndex(hasWorldFlag) >= gateChapter;
    }
}
