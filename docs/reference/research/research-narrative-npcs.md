# Research: `NarrativeNPCMap` - what the NPCS tab is actually editing

**Date:** 2026-06-11 · **Probe:** `tests/AbioticEditor.Tests/NarrativeNpcProbeTests.cs`

## Verdict

A `NarrativeNPCMap` entry is the persisted state of one **map-placed narrative actor**
(story NPC, hologram, or trader spawn slot) - keyed by its level actor path, not by
character identity. Across all 27 fixture world saves with entries (121 total), every
`CustomName` is empty, every `Location` is `(0,0,0)`, every `CurrentHealthMap` is empty,
and every `NPCClass` is `None` - only `IsDead` and `NarrativeState` ever carry data.

**`IsDead = true` is mostly written by the game's own story scripting, not by player
violence.** Most narrative NPCs are unkillable by players (wiki + Steam discussions:
"You cannot kill him, as with most NPCs in the game"), but the game's
`Trigger_NarrativeNPCUpdate` actor has explicit `KillNPC?` / `DeleteNPC?` options that
story triggers use to remove NPCs when the plot moves on (e.g. Abe & Janet leaving an
area, the dying scientist at the start of the Office Sector). A "dead" entry is therefore
usually *"the story removed this actor"*, occasionally *"the players killed a killable
one"* (a `NarrativeNPC_Human_Killable` variant and creature NPCs like Ela exist).

**Editor consequence:** a "mark as dead" checkbox is the wrong affordance. Killing a
story NPC from the editor reproduces something players can rarely even do in-game and
can only break things. The one user-meaningful action is the reverse: **revive** an
entry that is already dead (with a caveat that reviving story-scripted deaths may
resurrect an NPC the plot intentionally removed).

## Fixture dump (Cascade saves, 2026-06)

121 entries across 27 of the 43 world saves. Aggregate:

| Actor class (path tail) | Count | What it is |
|---|---|---|
| `NarrativeNPC_Human_Hologram_C_*` | 62 | Story holograms (incl. Order of Reason variants `ORD_Artem`, `ORD_Interfector` in the cooked data). Non-interactive, unkillable. |
| `NarrativeNPC_Human_ParentBP_C_*` | 53 | Generic placed human story NPC (scientists, Abe & Janet appearances, wandering-trader hosts). |
| `NarrativeNPC_Human_TRADER_C_*` | 2 | Static trader actors - both in `Facility_MFWest` (the Blacksmith's F.O.R.G.E. area). |
| `NarrativeNPC_Ela_C_1` | 1 | Ela, Abe's tamed Electro-Pest (Wildlife Pens). |
| `NarrativeNPC_HastaTria_C_1` | 1 | Hasta Tria, the non-hostile Order Lab Rat at the collapsed Surface Tunnel (Mines). |
| `NarrativeNPC_Larva_C_1` | 1 | Big Hive Larva trader (the Encroachment / `Facility_MFMaggot`). |
| `NarrativeNPC_MGT_CKCore_C_1` | 1 | The Core Keeper crossover "Core" NPC (Maggot sublevel). |

Field values observed:

- `NarrativeState`: `NewEnumerator3` for 118 of 121 entries; `NewEnumerator2` twice,
  `NewEnumerator0` once - all three non-3 values on **dead** entries.
- `IsDead = true` on exactly 5 entries:

| Save | Actor | State | Most plausible identity |
|---|---|---|---|
| `WorldSave_Facility_Office1.sav` | `Human_ParentBP_C_0` | 2 | Scripted story death early in the Office Sector |
| `WorldSave_Facility_Office3.sav` | `Human_ParentBP_C_1` | 3 | Scripted story removal |
| `WorldSave_Facility_Pens.sav` | `Human_ParentBP_C_1` | 3 | Abe or Janet leaving the Wildlife Pens as the story advances |
| `WorldSave_Facility_Pens.sav` | `Human_ParentBP_C_3` | 2 | Abe or Janet (as above) |
| `WorldSave_Facility_Pens.sav` | `NarrativeNPC_Ela_C_1` | 0 | Ela the Electro-Pest (creature - killable) |

- `CustomName`: always empty. `Location`: always `(0,0,0)`. `CurrentHealthMap`: always
  an empty map. `NPCClass`: always `None.None`. These are reserve fields the current
  game build never fills for these actors - the editor should not display them.

## NPC identity: the actor paths are NOT anonymous (superseded 2026-09-18, round 99)

**This section's original verdict was wrong.** It reasoned from the actor *class* alone
(`NarrativeNPC_Human_ParentBP_C`, `_Hologram_C`, generic and reused by many placed slots) and
concluded a save entry could not be traced to a specific character. It never looked at what the
*level file* itself sets per placed instance. The coordinator's
`tests/AbioticEditor.Probes/NarrativeNpcLevelProbe.cs` walked all 77 `.umap` level packages and
found that **every placed `NarrativeNPC_*` actor carries its own instance-level
`NarrativeNPC_ConversationRow`** (a `DT_NPC_Conversations` row handle, set per placed actor in
the level, not just inherited from the class default) - e.g. in `Facility_Pens`,
`NarrativeNPC_Ela_C_1` -> row `Labs_Ela_Pest`, and the generic
`NarrativeNPC_Human_Hologram_C_0` -> row `Manse_DL_03`. `DT_NPC_Conversations` itself carries
each row's `NPCName` (localized text) - `Labs_Ela_Pest` -> "Ela", `Manse_DL_03` -> "Dr. Manse".
So the class being generic never meant the *slot* was anonymous: the level file always knew
exactly which character occupies it, the save file just never needed to repeat that.

The map key is the **level actor instance** (`...PersistentLevel.NarrativeNPC_Human_ParentBP_C_1`).
`AbioticEditor.Core.WorldSaves.NarrativeNpcNameCatalog` (added round 99) walks every level once
at dump time (see `dump-registry`), keyed by level file name + actor instance name (the same
(map, actor) pair `DoorIdParser.Parse` already extracts from this exact actor-path shape for
`WorldDoor.Id`), and bundles the result as `GameDataRegistry.NarrativeNpcNames` so neither host
re-walks 77 levels at runtime (~85s measured). `WorldNpcsTab`'s story-character rows show this
name as the primary label when the registry has it, falling back to the pre-existing hint/live
lookup chain below when it does not (an older bundled registry, or a level this build cannot
read).

The named character blueprints exist in the cooked data (`Blueprints/Characters/NarrativeNPCs/`):
`NarrativeNPC_Human_ChefTrader` (Dr. Carson), `_GraysonTrader`, `_MarionTrader`,
`_TravelingTrader_Jimmy`, `_TravelingTrader_Thule`, `NarrativeNPC_Larva`, plus story NPCs (`_Ela`,
`_HastaTria`, `_HammeringHank` = Hank Kettle, `_ForkliftOperator` = wounded Grayson, `_Nibbles`,
`_UnlostMage`, `_ExorChieftain`, `_BossAlly_*` incl. Kylie, `_Waterbot`, `_Kyliebot`, Vignette
props like `_Penguin_VWinter`) - these are a different thing from the conversation-row names
above (a placed actor's *class*, evidenced only for the handful of dedicated non-generic
classes), and `NpcIdentityCatalog`'s curated hints (still the fallback when no conversation row
resolves) are keyed off this class list. Which trader currently occupies a wandering slot is
still tracked separately by `NarrativeNPCDirectorComponent` (`NarrativeNPCSpawns_Struct`: trader
`NPCRowName` + `TradeItemsRow` + `TradeStock` + `NarrativeState`), not by this map or by the
conversation row - a wandering trader's `ConversationRow` may not track which trader is currently
assigned to that spawn point, so the resolved name here is the *placed actor's* identity, not
necessarily "whichever trader is standing there today" for the handful of roaming trader slots.

Trader identities themselves (DT_NPC_Traders -> wiki names) are already in
`TraderLore.cs`: Warren Bunning (Office plaza kiosk, stationary), the Blacksmith
(Manufacturing West F.O.R.G.E., stationary), Jimmy Sanders (Residence), Big Hive Larva
(the Encroachment), and the travelers Grayson Isling, Marion, Dr. Carson, Dr. Ulrich
Thule (first met at fixed story spots, then roam between trader stops).

## `E_NarrativeNPCStates` semantics

The enum has **6 real values** (`NewEnumerator0...5` + `E_MAX`); names are compiler
artifacts with no friendly strings anywhere in the cooked data. Evidence for meaning:

- `NarrativeNPC_ParentBP` (base class of all of these) has `NarrativeState`,
  `LastPlayedNarrativeState`, `OnNarrativeStateChanged`, `SetNewNarrativeState`,
  `SaveNarrativeState`, and `ValidateWorldFlagsFromLoad(LoadedNarrativeState)`.
  Visibility is driven separately by `WorldFlagToAppear` / `WorldFlagToDisappear`
  (world flags), and dialogue by `NarrativeNPC_ConversationRow`.
- `Trigger_NarrativeNPCUpdate` (placed by level designers) pushes `NewNPCState`
  (CDO default `NewEnumerator1`), can swap the conversation row and idle animation,
  and can kill/delete the NPC. So the state selects **which dialogue/behaviour phase**
  the NPC is in - a per-NPC script phase, not a global questline tracker.
- Observed: CDO default `2` (e.g. `NarrativeNPC_Shover`), virtually every live save
  entry `3` (settled/active), trigger pushes `1`, dead Ela `0`.

So states are best presented as opaque "script phase 0–5" with "3 = normal/active state
seen on virtually all live NPCs". There is no DataTable mapping state numbers to
descriptions; deriving more would require decompiling blueprint bytecode.

## UI recommendation

1. **Drop the IsDead checkbox.** Replace it with a status line ("Alive" / "Removed or
   dead") and a **Revive** button shown only when `IsDead` is true. Revive sets
   `IsDead = false` and resets `NarrativeState` to `NewEnumerator3` (the value every
   live entry in the wild carries). Do not offer a way to set `IsDead = true`: players
   generally cannot kill these NPCs in-game, and editor-killing a story NPC or trader
   only removes content (trader gone, dialogue gone) with no gameplay upside.
2. **Caveat on Revive:** most dead entries are story-scripted removals (the game's own
   `KillNPC` triggers - e.g. NPCs who died or moved on in the plot). Reviving them can
   resurrect an actor the story already moved elsewhere. Word the affordance as
   "Revive (restores this actor; story-scripted departures may reappear)".
3. **Keep the state picker but demote it**: label it "Script phase (0–5, game-internal)"
   showing the short ordinal (e.g. "3" instead of `E_NarrativeNPCStates::NewEnumerator3`),
   defaulting collapsed/advanced. Editing it only re-phases dialogue scripts; it is not
   questline progress.
4. **Identify what can be identified.** Map dedicated classes to friendly names
   (`Ela_C` -> "Ela (Abe's pet Electro-Pest)", `HastaTria_C` -> "Hasta Tria (Order
   soldier, Mines)", `Larva_C` -> "Big Hive Larva (trader)", `MGT_CKCore_C` -> "The Core
   (Core Keeper crossover)", `Human_TRADER_C` -> "Static trader (the Blacksmith's
   forge)", `Human_Hologram_C` -> "Story hologram", `Human_ParentBP_C` -> "Story NPC /
   trader spawn slot"). Show the per-save region so users can tell *where* the actor is.
5. **Hide the dead-weight fields** (`CustomName`, `Location`, `CurrentHealthMap`,
   `NPCClass`) - never populated in this game build.
6. Holograms can never be dead in practice (all 62 fixture holograms alive); consider
   filtering them behind a "show holograms" toggle so the list isn't 50% noise.

## Sources

- Probe output: `NarrativeNpcProbeTests` (fixture dump, enum dump, blueprint CDO dump).
- abioticfactor.wiki.gg: Trading, Warren_Bunning, Hasta_Tria, Abe_Stern, Janet_Ross,
  Pens, Manufacturing_West.
- Steam discussion 6369857142715703729 ("You cannot kill him, as with most NPCs in the
  game" - re the Flathill video-store NPC, i.e. Marion).
