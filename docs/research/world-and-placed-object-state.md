# Research: world and placed-object state

Scope: the "World and placed-object state" section of `docs/ROADMAP.md`. Everything below is
proven from the fixtures under `tests/fixtures/` (3 world folders plus a second Steam world
"Chrissie", 178 region saves, 4 metadata saves, 13 player saves) and from the code. The installed
game paks are not available, so nothing here was checked in-game. Where a claim needs the game, it
says so. No save write was added by this work.

Tests that keep these claims true: `UnmodeledFieldCensusTests`, `PlayerEntitlementReportTests`,
`StoryRollbackPreviewTests`, `NarrativeNpcInspectorTests`, `WorldObjectPlacementCatalogTests`.

## 1. Per-player recipe entitlements (`UserEntitlements`)

### Finding: the premise in the roadmap is not supported by any fixture

The roadmap and `docs/PROGRESS.md` describe a metadata map `UserEntitlements`, "keyed by SteamID64
and holding hundreds of `recipe_*` tokens". No fixture has that.

- The metadata save's top-level properties are `MinutesPassed`, `LastPlayed`, `GlobalUnlocks`,
  `LeyakContainmentIDs`, `ServerEntitlements`, `StoryProgressionRow`, `SaveIdentifier`,
  `SaveVersion`. There is **no top-level `UserEntitlements`** in any of the four metadata saves
  (the census test `Census_shows_no_top_level_UserEntitlements_map_in_any_fixture` pins this).
- The string `UserEntitlements` does occur in the metadata save bytes, but only as the **struct
  type name** (`/Script/AbioticFactor.UserEntitlements`) of each `ServerEntitlements` map value.
  That struct has one member, `Entitlements`, an array of strings.
- Every token in every fixture is an ownership token. The complete set is `EarlyAccess` and
  `SupportersEdition`. Client Steam world: 2 players; dedicated server: 2 players (one holding
  both, one holding only `SupportersEdition`); legacy world: 1 player.
- No token in any fixture is spelled like a recipe (`recipe_`, `srecipe_`, `frecipe_`,
  `trecipe_`, `crecipe_`).

So the earlier follow-up most likely conflated the struct type name with a map name. If a future
game build does write a real `UserEntitlements` map or recipe tokens, the code below shows it.

### What was built (read-only)

- `PlayerEntitlementReport.Build(metadata, recipes?, itemName?, personaFor?)` groups tokens per
  player per map (`ServerEntitlements`, and `UserEntitlements` if one ever appears), classifies each
  token as ownership, recipe or unknown, and for recipe tokens records whether the recipe row is in
  the catalog, its crafted item's display name, and whether the same id is in the world's
  `GlobalRecipesUnlocked`.
- Without game data the catalog is empty: recipe-shaped tokens are still classified by prefix and
  shown raw, with no label and no world comparison guess. This is exercised in
  `Classify_resolves_recipes_through_the_catalog_and_degrades_without_one`.
- `ServerEntitlementsFeature` (already shown in the editor's world map features) gains a
  **read-only** "Recipe entitlements" field, present only when a player holds a recipe-shaped
  token. Existing toggles and the "add entitlement" text field are unchanged.

### Relationship to player and world recipe unlocks

What the fixtures show, and no more:

- Player recipes live in each `Player_<id>.sav` `RecipesUnlock_` (4952 entries across 13 players).
  World recipes live in `WorldSave_MetaData.sav` `GlobalUnlocks.GlobalRecipesUnlocked_` and
  `GlobalRecipesResearched_` (a set, 356 to 432 entries). Both use catalog recipe row ids.
- Entitlements are a separate list keyed by SteamID and hold no recipe ids in any fixture, so no
  relationship between an entitlement and a recipe unlock can be shown from data.
- Whether the game grants recipes from an entitlement at runtime is unknown here. Do not offer edits
  that assume it.

## 2. Story rewind consequences

Reviewed map: `StoryRewindConsequenceCatalog` (data) and the read-only `StoryRollbackPreviewBuilder`
(model). Nothing in the rewind path was changed.

### What a rewind reverses (exactly what the existing code writes)

| Consequence | Where | Reversed by |
|---|---|---|
| Chapter and quest flags | Facility `WorldFlags` | `StoryFlagSync.PlanClearForwardFlags` (forward chapter triggers, their dependents, and flags whose region opens later) |
| World-wide email/journal/compendium unlocks | metadata `GlobalUnlocks` | `CodexRevert.ClearForwardGlobalUnlocks` |
| Per-player email/journal/compendium lists | `Player_<id>.sav` | `CodexRevert` player half |
| Respawn point | `Player_<id>.sav` | `PlayerRespawnRevert` |

### What a rewind does not reverse

Doors (`SimpleDoorMap`, `SecurityDoorMap`), story characters' dead flag and script stage
(`NarrativeNPCMap`), fired triggers (`TriggerMap`), pressed buttons, elevator positions, active
portals, broken props, spawn history, looted corpses and taken resources, player-built objects and
pets, tram positions, world-wide recipes and item pickups (deliberately excluded from the codex
trim), player recipes/skills/inventory, and containment records. Each is an entry in the catalog with
its save location and evidence.

### Why there is no chapter link in the save

Every placed-object map is written by the game at runtime, keyed by level actor path, with only a
state value. None stores which chapter or flag caused the state. Where such a link exists it is in the
cooked level (a door's `WorldFlagToUnlock`, read by `DoorGateResolver`), which needs the game
install. So the preview attributes leftovers to a chapter **by region only**:
`StoryRewindConsequenceCatalog.RegionOpensAtChapter` uses the area-to-chapter gates that `FlagGate`
already asserts and returns null for anything not asserted (Office regions, portal vignettes, the
aggregate `Facility` level). Null regions are reported separately as "unattributed" and are never
counted against a target chapter.

### Rollback preview

`StoryRollbackPreviewBuilder.Build(targetChapterRow, facility, regionSaves)` returns the number of
Facility flags the rewind would clear, plus counts of leftovers (non-default doors, dead or advanced
story characters, fired triggers, pressed buttons, elevators at top, active portals, broken props,
spawned spawn-points, looted corpses) in regions that open after the target chapter. It reads
saves and writes nothing (`Preview_does_not_change_the_saves`).

Sample output over the fixtures (regions opening after the target):

| World | to Office | to Labs | to Reactors1Labs |
|---|---|---|---|
| legacy Cascade | 744 leftovers, 77 flags | 316, 32 flags | 0, 1 flag |
| dedicated server | 1314, 176 flags | 877, 130 flags | 151, 70 flags |
| client Cascade | 1268, 153 flags | 830, 108 flags | 137, 48 flags |
| client Chrissie | 330, 33 flags | 18, 2 flags | 0, 1 flag |

Caveats to keep in mind before any UI: a count means "in a non-default state in a region that opens
later", not "was caused by a later chapter". The door state enum names are compiler artifacts; the
preview uses only "not `NewEnumerator0`", not a guessed open/closed meaning. Elevator `TopOpen` is
counted as "parked at the top" and is only loosely a story consequence. No write is proposed: a
reverse operation per map needs per-object semantics that no fixture proves.

## 3. Narrative character details

`NarrativeNpcInspector.ReadNarrative` / `ReadPets` expose every member of a `NarrativeNPCMap` or
`PetNPC` entry read-only: `ActorPath`, `IsDead`, `NarrativeState`, `CustomName` (a text property,
decoded), `NPCClass`, `Location`, the `CurrentHealthMap` (limb to health), the `DynamicProperties`
list (`EDynamicProperty::*` key plus value, kept as text) and the names of any member outside the
known eight. It agrees with the existing `WorldNpc` reader on the shared fields and never mutates the
tree.

Fixture facts (534 narrative entries, 27 pet entries across all worlds):

- Narrative NPCs: names 0, class 0, non-origin location 0, health maps 0, dynamic properties 0,
  unmodeled members 0. Only `IsDead` (20 dead) and `NarrativeState` ever carry data.
- `NarrativeState`: `NewEnumerator3` on 523 entries (9 of them dead), `NewEnumerator2` on 7 (all dead),
  `NewEnumerator0` on 4 (all dead). A non-default stage only ever appears on a dead entry, so the stage
  is a script marker for removed actors, not a free "alive" selector.
- Pets fill every reserve member: class, location, six-limb health map, dynamic properties (for
  example `TimerState`, and XP/mutation keys); 3 of 27 carry a custom name.

Decision: no `IsDead` or stage control was added. The meaning is per character and driven by story
scripts (see `research-narrative-npcs.md`). The inspector is the basis for a future read-only panel.

## 4. Tram destinations

`TramMapFeature` offers, as choices, only the stations some tram currently occupies. Fixture evidence
that this cannot be complete: each of the four Facility saves has 10 trams parked at 10 distinct
stations, but the sets differ between saves (17 distinct `TramSystem_Station_C_N` numbers in total:
1, 2, 3, 5, 6, 9, 10, 11, 12, 13, 16, 19, 20, 21, 22, 23, 27). The legacy world and Chrissie share
3/5/12/19/23; the server and client Cascade worlds share 2/11/22/27. The instance numbers are assigned
by the level asset and no save or catalog names them.

Repository station data is limited to `TramStationCatalog.UnlockFlags`: seven world flags that gate
named stations (`Tram_Containment`, `Tram_DamOffice`, `Tram_DF_R4`, `Tram_MFWest_Office1`,
`Tram_Mines_Office1`, `Tram_Plant`, and `Pens_OpenTramStation`). Nothing links a flag to an instance
number, so the editor cannot say which station is "Containment". Nothing was added to the editable
choices, because offering an instance number the running build lacks would write an invalid
reference.

Level-asset extraction needed (game install required): load
`AbioticFactor/Content/Maps/Facility.umap`, enumerate every `TramSystem_Station_C` export with its
transform, read the rail/track actors to learn connectivity, label each station (from the level or
the matching `Tram_*` flag), and validate which stations each tram can reach. The same walk pattern
already exists in `DoorGateResolver` and `DoorLocationResolver`.

## 5. Static world-object placement: which saved positions are authoritative

Classified in `WorldObjectPlacementCatalog` and asserted in `WorldObjectPlacementCatalogTests`.

| Kind | Maps | Meaning |
|---|---|---|
| Save spawned (position authoritative) | `DroppedItemMap` (2435, all GUID keys), `PetNPC` (27), player-built `DeployedObjectMap` entries (2528 GUID keys), `VehicleMap` GUID entries (2) | Moving the saved position moves the object. |
| Level placed, state only | `SimpleDoorMap`, `SecurityDoorMap`, `ButtonMap`, `CorpseMap`, `DecalMap`, `DestructibleMap`, `ElevatorMap`, `NPCSpawnMap`, `PortalMap`, `TramMap`, `TriggerMap`, `PowerSocketMap` | Keyed by level actor path; no position member exists. There is nothing to move in the save. |
| Level placed, transform saved | `DeployedObjectMap` level-path entries (7402), `VehicleMap` level-path entries (55), `ResourceNodeMap` (12457, `CurrentPosition` is the origin for 3317) | A value is stored, but the fixtures do not prove the game re-applies it. Treat as informational. |
| Position unused | `NarrativeNPCMap` (534) | `Location` is always the origin. |

Editor rule that follows: relocating is only supported for save-spawned entries. Everything level
placed can be located on the map (from the level asset) or have its state edited, not moved. Whether a
level-path deployable's saved transform is honoured on load needs an in-game test.

## 6. Unmodeled-field census

Produced by `UnmodeledFieldCensus` (top-level properties) and `UnmodeledFieldCensus.CollectLeaves`
(members inside struct-valued maps). "Modeled" is the readers' own test: `WorldSaveReader.IsModeledTopLevelKey`
(consumed prefixes plus registered map features) and `PlayerSaveReader.IsModeledKey`. Names have the
blueprint hash suffix stripped.

### Region saves (178 saves): 0 unmodeled top-level properties

Present and modeled: `ButtonMap`, `CorpseMap`, `CustomInventoryMap`, `DayDiscovered`, `DecalMap`,
`DeployedObjectMap` (9930 entries), `DestructibleMap`, `DroppedItemMap`, `ElevatorMap`, `LevelGUID`,
`NPCSpawnMap` (2867), `NarrativeNPCMap` (534), `PetNPC`, `PortalMap` (and a lower-case `portalmap`
variant in 5 saves), `PowerSocketMap`, `ResourceNodeMap` (12457), `SaveIdentifier`, `SaveVersion`,
`SecurityDoorMap`, `SimpleDoorMap`, `TimeOfDay`, `TramMap`, `TriggerMap`, `VehicleMap`, `WorldFlags`.

### Metadata saves (4 saves): 0 unmodeled top-level properties

`GlobalUnlocks`, `LastPlayed`, `LeyakContainmentIDs`, `MinutesPassed`, `SaveIdentifier`,
`SaveVersion`, `ServerEntitlements`, `StoryProgressionRow`. The `Chrissie` metadata save carries only
`MinutesPassed`, `LastPlayed`, `StoryProgressionRow`, `SaveIdentifier`, `SaveVersion` (no
`GlobalUnlocks`, no entitlements).

### Player saves (13 saves): 3 unmodeled members of `CharacterSaveData`

| Property | Type | Saves | Elements |
|---|---|---|---|
| `Compendium_Kill` | ArrayProperty | 3 | 10 |
| `CurrentBuffDebuffs` | StructProperty | 2 | 2 |
| `LastHotbarSelection` | IntProperty | 1 | 1 |

All three round-trip byte-exact (readers preserve unknown properties) but are not editable.
`Compendium_Kill` is a separate member from the modeled `Compendium_KillCount`.

### Members inside modeled maps that no reader references by name

Top-level "modeled" does not mean every entry member is read. A literal-name search of `Core` finds no
reference to these members (a heuristic; a member reached through a generic helper would be missed):
`DeployedObjectMap`: `ActiveSeats`, `BrokeWhenPackaged`, `ConstructionLevel`, `ConstructionMode`,
`CustomSpawnedTime`, `DeployableDestroyed`, `DeployedByPlayer`, `FoundByPlayer`, `HasBeenPackaged`,
`NoResetVignette`, `Supports`; `DroppedItemMap`: `ItemRotation`; `CustomInventoryMap`: `ComponentID`.
`NarrativeNPCMap` and `PetNPC` members are all read by `NarrativeNpcInspector` (empty for narrative
NPCs, see section 3). Full member inventory per map is printed by
`UnmodeledFieldCensusTests.Leaf_census_lists_members_of_struct_valued_maps`.

### Remaining unknowns

- Whether the game applies a level-path deployable's or resource node's saved transform on load.
- Whether any build writes a real `UserEntitlements` map or recipe tokens.
- Which `TramSystem_Station_C_N` is which place, and the track connections (level asset needed).
- Which chapter caused any given open door, dead character or fired trigger (level asset needed, and
  even then only where a flag is set on the actor).
- Semantics of `E_DoorStates` and `E_NarrativeNPCStates` enumerators.
