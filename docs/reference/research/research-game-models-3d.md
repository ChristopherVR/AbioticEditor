# Game models and level geometry for the 3D base view

Research behind the optional `plugins/GameModels3D` plugin (round 134). Probes:
`tests/AbioticEditor.Probes/GameMeshProbe.cs`, `GameModelsProviderProbe.cs`,
`LevelSurroundingsProbe.cs`. All numbers are from the installed game on 2026-10-01 and the
`DedicatedServerSaves/Worlds/Cascade` fixture.

## Placed objects: class to meshes

- A saved object's `ClassPath` (census) is the blueprint class. Its meshes come from the
  **simple construction script** (`SimpleConstructionScript.AllNodes`, each node's
  `ComponentTemplate`) of the class **and every blueprint parent**, root first.
- Subclasses change inherited components through the **`InheritableComponentHandler`**
  (`Records[].ComponentKey.SCSVariableName` -> `ComponentTemplate`). Example: every furniture
  class inherits `FurnitureMesh` from `AbioticDeployed_Furniture_ParentBP_C` with a placeholder
  `SM_Office_Desk_NoDrawers_01`; the leaf class overrides it (`SM_LootBag_01`, `SM_CraftingBench`).
- Override templates are **delta-serialized against the template they replace**. 9 of 212
  classes in the fixture (`Deployed_Antelight_Green/Blue/Orange`, `Deployed_CableReroute`,
  `Container_Aquarium_Large`, ...) have an override that stores no `StaticMesh`; the value comes
  from the archetype (`UObject.Template`). Reading properties through the archetype chain
  resolves **212 of 212** classes.
- `ChildActorComponent`s (power sockets on strips and benches) contribute the child class's parts.
- Component transforms compose through `ParentComponentOrVariableName`; the root component's
  relative transform is not applied (the saved actor transform is the root's world transform).

## Materials

- Item and furniture materials are instances of `/Game/Textures/M_AbioticItems_Master`; the
  diffuse is the instance's own **`Texture`** parameter (`T_LootBag_01`, `T_CraftingBench_Bench`).
  `CMaterialParams2` with `AllLayers` mixes in the master's defaults (`T_Floor_CheckerTile`,
  crack and noise masks), so the plugin reads `TextureParameterValues` leaf first instead.
- Level materials use other masters; the plugin falls back through common parameter names
  (`BaseColor`, `Diffuse`, `Albedo`, ...) and texture suffixes (`_D`, `_BC`, ...), skipping
  names that look like data (normal, mask, noise, checker, `/Engine/`).
- BC7 textures fail on Windows with "Detex decompression failed: not initialized" (CUE4Parse's
  native decoder is not shipped/initialised). The managed decoder
  (`TextureDecoder.UseAssetRipperTextureDecoder`) handles them; the plugin switches on the first
  such failure.

## Level geometry

- Level meshes are the `StaticMeshComponent`s of all actors (`RootComponent`,
  `InstanceComponents`, `BlueprintCreatedComponents`), plus instanced mesh instances. Components
  of placed blueprint actors are also delta-serialized, so the same archetype fallback applies.
- **No BSP**: the level `Model` of Facility, Facility_Dam, Facility_Dam_Waterfall and
  Facility_Office1 has zero nodes. Landscape exists (Facility.umap: 4 proxies, 134 components)
  but did not cover the tested bases; it is not drawn yet.
- **Bounds, not origins**: large pieces have origins far from their extent (a 167 m cliff with
  its origin 129 m from a base standing against it; a 92 m floor slab 155 m away). The index stores
  a world bounding sphere per instance from the mesh's render bounds times the instance scale.
- **Sky domes and backdrops** (`SM_VotV_SkySphere` 13 km, `SM_Skydome`, `SM_Train_Skydome`) hold
  every point; pieces over 800 m across are dropped.
- **HLOD proxies** (`LODActor`, e.g. `SM_HLOD_DamsV2_CleanUp`) are merged far-distance stand-ins
  and are skipped.

### Streamed levels

- `Facility.umap` streams 59 maps, all `LevelStreamingDynamic`, none initially loaded. Each entry
  has a **`LevelTransform`** (translation plus yaw; scale serialised as 0 = default 1), e.g.
  `Facility_Dam` at (-20700, -13900, 0) turned -90 degrees. Drawing sub-levels at their own origin
  put the Mines geometry 165 m+ away from a base standing in it; with the transform applied the
  floor is directly under the bench.
- **Where** a map is loaded comes from `AbioticLevelStreamingVolume` actors (223 in Facility.umap):
  `LevelToLoad` names the map, the shape is the volume's **brush model points** (`Brush` ->
  `UModel.Points`) through the root component's transform. The brush builder's `X/Y/Z` are not the
  placed size (they produced kilometre-wide boxes).
- Vignettes and other set pieces (`V_Salem`, `V_Alps`, ...) share the same space; `V_Salem`
  contributed a huge plane over the Facility. Drawing a streamed map only where one of its volumes
  overlaps the query box removes them, and cut first-time indexing for a Facility base from 60
  maps / 128 s to 5 maps / 14 s.
- Player-built pieces are saved in the persistent level's region save (`WorldSave_Facility.sav`)
  even when they stand in a streamed area (the fixture bench at (3874, 33836, 1608) cm is in
  `Facility_MFMines`).

## Cost

- Resolving 212 classes with materials: 18 s the first time, 45 ms from the disk cache.
- Baking 37 meshes and 54 textures: 0.6 s; a level slice: under 25 ms once indexed.
- Level index cache: about 50 MB for all 60 Facility maps (binary), a few MB for one base.

## Not covered

Landscape terrain, spline meshes (pipes and cables bent at run time), skeletal animation (bind
pose is drawn), decals, lights, and per-object state such as paint colour or open lids.
