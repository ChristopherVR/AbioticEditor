namespace AbioticEditor.Core.WorldSaves;

/// <summary>Whether the editor's story rewind undoes a given consequence of story progress.</summary>
public enum RewindReversal
{
    /// <summary>The rewind clears or resets it.</summary>
    Reversed,

    /// <summary>The rewind leaves it as it is, so it can contradict the rewound chapter.</summary>
    NotReversed,
}

/// <summary>
/// One reviewed entry of the rewind consequence map: something the story changes in a save, where it
/// is stored, and whether <c>StoryFlagSync.PlanClearForwardFlags</c> and its companions undo it.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Title">Short player-facing name.</param>
/// <param name="SaveLocation">Which save file and property holds it.</param>
/// <param name="Reversal">Whether a rewind undoes it.</param>
/// <param name="Detail">What a player would see, and why the rewind does or does not reach it.</param>
/// <param name="Evidence">The fixture or code that backs the entry (so a reviewer can re-check it).</param>
public sealed record RewindConsequence(
    string Id,
    string Title,
    string SaveLocation,
    RewindReversal Reversal,
    string Detail,
    string Evidence);

/// <summary>
/// The reviewed map from story-driven physical consequences to what a story rewind does about them,
/// plus the region-to-chapter table used to attribute a region save's leftovers to a chapter.
/// </summary>
/// <remarks>
/// Built from the existing catalogs and writers (see docs/research/world-and-placed-object-state.md):
/// <list type="bullet">
///   <item>What a rewind clears is exactly what <c>StoryFlagSync.PlanClearForwardFlags</c>,
///   <c>CodexRevert</c> and <c>PlayerRespawnRevert</c> write.</item>
///   <item>Every other placed-object map is written by the game at runtime and is keyed by a level
///   actor, with no link back to a chapter flag in the save. The link, where one exists, lives in the
///   cooked level (for example a door's <c>WorldFlagToUnlock</c>, see <c>DoorGateResolver</c>), so it
///   is only available with the game installed.</item>
/// </list>
/// The map is deliberately conservative: it states which chapter a region opens at only where an
/// existing catalog already asserts it (<c>FlagGate</c>'s area gates), and marks the rest as
/// unattributed rather than guessing.
/// </remarks>
public static class StoryRewindConsequenceCatalog
{
    /// <summary>Every reviewed consequence, reversed ones first.</summary>
    public static IReadOnlyList<RewindConsequence> All { get; } =
    [
        new("flags", "Chapter and quest flags", "WorldSave_Facility.sav WorldFlags",
            RewindReversal.Reversed,
            "Forward chapter trigger flags, every quest step built on them, and every flag whose region opens later are removed.",
            "StoryFlagSync.PlanClearForwardFlags; FlagGateTests"),
        new("codex-world", "World-wide codex, email and journal unlocks", "WorldSave_MetaData.sav GlobalUnlocks",
            RewindReversal.Reversed,
            "Email, journal and compendium rows whose id maps to a later chapter are dropped. Item pickups, distilled items and recipes are not.",
            "CodexRevert.ClearForwardGlobalUnlocks; CodexRevertTests"),
        new("codex-player", "Per-player codex, email and journal lists", "Player_<id>.sav",
            RewindReversal.Reversed,
            "The same rows are dropped from each player's own read/discovered lists.",
            "CodexRevert (player half); CodexRevertTests"),
        new("respawn", "Respawn point", "Player_<id>.sav LastSafeWorldLocation / TerminalRespawnID",
            RewindReversal.Reversed,
            "Every player is moved back to the chapter's punch-card terminal so nobody loads in beyond the rewound story.",
            "PlayerRespawnRevert; RespawnTerminalCatalog"),

        new("doors", "Doors left open or unlocked", "WorldSave_<Region>.sav SimpleDoorMap / SecurityDoorMap",
            RewindReversal.NotReversed,
            "Door state, one-way-unlocked and open flags are runtime state keyed by level actor. A door a later chapter opened stays open even though the flag that opened it is cleared.",
            "WorldDoor; DoorGateResolver (level-asset link, needs the game install)"),
        new("npcs", "Story characters that were removed", "WorldSave_<Region>.sav NarrativeNPCMap IsDead / NarrativeState",
            RewindReversal.NotReversed,
            "The game's story scripts write these when the plot moves on (an NPC leaves, a scientist dies). Rewinding does not bring them back, and the meaning differs per character, so it is not a universal control.",
            "research-narrative-npcs.md; NarrativeNpcInspectorTests (all 20 dead entries are script-state)"),
        new("triggers", "One-shot triggers already fired", "WorldSave_<Region>.sav TriggerMap TimesTriggered",
            RewindReversal.NotReversed,
            "Trigger volumes remember how often they fired. A trigger that already ran will not run again after a rewind, so the scene it starts is skipped.",
            "TriggerMapFeature"),
        new("buttons", "Buttons and panels already used", "WorldSave_<Region>.sav ButtonMap",
            RewindReversal.NotReversed,
            "Pressed-once and activated state persists.",
            "ButtonMapFeature"),
        new("elevators", "Elevator positions", "WorldSave_<Region>.sav ElevatorMap TopOpen",
            RewindReversal.NotReversed,
            "An elevator left at the top stays there.",
            "ElevatorMapFeature"),
        new("portals", "Portals switched on", "WorldSave_<Region>.sav PortalMap PortalActive",
            RewindReversal.NotReversed,
            "An activated portal stays active.",
            "PortalMapFeature"),
        new("destructibles", "Broken barriers and props", "WorldSave_<Region>.sav DestructibleMap Broken; DecalMap Removed",
            RewindReversal.NotReversed,
            "Anything the story had you break stays broken.",
            "DestructibleMapFeature; DecalMapFeature"),
        new("spawns", "Enemy and NPC spawn history", "WorldSave_<Region>.sav NPCSpawnMap",
            RewindReversal.NotReversed,
            "HasSpawnedOnce and cooldowns persist, so encounters do not replay.",
            "NpcSpawnMapFeature"),
        new("loot", "Looted corpses, taken resources and dropped items", "CorpseMap; ResourceNodeMap; DroppedItemMap",
            RewindReversal.NotReversed,
            "Anything already taken stays taken.",
            "CorpseMapFeature; ResourceNodeMapFeature"),
        new("built", "Player-built and placed deployables", "DeployedObjectMap; VehicleMap; PetNPC",
            RewindReversal.NotReversed,
            "Bases, vehicles and pets are player-owned state and are not chapter-bound.",
            "WorldDeployable; WorldPet"),
        new("trams", "Tram positions", "WorldSave_Facility.sav TramMap LastStation",
            RewindReversal.NotReversed,
            "Trams stay parked where they were.",
            "TramMapFeature"),
        new("recipes-world", "World-wide unlocked recipes and pickups", "WorldSave_MetaData.sav GlobalUnlocks",
            RewindReversal.NotReversed,
            "Recipes unlocked and items picked up earlier stay unlocked; the rewind only trims codex rows.",
            "CodexRevert.GlobalCodexPrefixes (recipes and pickups are excluded on purpose)"),
        new("player-progress", "Player recipes, skills, inventory and quest rewards", "Player_<id>.sav",
            RewindReversal.NotReversed,
            "Items and knowledge a player earned are theirs; a rewind never takes them away.",
            "PlayerRespawnRevert touches position only"),
        new("containment", "Contained creatures", "WorldSave_MetaData.sav LeyakContainmentIDs",
            RewindReversal.NotReversed,
            "Containment records are left alone.",
            "WorldSaveReader ConsumedPrefixes"),
    ];

    /// <summary>The entries a rewind undoes.</summary>
    public static IEnumerable<RewindConsequence> Reversed => All.Where(c => c.Reversal == RewindReversal.Reversed);

    /// <summary>The entries a rewind leaves behind.</summary>
    public static IEnumerable<RewindConsequence> NotReversed => All.Where(c => c.Reversal == RewindReversal.NotReversed);

    // Region save token (WorldSave_ and Facility_ stripped, prefix match, longest first) -> the chapter
    // row at which that region opens. Rows come from FlagGate's area gates and the region names in
    // WorldAreaCatalog. Regions absent here are unattributed: reachable from the start, portal
    // vignettes with no fixed gate, or simply not asserted by any existing catalog.
    private static readonly (string Prefix, string ChapterRow)[] RegionOpens =
    [
        ("MFMines", "MFMines"),
        ("MFWest", "MF"),
        ("MFFoundry", "MF"),
        ("MFHQ", "MF"),
        ("MFMaggot", "MF"),
        ("Pens", "Pens"),
        ("Containment", "Containment"),
        ("Labs", "Labs"),
        ("Security", "PostLabs"),
        ("Dam", "EndSecurity"),
        ("Plant", "PowerServices"),
        ("DF_", "ReactorsEntry"),
        ("Residence", "Residence"),
        ("Fracture", "Fracture"),
        ("Botanical", "Botanical"),
    ];

    /// <summary>
    /// The chapter row at which a region save opens, or null when no existing catalog asserts one.
    /// <paramref name="regionToken"/> may be a file name (<c>WorldSave_Facility_Pens.sav</c>) or a
    /// bare token (<c>Facility_Pens</c>, <c>Pens</c>). The aggregate <c>Facility</c> level spans many
    /// chapters and always returns null.
    /// </summary>
    public static string? RegionOpensAtChapter(string? regionToken)
    {
        if (string.IsNullOrWhiteSpace(regionToken)) return null;
        var t = Path.GetFileNameWithoutExtension(regionToken.Trim());
        if (t.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase)) t = t["WorldSave_".Length..];
        if (t.StartsWith("Facility_", StringComparison.OrdinalIgnoreCase)) t = t["Facility_".Length..];

        foreach (var (prefix, row) in RegionOpens)
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return row;
        }
        return null;
    }
}
