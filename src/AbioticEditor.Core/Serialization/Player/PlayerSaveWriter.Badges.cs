using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - "NEW" badge sync, the research queue, and the two small
// CharacterSaveData fields (CompletedIntro_, LastControlRotation_).
public static partial class PlayerSaveWriter
{
    /// <summary>
    /// Mirrors the game's own behaviour: when an entry is newly added to a discovery array
    /// (recipes/compendium/journal/fish), it also goes into the matching "NEW" badge array so
    /// the in-game UI shows the toast/highlight; when an entry is removed, it comes back out of
    /// the badge array too so a relocked/cleared entry doesn't leave a stale badge behind.
    /// Read the source array's *current* contents before the caller overwrites it, diff against
    /// <paramref name="newValues"/>, and patch the badge array in place. A no-op diff never
    /// touches the badge array (so an unrelated edit can't create a tag on a save that never had
    /// one).
    /// </summary>
    private static void SyncNewBadge(
        IList<FPropertyTag> root, string sourcePrefix, IReadOnlyList<string> newValues,
        string badgePrefix, string badgeFullName)
        => SyncNewBadgeFromSnapshot(root, GvasTags.ReadNameArray(root, sourcePrefix), newValues, badgePrefix, badgeFullName);

    /// <summary>
    /// Same as <see cref="SyncNewBadge"/>, but takes the "before" snapshot explicitly - used
    /// where the before-state isn't a single source array (e.g. the compendium's three
    /// section arrays combined).
    /// </summary>
    private static void SyncNewBadgeFromSnapshot(
        IList<FPropertyTag> root, IReadOnlyList<string> oldValues, IReadOnlyList<string> newValues,
        string badgePrefix, string badgeFullName)
    {
        var newSet = new HashSet<string>(newValues, StringComparer.Ordinal);
        var oldSet = new HashSet<string>(oldValues, StringComparer.Ordinal);

        var added = newValues.Where(v => !oldSet.Contains(v)).ToList();
        var removed = oldValues.Where(v => !newSet.Contains(v)).ToList();
        if (added.Count == 0 && removed.Count == 0) return;

        var badge = GvasTags.ReadNameArray(root, badgePrefix).ToList();
        if (removed.Count > 0)
        {
            var removedSet = new HashSet<string>(removed, StringComparer.Ordinal);
            badge.RemoveAll(removedSet.Contains);
        }
        foreach (var entry in added)
        {
            if (!badge.Contains(entry, StringComparer.Ordinal)) badge.Add(entry);
        }
        ReplaceNameArray(root, badgePrefix, badge, badgeFullName);
    }

    /// <summary>
    /// Replaces the <c>RecipesRequiringResearch_</c> name array (recipes waiting on the
    /// research bench rather than fully unlocked). Not every fixture save carries this tag
    /// (several real characters have never queued anything), so an empty queue only creates
    /// the tag when it already exists - an empty <paramref name="recipes"/> against an absent
    /// tag is left alone rather than manufacturing a tag the game itself would never have
    /// written, matching the delta-serialization rule the rest of this writer follows.
    /// </summary>
    public static void ApplyResearchQueue(PlayerSaveData data, IReadOnlyList<string> recipes)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "RecipesRequiringResearch_", recipes,
            recipes.Count > 0 ? FullNames.RecipesRequiringResearch : null);
    }

    /// <summary>Sets <c>CompletedIntro_</c>; creates the tag when the save predates it.</summary>
    public static void ApplyCompletedIntro(PlayerSaveData data, bool completed)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        SetBool(root, "CompletedIntro_", completed, FullNames.CompletedIntro);
    }

    /// <summary>
    /// Sets <c>LastControlRotation_</c> (pitch/yaw/roll degrees). Every fixture save carries
    /// this tag already, but the writer still creates it on a save that doesn't, per the
    /// delta-serialization rule the rest of this writer follows.
    /// </summary>
    public static void ApplyLastControlRotation(PlayerSaveData data, double pitch, double yaw, double roll)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        GvasTags.SetRotator(root, "LastControlRotation_", pitch, yaw, roll, FullNames.LastControlRotation);
    }
}
