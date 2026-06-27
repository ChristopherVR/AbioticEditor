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
            // The whole tree below is verified against the official wiki Objectives page plus
            // region walkthroughs (Office/Manufacturing/Labs/Security/Hydroplant). Genuinely
            // any-order steps are deliberately left out (the four random weather events, the three
            // electron pumps among themselves, the Security gates, the Dams pumps, the Dams
            // survivors) so the editor never writes a prerequisite the game doesn't enforce.

            // --- Office sector (a linear arrival sequence; the Office isn't region-gated) ---
            ["Office_CafeteriaQuestStarted"] = new[] { "Office_NewGameStarted" },
            ["Office_FirstCombatEncounter"] = new[] { "Office_CafeteriaQuestStarted" },
            // Cafeteria: repair the door to let Dr. Jager in - you can't complete a quest never started.
            ["Office_CafeteriaUnlocked"] = new[] { "Office_CafeteriaQuestStarted" },
            ["Office_TalkedToWarren"] = new[] { "Office_CafeteriaUnlocked" },
            ["Office_ReachedLobby"] = new[] { "Office_TalkedToWarren" },
            ["Office_ForkliftFound"] = new[] { "Office_TalkedToWarren" },
            ["Office_Silo3PortalOpened"] = new[] { "Office_Silo3Opened" },
            // Power Cells are recovered through the Flathill portal in Silo 3.
            ["Office_PowerCellFound"] = new[] { "Office_Silo3PortalOpened" },
            // The blast-door forklift opens once the forklift is found and a Power Cell is recovered.
            ["Office_ForkliftDoorOpened"] = new[] { "Office_ForkliftFound", "Office_PowerCellFound" },

            // --- Manufacturing (NPC chain Varsha -> Soldier -> Blacksmith; pumps after the 2nd trade) ---
            ["MF_MetVarsha"] = new[] { "MF_ManufacturingOpen" },
            ["MF_MetSoldier"] = new[] { "MF_MetVarsha" },
            // The second Blacksmith meeting (for pump parts) is after the train/mines run.
            ["MF_MetBlacksmith2"] = new[] { "MF_OpenTrainStation" },
            ["MF_RedPumpFixed"] = new[] { "MF_MetBlacksmith2" },
            ["MF_TealPumpFixed"] = new[] { "MF_MetBlacksmith2" },
            ["MF_YellowPumpFixed"] = new[] { "MF_MetBlacksmith2" },
            // "All three electron pumps repaired" requires each colour.
            ["MF_PumpsFixed"] = new[] { "MF_RedPumpFixed", "MF_TealPumpFixed", "MF_YellowPumpFixed" },

            // --- Cascade Labs (two early branches re-converge at Containment, then a linear spine) ---
            ["LABS_ExploredLabs"] = new[] { "LABS_EnteredLabs" },
            ["Labs_AbeJanetElectroPestScene"] = new[] { "LABS_EnteredLabs" },
            ["Labs_MetWitch"] = new[] { "Labs_AbeJanetElectroPestScene" },
            ["LABS_MetAbe"] = new[] { "Labs_MetWitch" },
            ["LABS_ElectroPests"] = new[] { "LABS_MetAbe" },
            ["Labs_Containment"] = new[] { "Labs_MiddleProgression" },
            ["Labs_DiracGoal"] = new[] { "Labs_Containment" },
            ["LABS_ActivateWarehouseLift"] = new[] { "Labs_DiracGoal" },
            ["LABS_ReachedCommandCenter"] = new[] { "LABS_ActivateWarehouseLift" },
            ["LABS_TurretsDeactivated"] = new[] { "LABS_ReachedCommandCenter" },
            // Anteverse 2 / Mycofields: reset security -> open the portal -> fix it -> complete it.
            ["LABS_OpenAnteversePortal"] = new[] { "LABS_TurretsDeactivated" },
            ["LABS_AnteverseBFixed"] = new[] { "LABS_OpenAnteversePortal" },
            ["LABS_CompletedAnteverseB"] = new[] { "LABS_AnteverseBFixed" },
            // The Vacuum Chamber door (region exit) opens after completing the Mycofields.
            ["LABS_OpenVacuumDoor"] = new[] { "LABS_CompletedAnteverseB" },
            // Tram stations: the Containment station unlocks during the Labs containment phase; the
            // Dam/Office station only after leaving the Labs (a Security + Hydroplant gap sits between).
            ["Tram_Containment"] = new[] { "Labs_Containment" },
            ["Tram_DamOffice"] = new[] { "Tram_Containment", "LABS_OpenVacuumDoor" },

            // --- Security (the numeric gate order is unverified, so each gate only needs region entry) ---
            ["Security_FirstEncounterFailsafe"] = new[] { "Security_Entered" },
            ["Security_Gate1Opened"] = new[] { "Security_Entered" },
            ["Security_Gate2Opened"] = new[] { "Security_Entered" },
            ["Security_Gate3Opened"] = new[] { "Security_Entered" },
            ["Security_Gate4Opened"] = new[] { "Security_Entered" },

            // --- Hydroplant / Dams (the pump order is unverified; all three gate the drain) ---
            ["Dams_DarkWaterDrained"] = new[] { "Dams_ActivatedPump1", "Dams_ActivatedPump2", "Dams_ActivatedPump3" },
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
