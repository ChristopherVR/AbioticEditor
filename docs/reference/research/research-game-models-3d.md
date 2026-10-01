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

- **Blend modes are enum names.** `BlendMode` and `ShadingModel` are stored as
  `EBlendMode::BLEND_TranslucentGreyTransmittance` style names; a typed string read falls back to
  the default, which made all glass opaque. Read as text (`Props.EnumText`), only serialized
  values count (unset means opaque and lit).
- **Light-beam meshes** (`SM_Lightbeam_Wide`, `S_EV_SimpleLightBeam_01`, master
  `/Game/Models/FX/M_EV_Lightbeam_Master_01`: translucent and unlit, two-sided, a falloff
  gradient texture) fake the light under ceiling lamps. Drawn without the game's lighting they
  are solid cones, so any part or level piece whose every material is additive, or translucent
  and unlit, is left out. Window glass (`M_ABF_GlassNoOutline_Master`, translucent but lit) stays.

## Paint

`AbioticDeployed_ParentBP_C.SetupPaintAndTexture` calls `GetTextureOverrides` (the class
default's `PaintedDeployableRow` names a `DT_PaintedDeployables` row; the colour picks its
`Materials_<Colour>` array) and then `Try_ApplyTextureOverrides`, which loops over
`GetMeshComponents` (the actor's own `StaticMeshComponent`s, from `K2_GetComponentsByClass`,
cached in `MeshComponents`) and, for each array index `i` with a valid entry, calls
`SetMaterial(i, entry)`. So slot `i` of every own mesh takes entry `i`; empty entries keep the
mesh's material, and child actors (plug sockets) are untouched. Examples: the crafting bench's
`Materials_Red` is one entry (`M_CraftingBench_Bench_Red`) for slot 0; the makeshift crate's is
`M_MakeshiftCrate_Red`. Probes: `DeployablePaintProbeTests.Dump_PaintRowAgainstClassParts`,
`Dump_PaintFunctionsBytecode`. The plugin's `PaintResolver` follows this; the viewer asks for
`<class path>#paint=<value>` for painted objects.

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

### Portal worlds

The vignette and portal worlds (`V_Alps`, `V_RISE`, `V_FOG`, ...) are `LevelStreamingDynamic`
entries of `Facility` with a `LevelTransform` (e.g. `V_Alps` at (-278297, 79272, -990), yaw 41.7)
and no streaming volume. Their own region saves (`WorldSave_V_Alps.sav`) store positions in
Facility coordinates, so the region's map is placed by the streaming entry of the single-word
world map that streams it. Without that the level sits at the map's origin, kilometres away.

### Landscape terrain

8,255 `LandscapeComponent`s across 32 maps (the Dam valley, portal worlds, the Garden, Suomi,
Anteverse; `GameModelsProviderProbe.Dump_SplineAndLandscapeCounts`). Each component is a level
entry whose mesh key is the map's object path plus `#land=<export index>`, drawn with its proxy's
transform (`AttachParent`). `LandscapeBaker` uses CUE4Parse's `LandscapeMeshDto(component)`
(heights decoded from the heightmap with the component's scale and bias, vertices in proxy-local
quad units plus the component's offset), rebuilds the grid from each vertex's landscape
coordinates, and at level detail keeps every other row and column (always the edges, so pieces
meet). Material: the component's `OverrideMaterial`, else the proxy's `LandscapeMaterial`; the base
colour pick finds the first layer texture (snow in the Alps, grass in the Dam valley), tiled every
4 quads. Layer blending (weightmaps) is not drawn.

### Spline meshes and absolute components

All 391 `SplineMeshComponent`s are tram rail sections (`SM_Rail_Spline_300a`) in `Facility`,
created by `TramSystem_Rail_C` construction scripts and attached to a `SplineComponent` whose
template sets `bAbsoluteLocation` and `bAbsoluteRotation`: its points are world positions, so the
actor's location must not be added (without that the rails drew twice as far from the origin).
`SceneMath.Attach` honours the three absolute flags for every level component. `SplineBaker`
bends the static mesh per component as `USplineMeshComponent::CalcSliceTransform` does: alpha
along the forward axis (mesh bounds unless `SplineBoundaryMin/Max` differ), Hermite position and
direction, a frame from `SplineUpDir`, roll, offset and scale lerped (smoothstep when
`bSmoothInterpRollScale`), per-axis slice frames. Key `<map>#spline=<export index>`.

### Terrain layers, water and decals

* Terrain master slots and the `Main`/`Road`/`Rock`/`Misc` to `Primary`..`Quaternary` mapping: see
  PROGRESS round 137 (inferred from slot textures across every outdoor map; the cooked graph keeps
  no layer names).
* Liquid surface masters (`M_AbioticLiquidSurface_Master`, `M_LiquidSurfaceNoTransparency_Master`)
  tile by world position with a `Scale` parameter in centimetres; their planes are scaled up to
  130x, so mesh UVs would stretch one repeat across a reservoir.
* Some maps place merged HLOD meshes (`/HLOD/` folders) in ordinary static mesh actors.
* Decals: `DecalSize` is the half size; projection along decal X; Unreal's decal UVs are
  `U = 0.5 + z/2`, `V = 0.5 - y/2` in decal space.

## Cost

- Resolving 212 classes with materials: 18 s the first time, 45 ms from the disk cache.
- Baking 37 meshes and 54 textures: 0.6 s; a level slice: under 25 ms once indexed.
- Level index cache: about 50 MB for all 60 Facility maps (binary), a few MB for one base.

## Not covered

Anim-blueprint and ACL-compressed poses (rest pose is drawn) and lights.
Open lids are not saved by the game. Crops in garden plots are
drawn (see `research-garden-crops-and-pet-mutation.md`, "DT_Plants, read").
