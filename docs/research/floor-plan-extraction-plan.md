# Floor-plan extraction plan and location layer status

Status: planning note. Nothing here was verified against game paks (none were available when this was written). Only the shared location index described in the first section exists in code.

## What exists now (Core, no game assets needed)

`WorldLocationIndex` (`src/AbioticEditor.Core/Services/World/Locations/`) is built per region save from a loaded `WorldSaveData`. Every entry is keyed by region, level, full actor path and save identity, and carries one location kind:

| Kind | Meaning | Position |
| --- | --- | --- |
| StaticPlacement | Baked into a level package (doors, via `DoorLocationResolver`) | yes |
| SpawnPoint | Where something may spawn; not a confirmed present entity | yes (not populated yet) |
| LastSavedPosition | Coordinates written in the save (NPCs, creatures, dropped items, deployables, containers, vehicles) | yes |
| CurrentLivePosition | Read from a running game (live containers, doors with live coordinates) | yes |
| CarriedOrContained | Points at an owner; no floor position | never |
| Unresolved | Carries a reason: MissingGeometry, UnloadedActor, ObsoletePath, UnsupportedAsset, NoSavedPosition | never |

Rules enforced by tests: an exact (0,0,0) saved position is treated as "no position" and never becomes a marker; duplicate actor names (for example `SimpleDoor_C_1`) are disambiguated by level; `Coverage()` reports resolved versus unresolved counts per level and category with reasons, and reports "PARTIAL" while anything is unresolved.

Not yet populated from the save models: resource nodes and power sockets (their models are generic feature entries without coordinates; `AddExternal` is the hook for live channels), spawn points (needs level assets), and player positions.

## Extraction plan for floor plans (blocked on game assets)

1. Inventory level packages. Enumerate `AbioticFactor/Content/Maps/*.umap` from the mounted paks (`GameAssetProvider.LoadPackageInternal`, as `DoorLocationResolver` already does for one map). Record for each: persistent level versus streamed sublevels, the World Partition or streaming-level relationships, the level's transform, and which portal worlds exist.
2. Read placements. Root component `RelativeLocation` and rotation for every actor, keyed by actor name inside the level; this already works for doors and generalizes to all actor classes. Note nested attachment (child components) needs the parent chain composed, which the current resolver does not do.
3. Find geometry suitable for a plan. Candidates: static mesh actors for walls and floors (bounding boxes from mesh assets), nav mesh or volume actors as coarse room extents, blueprint room prefabs. Decide per level whether meshes are sliced into floor bands by Z or whether a footprint outline is enough. Exporting whole meshes needs the native texture and mesh decoders, which are optional in this repo today.
4. Generate 2D tiles. Render each floor band to an image tile set with a versioned calibration file per level: world-to-tile affine transform, floor Z ranges, tile size, source game build, generator version. `SectorMapCalibration` is the existing single-fit precedent for the six illustrated sectors.
5. Cache. Store generated tiles and metadata beside the existing extracted-asset cache, keyed by game build so a game patch invalidates them. The desktop host extracts locally; a browser or absent-install fallback would consume a versioned pre-built bundle.
6. Verify. Pick reference actors (doors are ideal) on each level and floor, project them, and record the pixel error. Track per-level status (exported, failed export, unresolved actors) in the coverage report rather than a single yes/no.

## Provenance and distribution review

Level geometry and derived plans are derived from copyrighted game assets. Until reviewed:

- Generate plans only on the user's machine from their own install; do not commit tiles or meshes.
- Do not ship a pre-built browser bundle. Whether abstracted line-art plans (wall outlines only, no textures) may be redistributed needs an explicit review of the game's terms and any modding policy before any bundle exists.
- Record provenance in the metadata: game build id, source package path, generator version, generation date.

## Still blocked or open

- Everything in steps 1 to 6 (no paks here).
- Streaming relationships and sublevel offsets, floors, stairs, lifts and portal worlds.
- Spawn-point placements and "unspawned creature" labeling.
- Coordinates for resource nodes and sockets from level assets.
- The editor-side "Show on map" beyond doors: the only existing map is the per-door sector illustration in the Doors tab, which pins doors on the six calibrated sector drawings. There is no general map surface that other editors could center yet, so cross-editor navigation should wait for the plan generator and a shared map component rather than duplicating the door pin logic.
