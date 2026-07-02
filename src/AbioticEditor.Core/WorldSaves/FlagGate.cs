namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Prerequisite model for world flags. Two rules:
/// 1. Story trigger flags (DT_StoryProgression) are strictly linear - every earlier
///    chapter's trigger must be set first.
/// 2. Ordinary flags belong to a region (area prefix); the region only becomes
///    reachable at a specific story chapter, so that chapter's trigger flag is a
///    prerequisite (e.g. no Labs flag makes sense before <c>Labs_MiddleProgression</c>'s
///    predecessors fired).
/// Areas with no story mapping (anomaly portals like NightRealm, account-level metas)
/// are left ungated.
/// </summary>
public static class FlagGate
{
    /// <summary>Canonical flag area -> the chapter row where that region opens.</summary>
    private static readonly Dictionary<string, string> AreaToChapterRow = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Weather"] = "Flathill",        // Fog_* / Weather* = the Flathill portal world
        ["Manufacturing"] = "MF",
        ["MF"] = "MF",
        ["Train"] = "MFTrain",
        ["MFMines"] = "MFMines",
        ["Pens"] = "Pens",
        ["Labs"] = "Labs",
        ["Security"] = "PostLabs",
        ["Dams"] = "EndSecurity",
        ["Voussoir"] = "Voussoir",
        ["VSignal"] = "Voussoir",
        ["Plant"] = "PowerServices",
        ["Reactors"] = "ReactorsEntry",
        ["Residence"] = "Residence",
        ["Fracture"] = "Fracture",
        ["Botanical"] = "Botanical",
        ["V"] = "InqEnd",       // V_INQ_* = the Praetorium
        ["Res"] = "Residence2", // Res_Objective1_* = the storm suppressor
        ["Wall"] = "DarkLens",
        ["End"] = "EndGame",
        // Office/Tram are reachable from the start; portal anomalies (NightRealm,
        // MirrorWorld, Snowglobe, Salem, Rise) have no fixed story gate.
    };

    // Both lookups are pure functions of static catalogs but sit on hot UI paths
    // (computed once per flag on every flag-list rebuild). Memoize them - the key space
    // is bounded by the flag vocabulary, so the caches stay small.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> PrereqCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, StoryChapter?> RegionChapterCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The flags (in story order) that should be set before <paramref name="flag"/> is
    /// enabled. Empty = ungated.
    /// </summary>
    public static IReadOnlyList<string> PrerequisitesFor(string flag)
        => PrereqCache.GetOrAdd(flag, static f =>
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Rule 1: chapter triggers require every earlier trigger.
            if (StoryProgressionCatalog.ChapterForFlag(f) is { } chapter)
            {
                var index = StoryProgressionCatalog.IndexOf(chapter.Row);
                foreach (var t in StoryProgressionCatalog.Chapters
                             .Take(Math.Max(0, index))
                             .Where(c => c.TriggerFlag is not null)
                             .Select(c => c.TriggerFlag!))
                {
                    if (seen.Add(t)) result.Add(t);
                }
            }
            else
            {
                // Rule 2: region flags require the chapter where their region opens.
                var area = QuestFlagCatalog.Lookup(f).Area;
                if (AreaToChapterRow.TryGetValue(area, out var row)
                    && StoryProgressionCatalog.Find(row)?.TriggerFlag is { } trigger
                    && !trigger.Equals(f, StringComparison.OrdinalIgnoreCase))
                {
                    if (seen.Add(trigger)) result.Add(trigger);
                }
            }

            // Rule 3: curated granular per-quest dependencies (transitive), so completing a quest
            // step also offers to set the steps it depends on (e.g. CafeteriaUnlocked -> the quest
            // must have been started). Keeps the editor from writing a half-finished quest.
            AddGranularPrerequisites(f, result, seen);

            return result;
        });

    // Walks QuestFlagDependencies transitively, appending each unseen prerequisite.
    private static void AddGranularPrerequisites(string flag, List<string> result, HashSet<string> seen)
        => AddGranularPrerequisites(flag, result, seen, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static void AddGranularPrerequisites(
        string flag, List<string> result, HashSet<string> seen, HashSet<string> expanded)
    {
        // Expand each flag's granular dependencies at most once (cycle and diamond-graph guard).
        if (!expanded.Add(flag)) return;
        foreach (var dep in QuestFlagDependencies.DirectPrerequisites(flag))
        {
            if (seen.Add(dep)) result.Add(dep);
            // Recurse even when the dep was already seen (e.g. a chapter rule added it first) - its
            // own granular children still need expanding, or a step "under" a trigger gets skipped.
            AddGranularPrerequisites(dep, result, seen, expanded);
        }
    }

    // Reverse of QuestFlagDependencies.Direct (prereq -> the flags that need it), built once. Powers
    // DependentsOf: the same curated graph PrerequisitesFor walks forward, walked backward.
    private static readonly Lazy<Dictionary<string, List<string>>> ReverseDependencies = new(() =>
    {
        var reverse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (flag, prereqs) in QuestFlagDependencies.Direct)
        {
            foreach (var prereq in prereqs)
            {
                if (!reverse.TryGetValue(prereq, out var dependents))
                {
                    reverse[prereq] = dependents = new List<string>();
                }
                dependents.Add(flag);
            }
        }
        return reverse;
    });

    /// <summary>
    /// Every currently-set flag that (directly or transitively, per <see cref="QuestFlagDependencies"/>)
    /// required one of <paramref name="roots"/> - the granular counterpart of clearing a chapter
    /// trigger. A story rewind that only removes the 37 curated trigger flags leaves every quest step
    /// built on top of them still set, so the game keeps reading the world as further along than the
    /// rewound chapter; this closes that gap by pulling in the whole dependent chain.
    /// </summary>
    public static IReadOnlySet<string> DependentsOf(IEnumerable<string> roots, IEnumerable<string> currentlySet)
    {
        var setFlags = new HashSet<string>(currentlySet, StringComparer.OrdinalIgnoreCase);
        var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var root in roots)
        {
            if (toRemove.Add(root)) queue.Enqueue(root);
        }

        var reverse = ReverseDependencies.Value;
        while (queue.Count > 0)
        {
            var flag = queue.Dequeue();
            if (!reverse.TryGetValue(flag, out var dependents)) continue;
            foreach (var dependent in dependents)
            {
                if (!setFlags.Contains(dependent)) continue; // not set, nothing to remove
                if (toRemove.Add(dependent)) queue.Enqueue(dependent);
            }
        }

        return toRemove;
    }

    /// <summary>The chapter whose region this flag belongs to (for card art / context), if mapped.</summary>
    public static StoryChapter? RegionChapterFor(string flag)
        => RegionChapterCache.GetOrAdd(flag, static f =>
        {
            if (StoryProgressionCatalog.ChapterForFlag(f) is { } own) return own;
            var area = QuestFlagCatalog.Lookup(f).Area;
            return AreaToChapterRow.TryGetValue(area, out var row) ? StoryProgressionCatalog.Find(row) : null;
        });

    /// <summary>
    /// The chapter gating an arbitrary row id. Journal ids share the flags' area-prefix
    /// convention directly (e.g. <c>Labs_AbeandJanet</c>). Email ids instead lead with an
    /// <c>Email_</c>/<c>email_</c> marker before the region (e.g. <c>Email_Labs_Kizz</c>,
    /// <c>email_labs_creepingcrystal</c>) - verified against real save data, not the reverse
    /// order the name might suggest. Null = no fixed story gate known (side content, or a
    /// row whose name doesn't embed a recognised area).
    /// </summary>
    public static StoryChapter? RegionChapterForRowId(string rowId)
    {
        var withoutEmailMarker = rowId.StartsWith("Email_", StringComparison.OrdinalIgnoreCase)
            ? rowId[6..]
            : rowId;
        var area = QuestFlagCatalog.Lookup(withoutEmailMarker).Area;
        return AreaToChapterRow.TryGetValue(area, out var row) ? StoryProgressionCatalog.Find(row) : null;
    }
}
