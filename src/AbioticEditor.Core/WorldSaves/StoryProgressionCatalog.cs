namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One main-quest chapter: the DT_StoryProgression row plus a human-friendly label.
/// Titles/summaries are editor-authored from the wiki's objectives walkthrough
/// (see docs/research-gatepal-quests.md §3) - the game table itself carries only the
/// trigger flag, region text, area count and a card-art texture.
/// </summary>
/// <param name="CardArt">Game asset path of the chapter card (ServerBrowser/map_*).</param>
public sealed record StoryChapter(
    string Row,
    string Title,
    string? Summary,
    string? TriggerFlag = null,
    string? CardArt = null);

/// <summary>
/// The 37 main-quest chapters in <c>DT_StoryProgression</c>, in table (= story) order.
/// <c>WorldSave_MetaData.sav</c> stores the current chapter in its
/// <c>StoryProgressionRow</c> NameProperty.
/// </summary>
public static class StoryProgressionCatalog
{
    private const string Art = "/Game/Textures/GUI/ServerBrowser/";

    public static IReadOnlyList<StoryChapter> Chapters { get; } = new[]
    {
        new StoryChapter("Office", "Office Sector - Arrival", "Containment breach day. You wake up in the Office Sector, scavenge your first tools, let Dr. Jager into the cafeteria and report to sector security officer Warren.", "Office_NewGameStarted", Art + "map_office1"),
        new StoryChapter("Office2", "Office Sector - Silo 3", "The only way out is through Manufacturing - but a forklift holding the blast door needs Power Cells. Grayson points you to the energy research team.", "Office_InformationFound", Art + "map_office2"),
        new StoryChapter("Office3", "Office Sector - Tram Restored", "You reach the Office Sector's third floor and convince Archie Roberts (or Regal) to open Silo 3, where the Power Cells were taken.", "Office_ThirdFloorReached", Art + "map_office3"),
        new StoryChapter("Flathill", "Portal World: Flathill", "Silo 3 is open. All signs point through the portal inside: step into Flathill, a fog-drowned suburban anomaly, to recover Power Cells.", "Office_Silo3Opened", Art + "map_office3"),
        new StoryChapter("PostFlathill", "Back from Flathill", "You survived Flathill and can now farm Power Cells there. Power the forklift and raise the blast door into Manufacturing.", "Fog_Completed", Art + "map_office3"),
        new StoryChapter("MF", "Manufacturing West", "Manufacturing West is open. Somewhere in this sector is a tunnel to the surface - explore and find your way out.", "MF_ManufacturingOpen", Art + "map_MFStart"),
        new StoryChapter("MFBlacksmith", "Manufacturing - The Blacksmith", "You meet the Blacksmith (Varsha), who knows the sector: the Surface Tunnel is the main heavy-traffic route out.", "MF_MetBlacksmith", Art + "map_MFStart"),
        new StoryChapter("MFMines", "Manufacturing - The Mines", "The Surface Tunnel is blocked. You descend into the mines beneath Manufacturing to find Frake, who may know another way.", "MFMines_Entered", Art + "map_MFStart"),
        new StoryChapter("MFFrake", "Manufacturing - Frake", "You found Frake. The plan: get the Blacksmith's help forging parts to repair the sector's three electron pumps.", "MF_MetFrake", Art + "map_MFStart"),
        new StoryChapter("MFTrain", "Manufacturing - Train Station", "The train station is open, connecting Manufacturing's far reaches while you gather pump materials.", "MF_OpenTrainStation", Art + "map_MFStart"),
        new StoryChapter("MFPumpsFixed", "Manufacturing - Electron Pumps Repaired", "All three electron pumps (yellow, green, red) are repaired. Time to activate the Synchrotron and overload it to blast an exit.", "MF_PumpsFixed", Art + "map_MFStart"),
        new StoryChapter("Pens", "The Pens", "The synchrotron overload opened the way out of Manufacturing. You emerge into Cascade Laboratories through the holding pens.", "MF_ExitOpened", Art + "map_labs"),
        new StoryChapter("Labs", "Cascade Laboratories", "Explore the Labs Sector for a way out; survivors suggest the Inner Wing containment blocks hold what you need.", "Labs_MiddleProgression", Art + "map_labs"),
        new StoryChapter("Containment", "Labs - Containment", "You're inside the Inner Wing containment blocks - deadly experiments, turrets, and the route toward the Control Center.", "Labs_Containment_Entered", Art + "map_labs"),
        new StoryChapter("Helmholtz", "Labs - Helmholtz", "The Helmholtz Wing is open, the path to the Control Center where the security system (and its turrets) can be reset.", "Labs_Helmholtz_Opened", Art + "map_labs"),
        new StoryChapter("Tarasque", "Labs - Control Center", "Via the Furniture Store anomaly you reached the Control Center and reset security - the turrets are off (and the Tarasque met).", "LABS_TurretsDeactivated", Art + "map_labs"),
        new StoryChapter("Mycofields", "Portal World: The Mycofields", "The Anteverse 2 portal is fixed: gather chemicals in the Mycofields' Shroom zone to make Antethermite for the Vacuum Chamber door.", "LABS_AnteverseBFixed", Art + "map_labs"),
        new StoryChapter("PostLabs", "Into the Security Sector", "The vacuum door is breached - you infiltrate the Security Sector, aiming for the Large Surface Elevator, one of the facility's most secure exits.", "LABS_OpenVacuumDoor", Art + "map_security1"),
        new StoryChapter("SecSurfaceElevator", "Security - Surface Elevator", "The surface elevator crashes catastrophically. Stranded, you explore Canaan for ingredients and open the remaining gates to escape the sector.", "Security_SurfaceElevatorEvent", Art + "map_security2a"),
        new StoryChapter("EndSecurity", "Down to the Hydroplant", "The Security Sector exit is open; there's only one way forward - down into the Hydroplant.", "Security_ExitOpened", Art + "map_dam1"),
        new StoryChapter("ElectricalStation", "Hydroplant - Electrical Station", "You reach the central dam. Survivors in the Electrical Station (Jonas) can help - reactivate the station, reboot the spillway computer, and open the flow controls.", "Dams_ReachedCentral", Art + "map_dam2"),
        new StoryChapter("Voussoir", "Portal World: Voussoir", "You step through to Voussoir, the drowned cathedral anteverse reachable from the Hydroplant.", "Voussoir_Entered", Art + "map_dam3b"),
        new StoryChapter("EndDam", "Hydroplant - Spillway Open", "The spillway is open. The only way out of the Hydroplant is wet: ride the water down toward the Reactors.", "Dams_SpillwayOpen", Art + "Map_Dam4"),
        new StoryChapter("PowerServices", "Power Services", "You enter Power Services. A massive fungal bloom blocks the way to the Reactors - you need something to kill it.", "Plant_EnteredPlant", Art + "map_powerservices"),
        new StoryChapter("AnteverseC", "Anteverse C - The Far Garden", "You enter Anteverse C (the Far Garden) to gather what's needed for Anti-Fungal Gelatin.", "Plant_AnteC_Entered", Art + "map_anteversec"),
        new StoryChapter("ReactorsEntry", "Reactors - Entry", "The bloom is destroyed and the way into the Reactor Sector is open - you hope that was the right thing to do.", "Plant_ExitOpened", Art + "map_reactorsentry"),
        new StoryChapter("Reactors1Labs", "Reactors - Deep Field Labs", "First contact in the Deep Field labs: Dr. Cahn explains the Gatekeeper's exit waygate needs four reactors online. Activate Dusk Reactor and report back.", "Reactors_FirstContact", Art + "map_dflabs"),
        new StoryChapter("ReactorsAll", "Reactors - All Four Online", "The S1 labs are searched (Cloud Reactor explored). Now bring Gale, Mist and Cloud Reactors online to join Dusk.", "Reactors_S1Labs_Complete", Art + "map_radwaste"),
        new StoryChapter("Shadowgate", "The Shadowgate", "All four reactors power the Fusion Generator's central waygate: the Shadowgate. Inside the mirrored Intrados facility, destroy the five containment devices and expel the entity.", "Reactors_SG_Opened", Art + "map_shadowgate"),
        new StoryChapter("InqEnd", "The Praetorium", "The creature fled through a massive rift. Beyond it lies somewhere immense and old - with Hasta's guidance, enter the Praetorium (IS-0101) and locate the Sun Disk.", "Reactors_SG_End", Art + "map_inq2"),
        new StoryChapter("Residence", "Residence Sector", "The Sun Disk's gift lets you endure the cold at last: enter the frozen Residence Sector.", "V_INQ_SunDiskTouched", Art + "map_residence1"),
        new StoryChapter("Residence2", "Residence - Storm Suppressor", "With Abe and Janet's help the storm suppressor is fixed; the upper floor is searchable, but a frozen ice wall bars the way ahead.", "Res_Objective1_Complete", Art + "map_residence2"),
        new StoryChapter("Fracture", "The Fracture", "An intense heat source melted the ice wall - you push into the Fracture, hunting the Dark Lens that can get everyone out.", "Residence_IceWallRemoved", Art + "map_fracture1"),
        new StoryChapter("Botanical", "Botanical Gardens", "You enter the Botanical Gardens, the overgrown heart of the Residence Sector's upper reaches.", "Res_EnteredBotanicals", Art + "map_botanical"),
        new StoryChapter("DarkLens", "The Dark Lens", "Past the wall lies the Dark Lens. Collect its fragments, face The Fallow, and open the way out of the facility at last.", "Residence_Wall", Art + "map_fracture2"),
        new StoryChapter("SouthIsland", "South Island", "The Fracture is behind you: you arrive at the South Island, whose altar can supposedly take you anywhere - Dr. Cahn and Thule have thoughts.", "Residence_Fracture_Complete", Art + "map_southisland"),
        new StoryChapter("EndGame", "Finale - Facility Escape", "The Wayseeker is defeated. The end? Talk to Dr. Cahn, Janet, and the Sister of the Unlost. Main story complete.", "EndBossDefeated", Art + "map_endgame"),
    };

    public static IReadOnlyList<string> Rows { get; } = Chapters.Select(c => c.Row).ToList();

    // O(1) lookups - IndexOf/ChapterForFlag sit on hot UI paths (flag-list rebuilds call
    // them once per flag), where the previous linear scans (and the per-call ToList in
    // IndexOf) showed up as allocation churn.
    private static readonly Dictionary<string, int> RowIndex = Chapters
        .Select((c, i) => (c.Row, Index: i))
        .ToDictionary(t => t.Row, t => t.Index, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, StoryChapter> ChapterByTriggerFlag = Chapters
        .Where(c => c.TriggerFlag is not null)
        .ToDictionary(c => c.TriggerFlag!, StringComparer.OrdinalIgnoreCase);

    public static int IndexOf(string? row)
        => row is not null && RowIndex.TryGetValue(row, out var i) ? i : -1;

    public static StoryChapter? Find(string? row)
    {
        var i = IndexOf(row);
        return i < 0 ? null : Chapters[i];
    }

    /// <summary>
    /// If <paramref name="flag"/> is the trigger flag of a chapter (per
    /// DT_StoryProgression's WorldFlag column), returns that chapter.
    /// </summary>
    public static StoryChapter? ChapterForFlag(string flag)
        => flag is not null && ChapterByTriggerFlag.TryGetValue(flag, out var c) ? c : null;
}
