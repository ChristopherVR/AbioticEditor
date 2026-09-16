# Research: garden plot crop identity and carried-pet mutation target

Session 2026-09-17. Two independent questions, following the "never write a field without
evidence" rule. Evidence below comes from the installed game's paks (via `GameAssetProvider`,
raw `DefaultFileProvider` dumps) and from real save fixtures (`tests/fixtures/`). No em dashes.
Probes: `tests/AbioticEditor.Probes/ResearchDumpProbe.cs`.

## A. Garden plot crop identity

### Where the crop is stored

A garden plot's `DeployedObjectMap` entry has an `ItemProxies_` array, one element per planting
spot, each with `SpotIndex_` and `ItemRow_` (a `DataTable` + `RowName` handle). Across every
fixture world save (`Dump_GardenPlotItemProxies_AcrossFixtureWorlds`), `DataTable` is always
blank (the handle's DataTable reference is unserialized at its blueprint default) and `RowName`
is one of: `Plant_GlowTulip`, `Plant_Corn`, `Plant_SpaceLettuce`, `Plant_Egg`, `Plant_Tomato`,
`Plant_Carrot`, `Plant_Wheat`, `Plant_Potato`, `Plant_Shadowberry`, `Plant_Pumpkin`,
`Plant_Greyeb`, `Plant_Nyxshade`.

### One row, not seed-vs-crop

There is no separate "seed" row: `ItemTable_Global` has a single `Plant_<Name>` row per crop,
and that is the same row the save stores whether the spot was just planted or is fully grown.
Growth is tracked separately by two per-spot dynamic properties (see below), not by swapping the
row. So the crop field is a single item-identity picker, not two related fields.

### Telling real crops from the "digital farm plot" cartridges

`ItemTable_Global` has 33 rows starting with `Plant_`, not all of them crops. A second,
unrelated feature - the "digital" farm plot (`Deployed_GardenPlot_Digital`, a different
deployable than `GardenPlot_Small/Medium/Large/SmallRound`, confirmed by
`AbioticFactor/Content/Blueprints/DeployedObjects/Farming/Deployed_GardenPlot_Digital.uasset`
and `FarmingPlot_Digital.uasset`) grows ammunition, and its cartridges also use the `Plant_`
prefix: `Plant_Blank`, `Plant_Pepper`, `Plant_Lamogi`, `Plant_9mm`, `Plant_Magnum`, `Plant_556`,
`Plant_308`, `Plant_12g`.

`GameplayTags_` does **not** separate the two families: both are tagged `Item.Plant`, and most
cartridges are even tagged `Item.Material.Biological` too (`Dump_ItemTableGlobal_PlantRowTags`).
The reliable discriminator found is `WorldStaticMesh_`: every cartridge row resolves to
`/Game/Models/Items/Farming/SM_FarmPlot_Digital_Cartridge.SM_FarmPlot_Digital_Cartridge`; every
real crop resolves to the generic `SM_ItemBox_Default` (the plant's actual model is chosen by
the garden plot itself, not this item). `Plant_Dead` is a third case: mesh
`SM_DeadPlant_Farmable`, tagged only `Item.Material.Biological` (no `Item.Plant`) - a fallback
identity, not something a player plants, so it is excluded too.

Applying that filter yields 24 real crop rows (all 12 fixture-observed names plus): `Plant_Corn`,
`Plant_Tomato`, `Plant_Wheat`, `Plant_Greyeb`, `Plant_Nyxshade`, `Plant_Super_Tomato`,
`Plant_RopePlant`, `Plant_Egg`, `Plant_SpaceLettuce`, `Plant_VinePlant`, `Plant_Potato`,
`Plant_Rice`, `Plant_Antelight`, `Plant_Antelight_GRN`, `Plant_Antelight_pink`,
`Plant_Antelight_red`, `Plant_Antelight_orange`, `Plant_Antelight_blue`, `Plant_Antelight_RGB`,
`Plant_Antelight_space`, `Plant_Pumpkin`, `Plant_GlowTulip`, `Plant_Shadowberry`, `Plant_Carrot`.
This list is hardcoded in `GardenPlotsFeature.CropRows` (curated, not read live from the game at
runtime - see "What was not attempted" below); every fixture-observed row is in it.

### Growth reset on a crop change

`GrowthStage_`/`GrowthProgress_` (per-spot dynamic properties, already read/written by the
existing stage/growth fields) were not found in any per-plant table with a documented "initial"
value, but the stage name enum's own first entry is `Sprout` (index 0) and progress is a 0-10,000
counter that starts at 0 by construction. Choosing a different crop resets both to 0/`Sprout` -
a generic, plant-independent reset, not a per-crop lookup, so no per-plant grow-time table was
needed to implement it safely.

### What was not attempted: DT_Plants

`AbioticFactor/Content/Blueprints/DataTables/DT_Plants` (row struct `PlantData`) is almost
certainly the table that actually defines each plant's grow time and stage meshes -
`CookableData_.FarmableDataRow_` on e.g. `Plant_Wheat` points at it with `RowName: Wheat` (no
`Plant_` prefix, a different naming scheme than `ItemTable_Global`). Loading it directly via
CUE4Parse's `UDataTable.RowMap` returns 0 rows even though the struct name resolves correctly;
it appears to be a `CompositeDataTable` whose merged rows are not baked into this asset the way
`ItemTable_Global`'s are. It was not pursued further because it turned out to be unnecessary:
the growth reset above only needs the universal 0/Sprout values, not any per-plant timing data.
If a future change needs real grow times, this table is where to look next, and reading it will
likely need `ModTableDiscovery`-style multi-file merging rather than a single `LoadPackage` call.

### What is out of scope: empty <-> planted transitions

No fixture shows a truly empty (never-planted) spot: every physical spot on every observed plot
size (small/medium/large) has an `ItemProxies_` element with a real crop row. Constructing a
brand-new proxy element (with its own `SpotIndex_`/`ItemRow_`/`ChangeableData_`/
`DynamicProperties_` sub-tree, hash-suffixed exactly) without a real example to match against
would be guessing a wire shape CLAUDE.md explicitly warns against risking. The shipped picker
therefore only changes the crop identity of a spot that is **already** planted (matching what
the read side already required); planting into a genuinely empty spot, or clearing one back to
empty, is left unsupported and documented as such.

## B. Carried pet mutation target (`PetMutation`)

### The encoding

`DT_Pets` defines `Mutations_` (an array of `MutationTarget_` + `MutationItems_`) only on a
family's "owner" row - most variant rows' own `Mutations_` is empty. Example, the base `pest`
row's `Mutations_` (7 entries, `MutationTarget_` in `DT_NPCList`):

| 0-based index | Target | 1-based (`PetMutation` value) |
|---|---|---|
| 0 | `Pest` (base/revert) | 1 |
| 1 | `Pest_Volatile` | 2 |
| 2 | `Pest_Snow` | 3 |
| 3 | `Pest_Magma` | 4 |
| 4 | `Pest_Enlightened` | 5 |
| 5 | `Pest_Leyak` | 6 |
| 6 | `Pest_Carbonated` | 7 |

Across every fixture player save, exactly two carried pets exist
(`tests/AbioticEditor.Probes/PetMutationProgressProbe.cs`):

- `Player_76561197993781479.sav`, slot Hotbar/0: `Skink_Magma_Crafted`, `PetMutation=1`.
- `Player_76561198128277890.sav`, slot Equipment/12: `Pest_Leyak`, `PetMutation=6`.

Both match **1 + the pet's own 0-based position in its owning row's `Mutations_`** exactly:
`Pest_Leyak` sits at index 5 in `pest`'s list (value 6); the crafted Magma Skink sits at index 0
in `Skink_Crafted`'s list (value 1) - a **separate** list from `Skink`'s own (`Skink_Magma`
index 0, `Skink_Mushroom` index 1), confirming the crafted (weapon-form) lineage owns its own
mutation list rather than inheriting one through `DefaultParent_`. `PetMutation=0` is the save's
default/unset sentinel and is never itself a list entry.

Because the crafted lineage's list is not reachable by walking `DefaultParent_` upward, the
owning row is found by: use the pet's own row if its `Mutations_` is non-empty, else search every
`DT_Pets` row's `Mutations_` for one whose `MutationTarget_` names this pet. Implemented as
`PetCareCatalog.MutationOptionsFor` (`src/AbioticEditor.Core/Catalogs/World/PetCareCatalog.cs`)
and verified against both real fixture values in
`tests/AbioticEditor.Probes/ResearchDumpProbe.cs::Verify_MutationOptionsFor_MatchesObservedFixtureValues`
and `tests/AbioticEditor.Tests/PetGameDataTests.cs::MutationOptionsFor_resolves_the_pets_own_identity`.

### What became editable

`CarriedPet.PetMutation` already round-tripped through the writer/reader and the live
`companions.lua` channel (both already read/write the `PetMutation` dynamic property exactly
like `MutationProgress`; no Lua change was needed). Only the UI exposed it as read-only. Now:

- `PetCareGuide.razor` renders a picker (options from `MutationOptionsFor`, plus "Not mutated"
  = 0, plus the current saved value as its own option when it does not match any resolved
  choice) when a caller binds `PetMutationChanged`; read-only text otherwise.
- `PlayerCompanionsTab.razor` binds it for both the offline session and the live COMPANIONS tab
  (same `IPlayerCompanionsSession` boundary both already share).
- `ItemCatalogService.GetPetMutationOptionsAsync` caches the resolved options per item row.

The picker does not validate that the pet could actually reach that mutation in the game (no
food-eaten history is modeled), matches the same caveat already documented for `MutationProgress`.
