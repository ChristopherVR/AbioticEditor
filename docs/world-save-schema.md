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
