# Research: garden planting, pet care state, chemistry timers, story phases, summons

Session 2026-09-28. Scope: the ROADMAP section "Deployables, pets, and progression" plus
"Summoned companions". Evidence is only what the checked-in fixtures and the repo's own verified
data show; the installed game paks were not available and nothing was tested in game. Fixture
tests: `tests/AbioticEditor.Tests/Features/GardenPlotPlantingTests.cs`,
`PetSavedCareStateTests.cs`, `ProgressionCensusFixtureTests.cs`.

## 1. Garden plots: planting and clearing

### Census (all fixture worlds, `DeployedObjectMap`, class `/Farming/GardenPlot_*`)

34 plot entries (some are the same plots seen in two copies of one world).

| Class | Entries | State observed |
|---|---|---|
| `GardenPlot_Large` (8 spots) | 14 | all spots planted |
| `GardenPlot_Medium` (4 spots) | 7 | all spots planted |
| `GardenPlot_SmallRound` (1 spot) | 5 | planted (3 grown, 2 dead) |
| `GardenPlot_Small` (1 spot) | 8 | 1 planted (Legacy Cascade, Nyxshade), 7 EMPTY (client world "Chrissie", `ItemProxies_` has zero elements) |

Only `GardenPlot_Small` has both an empty and a planted example. Comparing an empty small plot
with the planted one, the two entries have the same top-level tag names in the same order and
differ only in the actor's own id and transform and in the `ItemProxies_` array. So:

- **Empty spot = no proxy element.** Planted = one `ItemProxies_` element with `SpotIndex_`,
  `ChangeableData_` (an item struct with its own `AssetID_`, durability 350/350, stack 1, and a
  `DynamicProperties_` array of `Portions`, `GrowthStage`, `GrowthProgress`), `ItemRow_`
  (`RowName` = `Plant_*`), `CookingData_` (all defaults, `CookState` Raw) and an empty `TimerData_`.
- The proxy layout is identical across every class and crop in every fixture (12 crop rows seen).
- `AssetID_` is a 32 char uppercase hex GUID, unique per proxy, referenced nowhere else in the
  file (a raw text scan finds each once), so a fresh GUID is a legal value.

### Observed growth states

Only three `GrowthStage` values ever occur: `4` (Grown, `GrowthProgress` 0, by far the most
common), `5` (one Greyeb spot, progress 7900) and `7` (Dead, 2 spots, no `GrowthProgress` tag and
durability 100 instead of 350). The stage-name list in `GardenPlotsFeature` also names 0 to 3
(Sprout, Budding, Juvenile, Flowering) and 6 (Regrowing), but no fixture shows any of them, so
the values a fresh planting takes are UNVERIFIED. This also means the existing "changing a crop
resets the spot to Sprout/0" behaviour rests on the enum's first entry, not on an observed
freshly planted spot. Watered and fertilized transitions: water (`LiquidLevel_`) and per-spot
fertilizer (`PlayerMadeString_`, `0,|,0,...`) are stored at plot level; every fixture plot has
fertilizer 0, and no fixture shows a fertilized spot, so fertilizer above 0 has no confirmed
example either.

### What was implemented

`GardenPlotPlanting` (Core, `Services/WorldMapFeatures`) plants into or clears the single spot of
a `GardenPlot_Small`, and `GardenPlotsFeature` exposes it as the `crop:0` choice with an
`(Empty)` option. The write is the minimum the fixtures prove:

- Planting clones a game-authored fully grown proxy (stage 4, progress 0) from the same save (the
  same never-fabricate rule `AddDroppedItem` follows) and replaces only the spot, the crop row and
  a new `AssetID_` GUID. The result is therefore the exact observed "grown crop" state.
- Clearing removes the element, giving the observed empty shape.
- Test proof (legacy Cascade, which holds the planted small plot and other planted plots to copy
  from): clear then re-plant restores the identical serialized length, reloads cleanly and reads
  back as Grown/0; the clear step shrinks the file and reloads as an empty array.
- Planting is refused, with no change to the bytes, when the save has no planted crop to copy the
  layout from. This is the case for the "Chrissie" world that holds the 7 empty plots: it has no
  planted proxy of any kind, so nothing can be written there today. Hand-building the struct from
  scratch is not done because a fabricated struct array fails on reload (see the
  `PetDynamicProperties` remarks). A cross-save graft (template taken from another world) was not
  attempted because the two worlds come from different game versions.

### Still unknown, capture needed

- Medium, Large and SmallRound plots: no plot of those classes is empty or partly planted, so
  whether an unplanted spot is a missing element is unproven. Capture: a save with a medium/large
  plot where some spots are empty and others planted, plus the same plot before and after planting
  one spot.
- Fresh states: a plot within seconds of planting (expect stages 0 to 3 and a progress value), one
  after harvest (stage 5 or 6), and a fertilized spot.
- Whether the game accepts a hand-planted spot on load (never run in game).

## 2. Pet feeding and mutation

### What a saved pet stores

`PetSavedCareState` (read-only, Core `Services/World`) lists every dynamic int on a saved pet.
Across all fixtures the only keys on pets are `XP`, `TimerState`, `CurrentAmmo`, `Generic2`,
`Portions`, and on carried pets `MutationProgress` and `PetMutation`. There is:

- **No saved food identity.** No field on a `PetNPC` entry or a carried pet item names a food row,
  a last-fed item or a taming food. Food lists exist only in game tables (`DT_Pets`
  `TamingFood_`/`Mutations_[].MutationItems_`), loaded at runtime by `PetCareCatalog`.
- **No named cooldown or progress field for feeding.** The candidates are two opaque ints:
  - `TimerState`: on every pet (world pets 270,500 to 448,945; carried 436,894 and 448,843). It is 0
    on non-pet items that carry the same key (healing kits, pet beds, nets). Two snapshots of one
    world (dedicated vs Steam copy, different `MinutesPassed`) hold identical values, so it is not
    derived from the world minute counter at save time. Pets stored as items in containers keep one
    too. Plausibly a clock stamp of the pet's last timed event; unproven.
  - `Generic2`: only on the 10 Peccary Sows, always greater than the same pet's `TimerState`
    (differences 2,248 to 80,163). Meaning unknown.
- `MutationProgress` is 3 on both carried pets and `PetMutation` is 1 + the pet's identity index
  in its owner row's `Mutations_` list (Leyak Pest 6, crafted Magma Skink 1); see section B of
  `research-garden-crops-and-pet-mutation.md`. World `PetNPC` entries carry neither key.

### Mutation graph

The graph lives only in the pak tables (`DT_Pets` `Mutations_`), so it is available at runtime
through `PetCareCatalog.MutationOptionsFor`, not offline here. The one owner list documented from
real data is the base `pest` row: Pest, Volatile, Snow, Magma, Enlightened, Leyak, Carbonated
(values 1 to 7), and the crafted skink lineage owns a separate list (Magma at index 0, per that
note). Nothing new could be added without the paks.

### Decision

No feeding or cooldown editor and no write path: the timers are opaque and there is no food
identity to model. Capture needed: the same pet before and after being fed (each valid food, and a
wrong food), with the game clock noted, to see which of `TimerState`/`Generic2`/`MutationProgress`
change and by how much.

## 3. Offline chemistry production

A chemistry bench is a generic deployed-object entry: its top-level tags (`Class_`, `ActorPath_`,
`ChangableData_`, `Transform_`, `ContainerInventories_`, `ItemProxies_`, ...) are exactly the same
18 as a garden plot's, with no bench-specific tag. The words `ProcessingActive` and
`ProcessingTimestamp` (the blueprint's runtime properties named in the earlier audit) occur in none
of the Facility region saves, so no processing state is serialized. There are three bench entries
in the fixtures (two distinct benches) and every one of their 4 slots (3 inputs, 1 output) is
`Empty`. The only saved flasks are 4 `flask_pheromone` stacks, in ordinary container and player
inventories, with `LiquidLevel_` 0: a flask's content is its item row, not a liquid level.
Consequences: batch start, remaining time and elapsed time are runtime-only; no elapsed time is
fabricated and none should be. What could be captured to go further: a bench mid-mix and a bench
with a finished output flask (to see whether the game moves items between slots on its own).

## 4. Character story phases

`NarrativeNPCMap.NarrativeState_` is the only saved phase. Joining all 534 non-pet entries in all
fixture worlds to the bundled `NarrativeNpcNames` registry (verified per-actor character names,
509 of 534 resolve, the 25 rest are unnamed slots):

- Phases seen: `3` (514 alive plus 9 dead), `2` (7, all dead), `0` (4, all dead). Phases 1, 4 and 5
  exist in the enum but never occur in a save.
- Per character: Ela (Abe's Electro-Pest) is the only phase 0 (dead). Abe and Dr. Jager reach
  phase 2 (dead) in the older worlds; Dr. Jager is phase 3 dead in the "Chrissie" world. Janet and
  the Unknown Militant are phase 3 dead. Every other named character is phase 3 alive.
- The enum names are compiler artifacts (`NewEnumeratorN`) and no game table maps a phase to a
  description, so no per-character stage names are added: the census supports only "3 is the
  settled state, 0 and 2 occur together with removal". Adding labels would be a guess.

## 5. Summoned companions (armor-set summons)

`PetCatalog` already curates two summon classes, `NPC_Exor_Ally` (Exor Spirit) and
`NPC_MageEye_Ally` (Mystagogue Drone), as shown-but-not-editable. In the fixtures:

- Every `PetNPC` entry across all worlds is one of `NPC_Peccary_Sow`, `NPC_Monster_Pest_Electro`
  or `NPC_Skink_Crafted`; no summon class is ever in the pet map.
- The strings `Exor_Ally`, `MageEye_Ally` and `Summon` do not occur in any Facility region save
  (`Destructible_Fracture_MageEye_*` hits are unrelated map props). No player save lists a summon.
- Conclusion: the fixtures give no evidence that armor-set summons are persisted at all. Because
  saved pets are keyed by GUID in `PetNPC` and gated by the game's tamed-pet marker, the likeliest
  lifecycle is a runtime spawn that ends with the wearer's session or set (no owner field is saved
  for the regular pets either), but that is inference, not observation. Capture needed: a world
  saved while a summon is active (and while one is despawning), plus a save reloaded after
  logging out with the set equipped.
