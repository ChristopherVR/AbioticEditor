# Abiotic Factor World Save Schema

Save class: `/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C`
(also handled by `AbioticWorldSave` for `Abiotic_WorldMetadataSave_C`).

The `WorldSave_<Region>.sav` files contain one save per game world region. Most
regions are small (e.g. `WorldSave_H_Cabin.sav` ~ 18 KB JSON) and only contain
a handful of doors. The main region (`WorldSave_Facility.sav`) is large
(~108 MB JSON, ~13 MB binary) and contains the full set of deployed objects,
containers, dropped items, and world flags.

## Top-level properties (observed in `WorldSave_Facility.sav`)

| Property                | Type             | Sample count | Notes |
|-------------------------|------------------|-------------:|-------|
| `SimpleDoorMap`         | Map<Str, Struct>  |    4 / 604   | Doors keyed by actor path. Has `DoorState`, `Yaw`, `OneWayDoor_HasBeenUnlocked`, `NoReset`. |
| `WorldFlags`            | Array<Name>       |          109 | List of triggered string flags (e.g. `Office_NewGameStarted`, `Residence_TramLeft`). Plain name strings, no metadata. |
| `DeployedObjectMap`     | Map<Str, Struct>  |          604 | **Primary container source.** Keyed by GUID, value is `SaveData_Deployable_Struct`. Most non-empty containers live here. |
| `ResourceNodeMap`       | Map<Str, Struct>  |          169 | Resource nodes (ore veins, plants, etc.). |
| `SecurityDoorMap`       | Map<Str, Struct>  |           11 | Security doors. |
| `ElevatorMap`           | Map<Str, Struct>  |            3 | |
| `ButtonMap`             | Map<Str, Struct>  |            3 | |
| `NarrativeNPCMap`       | Map<Str, Struct>  |            2 | |
| `PowerSocketMap`        | Map<Str, Struct>  |          223 | Power sockets / wiring state. |
| `TimeOfDay`             | Struct            |       6 keys | Day/time state. |
| `TriggerMap`            | Map<Str, Struct>  |            7 | |
| `NPCSpawnMap`           | Map<Str, Struct>  |            2 | |
| `PortalMap`             | Map<Str, Struct>  |            8 | |
| `LevelGUID`             | Str               |            – | |
| `VehicleMap`            | Map<Str, Struct>  |           10 | |
| `TramMap`               | Map<Str, Struct>  |           10 | |
| `PetNPC`                | Map<Str, Struct>  |            5 | |
| `DroppedItemMap`        | Map<Str, Struct>  |        1 252 | Items dropped on the ground. Each entry has location/rotation + a slot-shaped `ChangeableData` / `ItemDataTable` (single item). |
| `CustomInventoryMap`    | Map<Str, Struct>  |            2 | Custom (named) inventories - `SaveData_Inventories_Struct` keyed by string (e.g. `"Boxy"`). |
| `SaveIdentifier`        | Str               |            – | |
| `SaveVersion`           | Int               |            – | |

## Deployable struct (`SaveData_Deployable_Struct`)

Every value in `DeployedObjectMap` is a struct with the following hash-suffixed fields:

```
Class_77_*                SoftObjectProperty   blueprint path (Deployed_*_C)
ActorPath_164_*           StructProperty       SoftObjectPath to the actor
ChangableData_37_*        StructProperty       single-item Abiotic_InventoryChangeableDataStruct
DeployableDestroyed_56_*  BoolProperty
BrokeWhenPackaged_63_*    BoolProperty
HasBeenPackaged_59_*      BoolProperty
Transform_50_*            StructProperty       Transform (location/rotation/scale)
DeployedByPlayer_71_*     BoolProperty
ConstructionMode_82_*     ByteProperty (enum)
ConstructionLevel_85_*    ByteProperty (enum)
ContainerInventories_110_*  ArrayProperty<SaveData_Inventories_Struct>
ActiveSeats_135_*         ArrayProperty
ItemProxies_149_*         ArrayProperty
CustomTextDisplay_152_*   StrProperty
FoundByPlayer_154_*       BoolProperty
Supports_158_*            ArrayProperty
NoResetVignette_161_*     BoolProperty
CustomSpawnedTime_169_*   ...
```

The interesting field for editing is `ContainerInventories_*` - an
`ArrayProperty` of `SaveData_Inventories_Struct`. **197 of 604** deployables
in the Facility save have a non-empty inventory.

### Top container blueprint classes (with item count)

| Count | Blueprint class |
|------:|-----------------|
|    83 | `Container_LootSpillBag_C` |
|    19 | `Deployed_StorageCrate_Makeshift_T2_C` |
|     9 | `Deployed_CraftingBench_Default_C` |
|     8 | `Deployed_StorageCrate_Makeshift_T3_C` |
|     7 | `GardenPlot_Medium_C` |
|     5 | `GardenPlot_SmallRound_C` |
|     4 | `Deployed_CraftedChargingStation_C` |
|     4 | `Deployed_Container_Stocking_C` |
|     4 | `Deployed_StorageCrate_Makeshift_C` |
|     4 | `Deployed_Freezer_C` |

## Leyak containment

Containment is stored across **two files**, which is why the editor has to read and write both.

### The link: `LeyakContainmentIDs` (metadata save only)

`WorldSave_MetaData.sav` carries a top-level `LeyakContainmentIDs` of type
`Map<NameProperty, StrProperty>`:

```
key   (Name)  creature row name in DT_NPCList - only "Leyak" or "Krasue" are possible
value (Str)   the containment unit's DeployedObjectMap GUID, e.g. F57999CA4F2992036D2AE693DDC396C4
```

Two consequences fall straight out of the shape:

- **A creature can be in at most one unit** (it is the map key), so moving a creature into a unit
  automatically takes it out of the one it was in. Swapping two creatures is a value swap.
- **A world where nothing was ever caught omits the property entirely** (delta-serialization);
  the fixture world `SteamSaves/SaveGames/.../Worlds/Chrissie` has no such tag. Creating the map
  from scratch is therefore part of the write path. Unlike a blueprint variable this property
  carries **no hash suffix** - it is a native property on the save-game class.

### The unit: `Deployed_LeyakContainment_C` (region saves)

A containment unit is an ordinary player-placed deployable in a **region** save's
`DeployedObjectMap` (in every fixture world, `WorldSave_Facility.sav`), keyed by the same GUID the
map above points at:

```
Class_77_*  =  /Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment.Deployed_LeyakContainment_C
```

Units are **player-crafted only** (item row `Leyak_Containment`). A sweep of every cooked `.umap`
found `LeyakSafetyZone_C`, `BP_ContainmentCell_Dropoff_C` and `NPCSpawn_ContainmentBot_C` but **no**
level-placed `Deployed_LeyakContainment_C` anywhere, so the saves are the complete list of units in
a world; there is nothing to enumerate from the paks.

Nothing on the unit itself says whether it is occupied - an empty unit is simply one whose GUID no
`LeyakContainmentIDs` value points at.

### The unit's own state: `EDynamicProperty` slots

The unit stores its state in the generic slots of its item data
(`ChangableData_.DynamicProperties_`):

| Slot       | Meaning |
|------------|---------|
| `Generic1` | Containment stability, 0..100. |
| `Generic2` | Unknown; 0 on every unit observed. |
| `Generic3` | Index into the blueprint's `LeyakContainmentData` array: **0 = Leyak, 1 = Krasue**. |
| `Portions` | 1 on every unit observed (and on most other deployables too). |

How those were pinned down: the blueprint reaches the slots through generic
`GetDynamicProperty`/`SetDynamicProperty` calls, so the slot names are not recoverable from the
cooked asset. Instead, across every fixture world `Generic2` and `Generic3` are used by **no other
deployable class at all**; `Generic1` only ever holds values in 0..100, matching the blueprint's
`MaxStability = 100` and its `StabilityDecreasePerNight = -14`; and `Generic3` matches the creature
the metadata save assigns to that exact unit in all five units observed.

The blueprint's class defaults also give the containable set directly - `LeyakContainmentData` has
exactly two entries:

| Index | `LeyakNpcRow` | `RequiredStabilityItem` | Bar colour | Output node |
|------:|---------------|-------------------------|-----------|-------------|
| 0 | `Leyak` | `food_greyeb` | `FF003A` | `Resource_Micronode_LeyakEssence_C` |
| 1 | `Krasue` | `food_milk` | `00FFFF` | `Resource_Micronode_KrasueEssence_C` |

### What we model

- `ContainmentCreatureCatalog` - the containable rows, their indices, feed items and the unit class.
- `WorldContainmentUnit` - one deployed unit (id, region save, position, stability, stored index,
  and the creature the metadata save assigns it).
- `WorldSaveReader.ReadContainmentUnits` / `WorldSaveWriter.SetLeyakContainment` /
  `WorldSaveWriter.SetContainmentUnitCreatureIndex`.
- `ContainmentDirectory.Survey` joins the two files; `ContainmentDirectory.SyncUnitRecords` writes
  the region saves back after the map changes.

Research probes: `tests/AbioticEditor.Probes/ContainmentUnitProbe.cs` (save side) and
`ContainmentBlueprintProbe.cs` (pak side).

## `SaveData_Inventories_Struct`

Each entry of `ContainerInventories_` (and of `CustomInventoryMap`) is a struct
with one field:

```
InventoryContent_3_*  ArrayProperty<Abiotic_InventoryItemSlotStruct>
```

`Abiotic_InventoryItemSlotStruct` is **the same struct used by player
inventories** - it has `ItemDataTable_*` (a `DataTableRowHandle` with
`RowName`) and `ChangeableData_*` (`CurrentStack_*`, `CurrentItemDurability_*`,
etc.). We can therefore reuse `InventoryItemSlot` and the existing
read/write helpers.

## What we model in the Core layer

The first pass models **containers** (the obvious editable category):

- `WorldSaveData` - top-level wrapper around `SaveGame` + typed container list.
- `WorldContainer` - one container (a deployable with a non-empty
  `ContainerInventories_*`, or an entry of `CustomInventoryMap`) with:
    - `Id` - the map key (GUID for deployables, name for custom inventories).
    - `Source` - `Deployed` vs `Custom`.
    - `ClassName` - blueprint asset name (e.g. `Deployed_StorageCrate_Makeshift_T2_C`)
      for deployables; `null` for custom inventories.
    - `Inventories` - list of `WorldInventory`, each holding
      `IReadOnlyList<InventoryItemSlot>`. A deployable can in theory have
      multiple inventories; the Facility data has one each in practice.

Future work (not in this pass):

- `SimpleDoorMap` / `SecurityDoorMap` - door state toggles.
- `WorldFlags` - add/remove triggered flags.
- `DroppedItemMap` - single-item slots scattered in the world.
- `ResourceNodeMap`, `TimeOfDay`, `PowerSocketMap`, NPCs, vehicles, trams.
- `DayDiscovered` (IntProperty) - day the level was first entered; present on every
  region save except `Facility` itself (found by the server-save deep dive, see
  `research-server-saves.md`). Read-only display candidate for a world-map view.
- `CorpseMap` (MapProperty) - `Map<Str actorPath, {ActorPath (SoftObjectPath),
  IsGibbed (Bool), IsLooted (Bool)}>`; NPC corpses, e.g.
  `...PersistentLevel.CharacterCorpse_OrderSniper_C_1`. Harmless to leave unmodeled
  (round-trips untouched). Also from the server-save deep dive.
