using System.Text;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Coarse buckets for world-flag intent. Used by the UI to colour-code and
/// group the ~100 flags a typical save accrues.
/// </summary>
public enum FlagCategory
{
    /// <summary>Tutorial / "first time" prompts and TV tips.</summary>
    Tutorial,
    /// <summary>Quest progression - *Started, *Completed, etc.</summary>
    Quest,
    /// <summary>Map reveals, discoveries, sightings.</summary>
    Discovery,
    /// <summary>Doors, gates, areas being unlocked.</summary>
    Unlock,
    /// <summary>NPC introductions and meta-state.</summary>
    Meta,
    /// <summary>Anything that didn't match a heuristic.</summary>
    Other,
}

/// <summary>
/// Structured view over a single world-flag string.
/// </summary>
/// <param name="Name">Raw flag, e.g. <c>Office_NewGameStarted</c>.</param>
/// <param name="Area">
/// Best-guess area prefix (the underscore-delimited head, normalised to a
/// canonical casing). E.g. <c>Office</c>, <c>Labs</c>, <c>MF</c>. For flags
/// without an underscore the entire name is returned as the area.
/// </param>
/// <param name="Category">Heuristic category - see <see cref="FlagCategory"/>.</param>
/// <param name="FriendlyName">
/// Humanised label, e.g. <c>Office: New Game Started</c>. Built by splitting on
/// underscores and CamelCase then prepending the area as a prefix.
/// </param>
/// <param name="Description">
/// Curator-supplied longer description when we have one, otherwise <c>null</c>.
/// </param>
public sealed record FlagInfo(
    string Name,
    string Area,
    FlagCategory Category,
    string FriendlyName,
    string? Description);

/// <summary>
/// Catalog of AF world-flag heuristics. Live save fixtures across all 42 of the
/// game's per-region world saves yielded the 109-flag inventory the area/category
/// heuristics here were tuned against; see <c>DoorProbeTests</c> for the full
/// dump.
///
/// This is intentionally a pure-static catalog - the heuristics work on flag
/// strings alone and need no asset extraction.
/// </summary>
public static class QuestFlagCatalog
{
    /// <summary>
    /// Canonical area prefixes. Used both to normalise mixed-case observations
    /// (e.g. <c>LABS_*</c> and <c>Labs_*</c> both collapse to <c>Labs</c>) and
    /// as a published vocabulary for UI filters.
    /// </summary>
    private static readonly Dictionary<string, string> _areaAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DAMS"] = "Dams",
        ["LABS"] = "Labs",
        ["MFMines"] = "MFMines",
        ["MF"] = "MF",
        ["Office"] = "Office",
        ["Office1"] = "Office",
        ["Office1TVTip"] = "Office",
        ["Officedemoend"] = "Office",
        ["NightRealm"] = "NightRealm",
        ["MirrorWorld"] = "MirrorWorld",
        ["Pens"] = "Pens",
        ["Residence"] = "Residence",
        ["Rise"] = "Rise",
        ["Salem"] = "Salem",
        ["Security"] = "Security",
        ["Snowglobe"] = "Snowglobe",
        ["Tram"] = "Tram",
        ["Train"] = "Train",
        ["TrainCompletedOnce"] = "Train",
        ["Voussoir"] = "Voussoir",
        ["V_Signal"] = "VSignal",
        ["VSignal"] = "VSignal",
        ["Weather"] = "Weather",
        ["WeatherFog"] = "Weather",
        ["WeatherFogTriggeredOnce"] = "Weather",
        ["Fog"] = "Weather",
        ["Manufacturing"] = "Manufacturing",
        ["MapReveal"] = "MapReveal",
        ["Grayson"] = "NPC",
        ["MetChefTrader"] = "NPC",
        ["H_Japan_Z"] = "HJapan",
        ["H"] = "HJapan",
        ["Labs_Containment"] = "Labs",
        // Round 91: the game's full flag table (tests/fixtures/world-flag-table.txt) brought
        // the later regions in. "Res_*" and "Residence_*" are one area; the end-game boss
        // flags have no underscore ("EndBossDefeated") so the whole-name prefix rule maps
        // them; V_INQ (Hasta's inquisition, in Voussoir) is a two-underscore prefix like V_Signal.
        ["Res"] = "Residence",
        ["EndBoss"] = "End",
        ["V_INQ"] = "Voussoir",
        ["KylieVocalizer"] = "NPC",
    };

    private static readonly string[] _knownAreas =
    {
        "Office", "Labs", "Dams", "MF", "MFMines", "Manufacturing", "MapReveal",
        "NightRealm", "MirrorWorld", "Pens", "Residence", "Rise", "Salem",
        "Security", "Snowglobe", "Tram", "Train", "Voussoir", "VSignal",
        "Weather", "HJapan", "NPC",
        "Plant", "Reactors", "Fracture", "End", "Intro", "Tile", "Wall", "Flathill", "VWinter",
    };

    /// <summary>Canonical area vocabulary as a stable, sorted list.</summary>
    public static IReadOnlyList<string> KnownAreas { get; } =
        _knownAreas.OrderBy(s => s, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every world-flag observed across the 42 shipped <c>WorldSave_*.sav</c>
    /// fixtures (a fully-finished save plus per-region empties). Used by the UI
    /// to render "active vs missing/inactive" rather than only the flags
    /// currently set on the loaded save. Sorted ordinally; one entry per line
    /// to keep diffs human-scannable.
    ///
    /// Refreshed by <c>AllFlagsProbe.DumpUnionOfAllWorldFlags</c> - paste from
    /// <c>%TEMP%/abiotic-editor-schema/all-flags.txt</c> after running it
    /// against new fixtures. Since round 91 it also carries every row of the game's own
    /// world-flag table as a running game reports it (<c>tests/fixtures/world-flag-table.txt</c>,
    /// refreshed from live editing's <c>flags.list</c>); a test keeps the two in step, so a
    /// flag the game has but this list lacks fails the build instead of silently missing
    /// from the offline flag screens.
    /// </summary>
    public static IReadOnlyList<string> KnownFlags { get; } = new[]
    {
        "91Contained",
        "Dams_ActivatedPump1",
        "Dams_ActivatedPump2",
        "Dams_ActivatedPump3",
        "Dams_ActivatedPump4",
        "Dams_ActivateFastTravel",
        "Dams_AllPumpsActivated",
        "DAMS_CompletedVoussoir",
        "Dams_DarkWaterDrained",
        "Dams_KylieUploaded",
        "Dams_MetElwyn",
        "Dams_MetIsaiah",
        "Dams_MetKylie",
        "Dams_MetSwimInstructor",
        "Dams_MetWitch",
        "Dams_NewmanAfterElectricalStation",
        "Dams_NewmanFinishedTyping",
        "Dams_ReachedCentral",
        "Dams_SpillwayOpen",
        "DemoNeverSet",
        "End_Boss_FightStartedOnce",
        "End_CahnDisappears",
        "End_Contact",
        "End_Entered",
        "End_FightStarted",
        "End_MainStoryComplete",
        "End_PostBoss_MetCahn_Island",
        "End_PostBoss_MetJanet_Island",
        "End_PostBoss_MetRiggs_Island",
        "End_PostBoss_MetWitch_Island",
        "EndBossDefeated",
        "EndBossPhase1Complete",
        "EndBossPhase2Complete",
        "Flathill_CornWeb",
        "Fog_Completed",
        "Fracture_DL_AllEyes_Destroyed",
        "Fracture_DL_Chieftain_Dead",
        "Fracture_DL_Chieftain_Eye_Destroyed",
        "Fracture_DL_Heavy_Dead",
        "Fracture_DL_Heavy_Eye_Destroyed",
        "Fracture_DL_Witch_Dead",
        "Fracture_DL_Witch_Eye_Destroyed",
        "Fracture_Forcefield_A",
        "Fracture_Forcefield_B",
        "Fracture_Forcefield_Complete",
        "Grayson_Bandaged",
        "Grayson_Wandering",
        "H_Garden_A",
        "H_Garden_B",
        "H_Garden_C",
        "H_Japan_Z",
        "Intro_Driver_Arrived",
        "Intro_Driver_Exit",
        "Intro_Driver_FinishedTalking",
        "Intro_Driver_Talks",
        "Intro_PickedUpGATEPal",
        "KylieVocalizer_Activated",
        "Labs_AbeJanetElectroPestScene",
        "LABS_ActivateWarehouseLift",
        "LABS_AdjustWingTape_01",
        "LABS_AnteverseBFixed",
        "LABS_CompletedAnteverseB",
        "Labs_Containment_Entered",
        "Labs_DiracGoal",
        "LABS_ElectroPests",
        "LABS_EnteredLabs",
        "LABS_ExploredLabs",
        "Labs_Helmholtz_Opened",
        "LABS_Met_Thule",
        "LABS_MetAbe",
        "LABS_MetCahn_01",
        "LABS_MetCahn_02",
        "LABS_MetJanet",
        "Labs_MetWitch",
        "Labs_MiddleProgression",
        "LABS_OpenAnteversePortal",
        "LABS_OpenVacuumDoor",
        "LABS_ReachedCommandCenter",
        "LABS_RecorderPortalConnections",
        "LABS_TurretsDeactivated",
        "Labs_XRay_Fixit1",
        "Labs_XRay_Fixit2",
        "Labs_XRay_Fixit3",
        "Labs_XRay_Fixit4",
        "Labs_XRay_FixitAll",
        "Manufacturing_Arrived",
        "MapReveal_AnteverseA",
        "MapReveal_AnteverseB",
        "MapReveal_Dams",
        "MapReveal_Flathill",
        "MapReveal_Labs",
        "MapReveal_Manufacturing",
        "MapReveal_Reactor",
        "MapReveal_Residence",
        "MapReveal_Security",
        "MapReveal_Shadowgate",
        "MapReveal_Voussoir",
        "MapReveal_Wall",
        "MapReveal_Warehouse",
        "MetChefTrader",
        "MF_ExitOpened",
        "MF_ManufacturingOpen",
        "MF_MetBlacksmith",
        "MF_MetBlacksmith2",
        "MF_MetFrake",
        "MF_MetSoldier",
        "MF_MetVarsha",
        "MF_OpenTrainStation",
        "MF_PumpsFixed",
        "MF_RedPumpFixed",
        "MF_TealPumpFixed",
        "MF_YellowPumpFixed",
        "MFMines_Entered",
        "MFMines_Mgt_Explored",
        "MFMines_Mgt_NythKilledOnce",
        "MFMines_Mgt_Restored",
        "MirrorWorld_Entered",
        "NightRealm_Entered",
        "NightRealm_LakeVisitor",
        "NightRealm_ShadowA_Destroyed",
        "NightRealm_ShadowB_Destroyed",
        "NightRealm_ShadowC_Destroyed",
        "NightRealm_ShadowD_Destroyed",
        "Office1TVTip",
        "Office3_Silo2_TVTip",
        "Office4_SecurityDoorTimerFix",
        "Office4_TunnelFixit",
        "Office_CafeteriaQuestStarted",
        "Office_CafeteriaUnlocked",
        "Office_FirstCombatEncounter",
        "Office_ForkliftDoorOpened",
        "Office_ForkliftFound",
        "Office_InformationFound",
        "Office_NewGameStarted",
        "Office_PowerCellFound",
        "Office_ReachedLobby",
        "Office_Silo3Opened",
        "Office_Silo3PortalOpened",
        "Office_TalkedToWarren",
        "Office_ThirdFloorReached",
        "OfficeDemoEnd",
        "Pens_OpenTramStation",
        "Plant_AnteC_Entered",
        "Plant_AnteC_FoundWarhead",
        "Plant_EnteredPlant",
        "Plant_ExitOpened",
        "Plant_FungusDestroyed",
        "Plant_MetWaterBotA",
        "Plant_TramKeyUsed",
        "Reactors_AllStations_Complete",
        "Reactors_Central_ElectricityOff",
        "Reactors_DFLabs_SapperCine",
        "Reactors_FirstContact",
        "Reactors_S1_Fixit",
        "Reactors_S1Labs_Complete",
        "Reactors_S2_Fixit",
        "Reactors_S2War_Button1_Pressed",
        "Reactors_S2War_Button2_Pressed",
        "Reactors_S2War_Button2_Ready",
        "Reactors_S2War_Button3_Pressed",
        "Reactors_S2War_Button3_Ready",
        "Reactors_S2War_Button4_Ready",
        "Reactors_S2War_Complete",
        "Reactors_S2War_GKLoss",
        "Reactors_S2War_OrderLoss",
        "Reactors_S3_FixitA",
        "Reactors_S3_FixitB",
        "Reactors_S3_FixitC",
        "Reactors_S3Overgrowth_Complete",
        "Reactors_S4_FixitA",
        "Reactors_S4_FixitB",
        "Reactors_S4RadWaste_Complete",
        "Reactors_SecondContact",
        "Reactors_SG_CableBreak1",
        "Reactors_SG_CableBreak2",
        "Reactors_SG_CableBreak3",
        "Reactors_SG_CableBreak4",
        "Reactors_SG_CableBreak5",
        "Reactors_SG_End",
        "Reactors_SG_Entered",
        "Reactors_SG_ExitOpened",
        "Reactors_SG_Interior",
        "Reactors_SG_InteriorContact",
        "Reactors_SG_Opened",
        "Reactors_ThirdContact",
        "Res_EnteredBotanicals",
        "Res_HastaTria_EndCutscene",
        "Res_HastaTria_PortalTriggered",
        "Res_Objective1_A",
        "Res_Objective1_B",
        "Res_Objective1_C",
        "Res_Objective1_Complete",
        "Residence_AbeJanetMet",
        "Residence_Entered",
        "Residence_Fracture_Complete",
        "Residence_IceWallRemoved",
        "Residence_Kylie_Destroy",
        "Residence_Kylie_Robot",
        "Residence_Kylie_Upload",
        "Residence_TramDeadGK",
        "Residence_TramLeft",
        "Residence_Wall",
        "Rise_Opened",
        "Salem_Complete",
        "Salem_MetRiggs",
        "Salem_Opened",
        "Salem_Pens_FixitL",
        "Salem_Pens_FixitR",
        "Security_Entered",
        "Security_ExitOpened",
        "Security_FirstEncounterFailsafe",
        "Security_Gate1Opened",
        "Security_Gate2Opened",
        "Security_Gate3Opened",
        "Security_Gate4Opened",
        "Security_MetCeline",
        "Security_SurfaceElevatorEvent",
        "SnowglobeComplete",
        "Tile_Entered",
        "TrainCompletedOnce",
        "Tram_Containment",
        "Tram_DamOffice",
        "Tram_DF_R4",
        "Tram_MFWest_Office1",
        "Tram_Mines_Office1",
        "Tram_Plant",
        "V_INQ_Entrance",
        "V_INQ_HastaMet",
        "V_INQ_SunDiskTouched",
        "V_Signal_AbductionStarted",
        "V_Signal_Complete",
        "V_Signal_DisabledJammer",
        "V_Signal_DisabledJammer_TalkFinished",
        "V_Signal_EncounteredHudson",
        "V_Signal_Entered",
        "V_Signal_FixedDish",
        "V_Signal_FixedDish_TalkFinished",
        "V_Signal_FixedFinalDish",
        "V_Signal_FixedSubstation",
        "V_Signal_ListenedToSignal",
        "V_Signal_PortalOpened",
        "V_Signal_ReachedRadar",
        "V_Signal_ReachedSubstation",
        "V_Signal_RevealedJammer",
        "V_Signal_StartDoorDestroyed",
        "V_Signal_SunOfficeExplored",
        "V_Signal_SunOfficeUnlocked",
        "Voussoir_Entered",
        "VWinter_Complete",
        "VWinter_LamogiCave",
        "Wall_AG_met",
        "Weather_BlackFogTriggered",
        "Weather_BlackoutTriggered",
        "Weather_ColdSnapTriggered",
        "Weather_RadLeakTriggered",
        "Weather_SporesTriggered",
        "WeatherFogTriggeredOnce",
    };

    /// <summary>
    /// <see cref="KnownFlags"/> grouped by <see cref="FlagInfo.Area"/> - handy
    /// for UI lists that want to render an "all flags in this region" panel
    /// without re-classifying on every frame. Areas are sorted ordinally; flags
    /// within each area preserve the sorted order of <see cref="KnownFlags"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> FlagsByArea { get; } =
        KnownFlags
            .GroupBy(f => Lookup(f).Area)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.ToArray(),
                StringComparer.Ordinal);

    /// <summary>
    /// Parses <paramref name="rawFlag"/> into a structured <see cref="FlagInfo"/>.
    /// Always returns a non-null result; unknown flags simply land in
    /// <see cref="FlagCategory.Other"/>.
    /// </summary>
    public static FlagInfo Lookup(string rawFlag)
    {
        if (string.IsNullOrEmpty(rawFlag))
        {
            return new FlagInfo(rawFlag ?? string.Empty, string.Empty, FlagCategory.Other, string.Empty, null);
        }

        var (area, remainder) = SplitArea(rawFlag);
        var category = ClassifyCategory(rawFlag, remainder);
        var friendly = BuildFriendlyName(area, remainder, rawFlag);
        return new FlagInfo(rawFlag, area, category, friendly, null);
    }

    // ---------- internals ----------

    private static (string Area, string Remainder) SplitArea(string raw)
    {
        // Two-underscore prefixes like "V_Signal_*" or "H_Japan_*" need to be
        // recognised before we naively split on the first underscore.
        foreach (var compound in new[] { "V_Signal", "H_Japan", "Labs_Containment", "V_INQ" })
        {
            if (raw.StartsWith(compound + "_", StringComparison.OrdinalIgnoreCase))
            {
                var canon = _areaAliases.TryGetValue(compound, out var c) ? c : compound;
                return (canon, raw[(compound.Length + 1)..]);
            }
            if (raw.Equals(compound, StringComparison.OrdinalIgnoreCase))
            {
                var canon = _areaAliases.TryGetValue(compound, out var c) ? c : compound;
                return (canon, string.Empty);
            }
        }

        var us = raw.IndexOf('_');
        if (us <= 0)
        {
            // No underscore - try whole-name aliases (e.g. "MetChefTrader",
            // "OfficeDemoEnd"), otherwise treat the entire flag as the area.
            if (_areaAliases.TryGetValue(raw, out var alias))
            {
                return (alias, raw);
            }
            // Special-case "Office1TVTip", "TrainCompletedOnce", etc. which
            // don't have an underscore separator.
            foreach (var (key, canon) in _areaAliases)
            {
                if (raw.StartsWith(key, StringComparison.OrdinalIgnoreCase) && raw.Length > key.Length)
                {
                    return (canon, raw[key.Length..]);
                }
            }
            return (raw, string.Empty);
        }

        var prefix = raw[..us];
        var rest = raw[(us + 1)..];
        var canonArea = _areaAliases.TryGetValue(prefix, out var a) ? a : Capitalise(prefix);
        return (canonArea, rest);
    }

    private static FlagCategory ClassifyCategory(string raw, string remainder)
    {
        // Pre-tutorial keywords win even if the suffix looks like "Started".
        if (ContainsAny(raw, "Tutorial", "TVTip", "Hint", "FirstTime") ||
            raw.EndsWith("NewGameStarted", StringComparison.OrdinalIgnoreCase))
        {
            return FlagCategory.Tutorial;
        }

        if (HasSuffix(raw, "Started", "Begun"))                              return FlagCategory.Quest;
        if (HasSuffix(raw, "Completed", "Complete", "Done", "Finished"))     return FlagCategory.Quest;

        // Order matters - "ExitOpened" and "PortalOpened" feel like Unlock to
        // a player even though "Opened" is a generic verb.
        if (HasSuffix(raw, "Unlocked", "Opened", "Activated", "Bandaged", "Triggered", "Fixed", "Drained", "Restored", "Destroyed", "Deactivated") ||
            ContainsAny(raw, "Reveal", "Pump", "Fixit", "OpenAnteverse", "OpenVacuumDoor", "ExitOpened", "MapReveal"))
        {
            return FlagCategory.Unlock;
        }

        if (ContainsAny(raw, "HasMet", "Met") && !ContainsAny(raw, "Metal"))
        {
            return FlagCategory.Meta;
        }

        if (HasSuffix(raw, "Discovered", "Found", "Seen", "Entered", "Reached", "Explored", "Wandering") ||
            ContainsAny(raw, "Reached", "Entered", "Explored"))
        {
            return FlagCategory.Discovery;
        }

        return FlagCategory.Other;
    }

    private static bool HasSuffix(string raw, params string[] suffixes)
    {
        foreach (var s in suffixes)
        {
            if (raw.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool ContainsAny(string raw, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (raw.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string BuildFriendlyName(string area, string remainder, string raw)
    {
        var body = string.IsNullOrEmpty(remainder) ? raw : remainder;
        var humanised = Humanise(body);
        if (string.IsNullOrEmpty(area)) return humanised;
        if (humanised.Equals(area, StringComparison.OrdinalIgnoreCase)) return area;
        return $"{area}: {humanised}";
    }

    private static string Humanise(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // First split on underscores, then within each chunk split on CamelCase
        // boundaries - UPPER followed by lower, or lower followed by UPPER.
        var pieces = s.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(SplitCamelCase)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(Capitalise);
        return string.Join(' ', pieces);
    }

    private static IEnumerable<string> SplitCamelCase(string word)
    {
        if (string.IsNullOrEmpty(word)) yield break;
        var sb = new StringBuilder();
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(word[i - 1]) ||
                (i + 1 < word.Length && char.IsLower(word[i + 1]))))
            {
                yield return sb.ToString();
                sb.Clear();
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string Capitalise(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        // Preserve all-caps tokens (e.g. "TV", "MF").
        if (word.Length <= 2 && word.All(char.IsUpper)) return word;
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}
