# Base-building phase 1: placed-object census

Read-only inventory of `DeployedObjectMap` and the other per-actor maps across every `WorldSave_*.sav`
fixture. Produced by `abioticeditor world census <save-or-world-folder> [--json] [--no-objects] [-o file]`
(Core: `PlacedObjectCensus`). Nothing here writes to a save. The CLI output is JSON with per-class
counts, per-field frequency, a per-map layout table, power links, and (unless `--no-objects`) one row
per object.

Fixtures censused (region saves excluding `WorldSave_MetaData.sav`):

| World | Saves | Placed objects | Distinct classes |
| --- | ---: | ---: | ---: |
| DedicatedServerSaves/Worlds/Cascade | 59 | 3603 | 264 |
| SteamSaves/SaveGames/.../Worlds/Cascade | 57 | 3460 | 256 |
| SteamSaves/Legacy/Cascade (older build) | 42 | 2475 | 214 |
| SteamSaves/SaveGames/.../Worlds/Chrissie | 20 | 392 | 78 |

## What `DeployedObjectMap` actually holds

It is **not** only player-built pieces. It holds two populations, told apart by key shape and
`DeployedByPlayer_`:

| Population | Key | `DeployedByPlayer_` | `ConstructionMode_` / `ConstructionLevel_` | Where it is saved |
| --- | --- | --- | --- | --- |
| Level-placed statics (kitchen counters, lockers, office furniture, water coolers, ...) | the actor path, e.g. `/Game/Maps/Facility_Office2.Facility_Office2:PersistentLevel.Deployed_WaterCooler_C_1` | false | `False` / `9999` | the region save of the sublevel they sit in |
| Player-built objects (plug strips, batteries, benches, beds, loot spill bags, teleporter pads, ...) | a 32-hex-character GUID | true | `False` / `9999` (907 of 911 in the dedicated world) | **only `WorldSave_Facility.sav`**, in every fixture world (911 of 911) |

Consequences:

- Player-built objects have an `ActorPath_` in the *persistent* level (`/Game/Maps/Facility.Facility:PersistentLevel.<Class>_<n>`)
  even when they stand in a vignette or another sublevel. `SubLevel` is therefore "Facility" for all of
  them; their **position** (`Transform_`) is the only clue to where they really are.
- Player-built actor names end in a huge instance number (2146867431 .. 2147481744, i.e. close to
  `INT32_MAX`). 36 water coolers / figurines in `WorldSave_Facility.sav` are `DeployedByPlayer_` false yet carry
  such names: level-placed by key shape but runtime-named. Key shape alone is therefore a strong hint,
  not a proof, of "static".
- `actor path == key` held for every path-keyed object (2692 of 2692); GUID-keyed objects have an
  `ActorPath_` that differs from the key.
- No key or actor path is duplicated across region saves of one world (0 duplicates in 3603 objects).
- 4 objects in the dedicated world (furniture: cafeteria table, round table, L-desk, locker-room bench)
  have `ConstructionMode_` = true and `ConstructionLevel_` = 0. Meaning unproven (a construction-mode
  piece at level 0 is the natural guess; not confirmed).

## Struct members (`SaveData_Deployable_Struct`)

Identical member set in all four fixture worlds, each present on every entry (the census reports
counts equal to the object count for all 18). Types are as the GVAS tag declares them:

| Member (hash suffix removed) | Type | Class |
| --- | --- | --- |
| `Class` | SoftObjectProperty (package + asset) | persistent |
| `ActorPath` | Struct SoftObjectPath | persistent |
| `Transform` | Struct Transform (Translation, Rotation, Scale3D) | persistent |
| `ConstructionMode` | **BoolProperty** | persistent |
| `ConstructionLevel` | **DoubleProperty** | persistent |
| `DeployedByPlayer` | Bool | persistent |
| `HasBeenPackaged` | Bool | persistent |
| `ChangableData` | Struct Abiotic_InventoryChangeableDataStruct (paint, dynamic properties) | persistent |
| `ContainerInventories` | Array of Struct | persistent |
| `CustomTextDisplay` | Str (player name, or bed claim `owner}\|!\|{name`) | persistent |
| `ActiveSeats` | Array of Bool | likely runtime |
| `ItemProxies` | Array of Struct | likely runtime |
| `CustomSpawnedTime` | Double | likely runtime |
| `NoResetVignette` | Bool | likely runtime |
| `BrokeWhenPackaged` | Bool | likely runtime |
| `DeployableDestroyed` | Bool | likely runtime |
| `FoundByPlayer` | Bool | likely runtime |
| `Supports` | Array of Struct | likely runtime |

Notes and corrections:

- `docs/reference/world-save-schema.md` still lists `ConstructionMode_` / `ConstructionLevel_` as
  `ByteProperty (enum)`. Every fixture here, including the newer Steam build, declares Bool and Double. The
  schema doc predates that; the coordinator should reconcile it.
- "Persistent" means an existing reader or writer already treats the member as meaningful (identity,
  placement, user-authored state). "Likely runtime" is a heuristic from the member's name and the values
  seen (all defaults or empty in the fixtures); it is **not** proven that the game re-derives them, and no
  writer relies on it. Nothing was found that contradicts either grouping.
- Every fixture entry carries all 18 members, so no delta-serialization omission was observed for this
  struct; that is a property of these fixtures, not a guarantee.
- `Transform_` is complete (Translation, Rotation, Scale3D all present) on all 3603 dedicated-world objects.
  Rotation is a unit quaternion on every object (0 non-unit). 3105 are yaw-only (X = Y = 0), 498 carry pitch
  or roll (232 of the 911 player-built). Scale is (1,1,1) on all but five objects (0.832, mirrored -1,1,1, and
  small non-uniform stretches), so scale is real data and must be preserved.

## Class origins

Every class path in all four worlds is under `/Game/Blueprints/` (`PlacedClassOrigin.GameBlueprint`). No
non-game/modded class path appears in any fixture, so the modded-class handling (`NonGamePath`, reported and
counted, never dropped) is exercised only by unit tests on the path classifier. It stays unproven against a real
modded save.

Most common player-built classes (dedicated world): loot spill bag 116, cable reroute 103, plug strip 40,
crafted wall lamp 25, teleporter pad 25, crafting bench 23, T2 battery 23, T3 crate 20 (212 classes in all).

## Other per-actor maps and where a location lives

From the per-map layout table (`Maps` in the JSON). Only these maps carry a spatial member:

| Map | Location member | Notes |
| --- | --- | --- |
| `DeployedObjectMap` | `Transform` (Translation/Rotation/Scale3D) | full transform |
| `VehicleMap` | `Transform` | already editable (`ApplyVehicles`) |
| `ResourceNodeMap` | `CurrentPosition` (Vector) | position only |
| `NarrativeNPCMap` | `Location` (Vector) | |
| `PetNPC` | `Location` (Vector) | GUID keys |
| `DroppedItemMap` | `ItemLocation` (Vector) | GUID keys, plus its own rotation member |

Doors (`SimpleDoorMap`, `SecurityDoorMap`), elevators, buttons, triggers, NPC spawners, portals and trams store
**state only**; their placement lives in the level asset (see `DoorLocationResolver`). `PowerSocketMap` has no
transform either: a socket's position is its owner's.

## Power links

`PowerSocketMap` entries: `PowerSocket_` (the socket id), `PluggedInDeviceAssetID_`, `ExtraPoweredDeviceAssetIDs_`.
Dedicated world: 552 sockets, 355 with a plugged device, and **`ExtraPoweredDeviceAssetIDs_` is empty on every socket**
(0 extras in all four worlds).

- 507 of 552 socket ids are 33 characters: the owning deployable's 32-hex GUID key plus one suffix digit. 271 of
  those owner keys resolve to a `DeployedObjectMap` entry in the same save. The other 45 socket ids are long
  actor-path strings (level-placed sockets).
- Of 355 plugged devices, 254 resolve in the same save, 37 resolve in a *different* region save of the same world
  (level-placed sockets in a sublevel powering a player-built device stored in `WorldSave_Facility.sav`), and 64
  resolve nowhere in the fixture world.
- No level-placed static object is a plugged device; 252 of the player-built objects are.

See `base-building-group-operations.md` for what this means when a selection is copied.

## Reproduce

```console
dotnet run --project src/AbioticEditor.Cli -- world census tests/fixtures/DedicatedServerSaves/Worlds/Cascade --no-objects -o census.json
dotnet run --project src/AbioticEditor.Cli -- world census tests/fixtures/DedicatedServerSaves/Worlds/Cascade/WorldSave_Facility.sav
```

## Remaining for phase 1

- No modded/unknown class exists in the fixtures, so unknown-class preservation across edits is untested on real data.
- Persistent vs runtime is a heuristic until a before/after pair from the live game is captured.
- Whole-world census is limited to these four fixture worlds and two game builds.
