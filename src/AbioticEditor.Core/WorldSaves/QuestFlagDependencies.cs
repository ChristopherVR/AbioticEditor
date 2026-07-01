namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Curated per-quest flag dependencies that the coarse chapter model (<see cref="FlagGate"/>'s
/// linear chapter triggers + region-opening chapters) does not capture. A flag here requires the
/// listed flags to be set first, so the editor can offer to flip a whole quest's granular steps
/// instead of leaving a half-finished state (the classic symptom: a quest's "completed" flag is set
/// but its "started" flag isn't, so the in-game door/NPC the step controls never resolves).
///
/// <para>This same graph, walked in reverse by <see cref="FlagGate.DependentsOf"/>, is also how a
/// story rewind (<see cref="StoryFlagSync.ClearForwardFlags"/>) knows which granular flags to clear
/// alongside a chapter trigger: without it, rewinding past a chapter only cleared the 37 curated
/// trigger flags and left every granular step (and, at the very end, <c>End_MainStoryComplete</c>
/// itself) still set, so the game kept treating the save as finished.</para>
///
/// <para>Deliberately conservative: only steps that are definitionally true (you cannot COMPLETE a
/// quest you never STARTED) or confirmed from the wiki walkthrough / real completed-save flag dumps
/// are listed, so setting a flag can never fabricate an inconsistent world. Covers the full main
/// story spine from Office through the finale - <see cref="Consequences"/> records the physical
/// results (doors opened, NPCs killed) of a step for tools that can apply them.</para>
/// </summary>
public static class QuestFlagDependencies
{
    /// <summary>Direct (one-hop) prerequisite flags for a quest flag. Transitive expansion is done by
    /// <see cref="FlagGate.PrerequisitesFor"/> (forward) and <see cref="FlagGate.DependentsOf"/> (reverse).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Direct =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // The whole tree below is verified against the official wiki Objectives page, region
            // walkthroughs, and (for Power Services onward) the flag set of a real completed-game
            // save. Genuinely any-order steps are deliberately left out (the four random weather
            // events, the three electron pumps among themselves, the Security gates, the Dams
            // pumps, the Dams survivors) so the editor never writes a prerequisite the game doesn't
            // enforce. Side content (portal-world vignettes like V_Signal/Salem/NightRealm, ambient
            // MapReveal/Tram/Weather flags) is intentionally left unmodeled - it doesn't gate or
            // reflect main-story completion.

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

            // --- Power Services / Anteverse C (bloom is destroyed after the antifungal is made) ---
            ["Plant_AnteC_FoundWarhead"] = new[] { "Plant_AnteC_Entered" },
            ["Plant_FungusDestroyed"] = new[] { "Plant_AnteC_FoundWarhead" },
            ["Plant_ExitOpened"] = new[] { "Plant_FungusDestroyed" },

            // --- Reactors: Deep Field Labs (Dusk reactor; opens the chapter) ---
            ["Reactors_DFLabs_SapperCine"] = new[] { "Reactors_FirstContact" },
            ["Reactors_S1_Fixit"] = new[] { "Reactors_FirstContact" },
            ["Reactors_S1Labs_Complete"] = new[] { "Reactors_S1_Fixit" },

            // --- Reactors: Gale/Mist/Cloud stations (three independent repair sequences, each
            // gated only by S1 Labs being complete; genuinely any-order among themselves so only
            // each station's own internal chain is linked, matching the pumps/gates precedent) ---
            ["Reactors_SecondContact"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_ThirdContact"] = new[] { "Reactors_SecondContact" },
            // War station: a strictly numbered button sequence (ready -> pressed, next ready...).
            ["Reactors_S2War_Button1_Pressed"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S2War_Button2_Ready"] = new[] { "Reactors_S2War_Button1_Pressed" },
            ["Reactors_S2War_Button2_Pressed"] = new[] { "Reactors_S2War_Button2_Ready" },
            ["Reactors_S2War_Button3_Ready"] = new[] { "Reactors_S2War_Button2_Pressed" },
            ["Reactors_S2War_Button3_Pressed"] = new[] { "Reactors_S2War_Button3_Ready" },
            ["Reactors_S2War_Button4_Ready"] = new[] { "Reactors_S2War_Button3_Pressed" },
            ["Reactors_S2War_Complete"] = new[] { "Reactors_S2War_Button4_Ready" },
            ["Reactors_S2War_OrderLoss"] = new[] { "Reactors_S2War_Button1_Pressed" },
            ["Reactors_S2War_GKLoss"] = new[] { "Reactors_S2War_Button1_Pressed" },
            // Overgrowth station: three parallel repair points feed the station's "complete" flag.
            ["Reactors_S3_FixitA"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S3_FixitB"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S3_FixitC"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S3Overgrowth_Complete"] = new[] { "Reactors_S3_FixitA", "Reactors_S3_FixitB", "Reactors_S3_FixitC" },
            // RadWaste station: two parallel repair points feed the station's "complete" flag.
            ["Reactors_S4_FixitA"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S4_FixitB"] = new[] { "Reactors_S1Labs_Complete" },
            ["Reactors_S4RadWaste_Complete"] = new[] { "Reactors_S4_FixitA", "Reactors_S4_FixitB" },
            // All four reactors (Dusk from S1 Labs, Gale/Mist/Cloud from the three stations) online.
            ["Reactors_AllStations_Complete"] = new[]
            {
                "Reactors_S2War_Complete", "Reactors_S3Overgrowth_Complete", "Reactors_S4RadWaste_Complete",
            },
            ["Reactors_Central_ElectricityOff"] = new[] { "Reactors_AllStations_Complete" },
            ["Reactors_SG_Opened"] = new[] { "Reactors_AllStations_Complete" },

            // --- The Shadowgate: five containment devices ("cable breaks") gate the exit ---
            ["Reactors_SG_Entered"] = new[] { "Reactors_SG_Opened" },
            ["Reactors_SG_Interior"] = new[] { "Reactors_SG_Entered" },
            ["Reactors_SG_InteriorContact"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_CableBreak1"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_CableBreak2"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_CableBreak3"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_CableBreak4"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_CableBreak5"] = new[] { "Reactors_SG_Interior" },
            ["Reactors_SG_ExitOpened"] = new[]
            {
                "Reactors_SG_CableBreak1", "Reactors_SG_CableBreak2", "Reactors_SG_CableBreak3",
                "Reactors_SG_CableBreak4", "Reactors_SG_CableBreak5",
            },
            ["Reactors_SG_End"] = new[] { "Reactors_SG_ExitOpened" },

            // --- The Praetorium (Hasta's guidance leads to the Sun Disk) ---
            ["V_INQ_Entrance"] = new[] { "Reactors_SG_End" },
            ["V_INQ_HastaMet"] = new[] { "V_INQ_Entrance" },
            ["V_INQ_SunDiskTouched"] = new[] { "V_INQ_HastaMet" },

            // --- Residence Sector: storm suppressor (three parallel repairs, like the pumps) ---
            ["Residence_Entered"] = new[] { "V_INQ_SunDiskTouched" },
            ["Residence_AbeJanetMet"] = new[] { "Residence_Entered" },
            ["Res_HastaTria_PortalTriggered"] = new[] { "Residence_Entered" },
            ["Res_HastaTria_EndCutscene"] = new[] { "Res_HastaTria_PortalTriggered" },
            ["Res_Objective1_A"] = new[] { "Residence_AbeJanetMet" },
            ["Res_Objective1_B"] = new[] { "Residence_AbeJanetMet" },
            ["Res_Objective1_C"] = new[] { "Residence_AbeJanetMet" },
            ["Res_Objective1_Complete"] = new[] { "Res_Objective1_A", "Res_Objective1_B", "Res_Objective1_C" },
            ["Residence_IceWallRemoved"] = new[] { "Res_Objective1_Complete" },

            // --- Botanical Gardens / the Dark Lens wall (three miniboss "eyes" gate the way out) ---
            ["Res_EnteredBotanicals"] = new[] { "Residence_IceWallRemoved" },
            ["Residence_Wall"] = new[] { "Res_EnteredBotanicals" },
            ["Wall_AG_met"] = new[] { "Residence_Wall" },
            ["Residence_Kylie_Robot"] = new[] { "Residence_Wall" },
            ["Fracture_Forcefield_A"] = new[] { "Residence_Wall" },
            ["Fracture_Forcefield_B"] = new[] { "Residence_Wall" },
            ["Fracture_Forcefield_Complete"] = new[] { "Fracture_Forcefield_A", "Fracture_Forcefield_B" },
            ["Fracture_DL_Heavy_Eye_Destroyed"] = new[] { "Fracture_Forcefield_Complete" },
            ["Fracture_DL_Heavy_Dead"] = new[] { "Fracture_DL_Heavy_Eye_Destroyed" },
            ["Fracture_DL_Chieftain_Eye_Destroyed"] = new[] { "Fracture_Forcefield_Complete" },
            ["Fracture_DL_Chieftain_Dead"] = new[] { "Fracture_DL_Chieftain_Eye_Destroyed" },
            ["Fracture_DL_Witch_Eye_Destroyed"] = new[] { "Fracture_Forcefield_Complete" },
            ["Fracture_DL_Witch_Dead"] = new[] { "Fracture_DL_Witch_Eye_Destroyed" },
            ["Fracture_DL_AllEyes_Destroyed"] = new[]
            {
                "Fracture_DL_Heavy_Eye_Destroyed", "Fracture_DL_Chieftain_Eye_Destroyed", "Fracture_DL_Witch_Eye_Destroyed",
            },
            ["Residence_Fracture_Complete"] = new[] { "Fracture_DL_AllEyes_Destroyed" },

            // --- Finale: the boss fight and epilogue (the "is the game done" flags) ---
            ["End_Contact"] = new[] { "Residence_Fracture_Complete" },
            ["End_Boss_FightStartedOnce"] = new[] { "End_Contact" },
            ["EndBossPhase1Complete"] = new[] { "End_Boss_FightStartedOnce" },
            ["EndBossPhase2Complete"] = new[] { "EndBossPhase1Complete" },
            ["EndBossDefeated"] = new[] { "EndBossPhase2Complete" },
            ["End_PostBoss_MetWitch_Island"] = new[] { "EndBossDefeated" },
            ["End_PostBoss_MetCahn_Island"] = new[] { "EndBossDefeated" },
            ["End_PostBoss_MetRiggs_Island"] = new[] { "EndBossDefeated" },
            ["End_PostBoss_MetJanet_Island"] = new[] { "EndBossDefeated" },
            // The actual "main story complete" flag - not a DT_StoryProgression trigger itself, so
            // nothing cleared it on a chapter rewind before this table existed. Requiring every
            // epilogue conversation is the conservative choice: it can never be set without also
            // pulling in (and, on revert, clearing) the whole boss + epilogue chain above.
            ["End_MainStoryComplete"] = new[]
            {
                "End_PostBoss_MetWitch_Island", "End_PostBoss_MetCahn_Island",
                "End_PostBoss_MetRiggs_Island", "End_PostBoss_MetJanet_Island",
            },
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
