namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Curated per-quest flag dependencies that the coarse chapter model (<see cref="FlagGate"/>'s
/// linear chapter triggers + region-opening chapters) does not capture. A flag here requires the
/// listed flags to be set first, so the editor can offer to flip a whole quest's granular steps
/// instead of leaving a half-finished state (the classic symptom: a quest's "completed" flag is set
/// but its "started" flag isn't, so the in-game door/NPC the step controls never resolves).
///
/// <para>Deliberately conservative: only steps that are definitionally true (you cannot COMPLETE a
/// quest you never STARTED) or confirmed from the wiki walkthrough are listed, so setting a flag can
/// never fabricate an inconsistent world. It is seeded with the Office sector and is meant to grow
/// region by region as steps are verified - <see cref="Consequences"/> records the physical results
/// (doors opened, NPCs killed) of a step for tools that can apply them.</para>
/// </summary>
public static class QuestFlagDependencies
{
    /// <summary>Direct (one-hop) prerequisite flags for a quest flag. Transitive expansion is done by
    /// <see cref="FlagGate.PrerequisitesFor"/>.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Direct =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Office sector. Cafeteria: you let Dr. Jager in and repair the door; you cannot complete
            // ("Unlocked") a quest you never started.
            ["Office_CafeteriaUnlocked"] = new[] { "Office_CafeteriaQuestStarted" },
            // The blast-door forklift only opens once the forklift and its power cells are found.
            ["Office_ForkliftDoorOpened"] = new[] { "Office_ForkliftFound", "Office_PowerCellFound" },
        };

    /// <summary>The physical world-state a completed quest step leaves behind, for tools that apply
    /// consequences (open doors / mark NPCs dead). NPC and door entries are matched by the in-save
    /// actor's class/marker, since the exact GUID is per-save.</summary>
    public sealed record QuestConsequence(
        IReadOnlyList<string> NpcDeaths,   // NPC class/name markers that should read as dead
        IReadOnlyList<string> DoorsOpened, // door class/marker hints that should read as open
        string Note);

    public static readonly IReadOnlyDictionary<string, QuestConsequence> Consequences =
        new Dictionary<string, QuestConsequence>(StringComparer.OrdinalIgnoreCase)
        {
            // Repairing/opening the cafeteria door spawns an Anteverse-22 creature that kills Dr.
            // Jager; he is then found dead on the floor. So a save where this step is done should show
            // his door open AND Jager dead.
            ["Office_CafeteriaUnlocked"] = new QuestConsequence(
                NpcDeaths: new[] { "Jager" },
                DoorsOpened: new[] { "Cafeteria" },
                Note: "Opening the cafeteria door spawns an Anteverse-22 creature that kills Dr. Jager; "
                    + "he is left dead on the floor."),
        };

    /// <summary>Direct prerequisite flags of <paramref name="flag"/>, or empty when none are curated.</summary>
    public static IReadOnlyList<string> DirectPrerequisites(string flag)
        => Direct.TryGetValue(flag, out var v) ? v : System.Array.Empty<string>();
}
