using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One kind of leftover a rewind would not reverse, counted in one region save.</summary>
/// <param name="ConsequenceId">The matching <see cref="RewindConsequence.Id"/>.</param>
/// <param name="Title">Short player-facing description of what was counted.</param>
/// <param name="Region">The region save token (file name without extension).</param>
/// <param name="OpensAtChapter">The chapter row at which the region opens, or null when the region cannot be attributed to a chapter.</param>
/// <param name="Count">How many entries were counted.</param>
/// <param name="Examples">Up to three actor names, for a reviewer to look at.</param>
public sealed record RewindResidual(
    string ConsequenceId,
    string Title,
    string Region,
    string? OpensAtChapter,
    int Count,
    IReadOnlyList<string> Examples);

/// <summary>
/// What rewinding the story to <see cref="TargetChapterRow"/> would leave behind, worked out from
/// already-read saves. Read-only: it plans nothing and writes nothing.
/// </summary>
/// <param name="TargetChapterRow">The chapter the story would be rewound to.</param>
/// <param name="FlagsCleared">How many Facility world flags the rewind would remove.</param>
/// <param name="Residuals">Counted leftovers in regions the rewound story has not reached yet.</param>
/// <param name="Unattributed">Counted leftovers in saves whose region spans several chapters (the aggregate Facility level) or has no known gate; shown for awareness only.</param>
public sealed record StoryRollbackPreview(
    string TargetChapterRow,
    int FlagsCleared,
    IReadOnlyList<RewindResidual> Residuals,
    IReadOnlyList<RewindResidual> Unattributed)
{
    /// <summary>True when at least one leftover was found in a not-yet-reached region.</summary>
    public bool HasResiduals => Residuals.Count > 0;

    /// <summary>Total leftover entries in not-yet-reached regions.</summary>
    public int ResidualTotal => Residuals.Sum(r => r.Count);
}

/// <summary>
/// Builds the read-only "rollback preview": the physical consequences of story progress that a
/// rewind (<see cref="StoryFlagSync.PlanClearForwardFlags"/>) would NOT reverse, so the player can
/// see them before rewinding. See <see cref="StoryRewindConsequenceCatalog"/> for the reviewed map.
/// </summary>
/// <remarks>
/// A region save is attributed to a chapter only where <see cref="StoryRewindConsequenceCatalog.RegionOpensAtChapter"/>
/// is known. Counting is by save state that the game itself wrote; it says "this door is in a
/// non-default state in a region that opens after the target chapter", not "this door was opened by
/// that chapter" - the save carries no such link (see the research doc).
/// </remarks>
public static class StoryRollbackPreviewBuilder
{
    // The default (closed) door enumerator; anything else is a door the world has moved off its default.
    private const string ClosedDoorState = "NewEnumerator0";

    // The default narrative script stage. Non-default stages only ever appear on dead entries in the fixtures.
    private const string DefaultNarrativeState = "NewEnumerator3";

    private sealed record Rule(string ConsequenceId, string Title, string Map, Func<IList<FPropertyTag>, bool> Counts);

    private static readonly Rule[] Rules =
    [
        new("doors", "doors moved off their default state", "SimpleDoorMap",
            p => p.FindByPrefix("DoorState_")?.Property?.Value?.ToString() is { } s
                 && !s.EndsWith(ClosedDoorState, StringComparison.Ordinal)),
        new("doors", "security doors standing open", "SecurityDoorMap", p => p.TryGetBool("IsDoorOpen_") == true),
        new("npcs", "story characters marked dead", "NarrativeNPCMap", p => p.TryGetBool("IsDead_") == true),
        new("npcs", "story characters past their first script stage", "NarrativeNPCMap",
            p => p.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString() is { } s
                 && !s.EndsWith(DefaultNarrativeState, StringComparison.Ordinal)),
        new("triggers", "triggers that already fired", "TriggerMap", p => Int(p, "TimesTriggered_") > 0),
        new("buttons", "buttons already pressed", "ButtonMap", p => p.TryGetBool("ButtonHasBeenPressedOnce_") == true),
        new("elevators", "elevators parked at the top", "ElevatorMap", p => p.TryGetBool("TopOpen_") == true),
        new("portals", "portals switched on", "PortalMap", p => p.TryGetBool("PortalActive_") == true),
        new("destructibles", "broken barriers and props", "DestructibleMap", p => p.TryGetBool("Broken_") == true),
        new("spawns", "spawn points that already spawned", "NPCSpawnMap", p => p.TryGetBool("HasSpawnedOnce_") == true),
        new("loot", "looted corpses", "CorpseMap", p => p.TryGetBool("IsLooted_") == true),
    ];

    /// <summary>
    /// Builds the preview for rewinding to <paramref name="targetChapterRow"/>.
    /// </summary>
    /// <param name="targetChapterRow">A <see cref="StoryProgressionCatalog"/> row.</param>
    /// <param name="facility">The already-read <c>WorldSave_Facility.sav</c>, used only to count the flags a rewind would clear (null counts 0).</param>
    /// <param name="regionSaves">Every already-read region save with its file name; the metadata save is ignored.</param>
    public static StoryRollbackPreview Build(
        string targetChapterRow,
        WorldSaveData? facility,
        IEnumerable<(string FileName, WorldSaveData Data)> regionSaves)
    {
        ArgumentNullException.ThrowIfNull(regionSaves);
        var target = StoryProgressionCatalog.IndexOf(targetChapterRow);
        if (target < 0) throw new ArgumentException($"Unknown chapter '{targetChapterRow}'.", nameof(targetChapterRow));

        var flagsCleared = facility is null ? 0 : StoryFlagSync.PlanClearForwardFlags(facility, targetChapterRow).Count;

        var residuals = new List<RewindResidual>();
        var unattributed = new List<RewindResidual>();

        foreach (var (fileName, data) in regionSaves)
        {
            var region = Path.GetFileNameWithoutExtension(fileName);
            if (region.EndsWith("MetaData", StringComparison.OrdinalIgnoreCase)) continue;

            var opens = StoryRewindConsequenceCatalog.RegionOpensAtChapter(region);
            var opensIndex = opens is null ? -1 : StoryProgressionCatalog.IndexOf(opens);
            var attributable = opensIndex > target;
            var start = opens is null;   // unknown gate or spans chapters
            if (!attributable && !start) continue;   // region is reachable at the target chapter: not a contradiction

            foreach (var rule in Rules)
            {
                var (count, examples) = Count(data.Raw, rule);
                if (count == 0) continue;
                var row = new RewindResidual(rule.ConsequenceId, rule.Title, region, opens, count, examples);
                (attributable ? residuals : unattributed).Add(row);
            }
        }

        return new StoryRollbackPreview(targetChapterRow, flagsCleared, residuals, unattributed);
    }

    private static (int Count, IReadOnlyList<string> Examples) Count(SaveGame save, Rule rule)
    {
        var pairs = WorldSaveReader.GetMapPairs(save.Properties, rule.Map);
        if (pairs is null) return (0, []);

        var count = 0;
        var examples = new List<string>();
        foreach (var kv in pairs)
        {
            if (kv.Value is not StructProperty { Value: PropertiesStruct ps }) continue;
            if (!rule.Counts(ps.Properties)) continue;
            count++;
            if (examples.Count < 3 && WorldSaveReader.ExtractMapKeyString(kv.Key) is { } key)
            {
                var dot = key.LastIndexOf('.');
                examples.Add(dot >= 0 ? key[(dot + 1)..] : key);
            }
        }
        return (count, examples);
    }

    private static int Int(IList<FPropertyTag> props, string prefix)
        => props.FindByPrefix(prefix)?.Property?.Value switch { int i => i, long l => (int)l, _ => 0 };
}
