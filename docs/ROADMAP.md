# Known editing gaps and base-building roadmap

Status reviewed 27 September 2026 and updated 28 September 2026 against the current save readers/writers, editor surfaces, and the documented fixture and game-data audits. The list focuses on known gaps. It is a working plan and should be updated as each item is researched and completed.

## Progress on 28 September 2026

Work landed on every section below. No item has left the roadmap yet: nothing was verified in-game, and most of what landed is read-only inspection or research that narrows the item. Research notes are listed under **Research notes** in the [technical reference](reference/index.md) sidebar.

| Area | Landed | Still open |
| --- | --- | --- |
| Character saves | Transmog disable flags, favorites, distillery history, active effects and last hotbar slot are read and shown (view only) on the Transmog, Inventory, Vitals and Recipes tabs. [Notes](reference/research/research-player-slot-flags-and-effects.md) | Meaning of each transmog flag index, favorites remapping, buff expiry units. Writers for the transmog flags and hotbar slot exist but are not offered in the UI. |
| Account saves | Read-only readers for `Unlocks.sav`, `PlayerStatsSave.sav` and `UserSettings.sav`; the host password is never carried in the model. [Notes](reference/research/research-account-saves.md) | No editor screen or writes. Relationship to Steam achievements. |
| World state | Entitlement report, story rewind consequence catalog and preview, narrative character inspector, tram station and placement catalogs, unmodeled-field census. [Notes](reference/research/world-and-placed-object-state.md) | The fixtures contain no per-player recipe entitlement map (only `EarlyAccess`/`SupportersEdition` tokens), so that item is re-scoped below. Station identities and track links need level assets. |
| Crops, pets, chemistry | Planting and clearing spot 0 of a small garden plot, proven byte-for-byte against fixtures (copies a planted crop from the same save). Read-only pet care state. [Notes](reference/research/research-garden-planting-and-pet-feeding.md) | Other plot sizes, growth stages 0-3, harvest and fertilizer states, pet feeding captures, chemistry mid-mix captures, summon persistence. |
| Compatibility | Older-than-tested saves are flagged; header evidence and an explicit unknown-build result; per-area operation support; live compatibility fields. [Matrix](reference/compatibility-support-matrix.md), [migration policy](reference/save-migration-policy.md), [coverage audit](reference/coverage-audit.md) | Per-area evidence, version-specific layouts, build-matched catalogs, older fixtures, a UI surface for the report. |
| Base building | `world census` CLI and placed-object census, coordinate-space findings, group reference analyzer. A **3D view** tab (Three.js, bundled offline) with category boxes, search, filters and an inspector. **Experimental move/rotate** of player-built objects (numeric and gizmo), staged until SAVE. Core **delete, duplicate** (identity, power-outlet and teleporter remapping with explicit policies), group move/rotate, snapping, alignment, distribution and proximity hints, one staged model with preview and revert, and `world object move\|rotate\|delete\|duplicate` CLI commands. All proven against fixtures. [Census](reference/research/base-building-placed-object-census.md), [coordinates](reference/research/base-building-coordinate-spaces.md), [group operations](reference/research/base-building-group-operations.md) | Delete/duplicate and group edits in the 3D view; phase 6 build-piece semantics (real models, costs); phase 7 in-game validation of moved, copied and deleted objects (fresh GUIDs, outlet records, teleporter tags, streamed sublevels, yaw sign). |
| Level maps | Shared location index with explicit unresolved reasons and a coverage report. [Extraction plan](reference/research/floor-plan-extraction-plan.md) | Floor plans and "Show on map" (no shared map exists yet to center). |
| Power | Link tracing, power graph and validator, connections inspector under the Power Sockets tab. [Notes](reference/research/research-power-network-links.md) | Offline connection writes, live connect/disconnect operations, placement. |
| Game Pass package | Characterization tests, then extraction of the storage layer into the standalone [GamePassStorage](https://github.com/ChristopherVR/GamePassStorage) repository (no dependency on this repo), consumed here as a submodule. [Inventory](reference/game-pass-extraction-inventory.md) | Migration steps 5 to 7: Abiotic adapter extraction, a second real game, NuGet publishing. |

## Known gaps

### Player and account saves

- **Per-slot transmog disable flags:** `TransmogDisabledArray_` is preserved in character saves but is not exposed for editing. Determine whether each index maps to a visible gear slot or an internal state before adding controls.
- **Inventory favorites and distillery history:** `FavoritedSlots_` and `ItemsDistilled_` are recognized and preserved but have no editor controls. Establish whether users need to edit these directly and how they should stay consistent when inventory slots move or items change.
- **Current buffs and debuffs:** the character's active effect data is not presented as a semantic editor. Identify which effects persist in saves, their duration/stack rules, and whether writing them can be done safely without reconstructing runtime state.
- **Last selected hotbar slot:** `LastHotbarSelection_` has appeared in newer saves and is not modeled. Confirm its valid range and whether changing it has a useful effect beyond choosing the initial selected slot.
- **Customization unlock state:** `Unlocks.sav` is carried during conversion but does not have a dedicated editor. A useful surface would show owned versus unavailable appearance rows and avoid offering invalid unlock names.
- **Account statistics and achievement mirror:** `PlayerStatsSave.sav` is preserved and carried during conversion, but its kill counters and local achievement mirror are not edited in the app. Define how this should relate to Steam achievements before exposing writes.
- **Account settings:** `UserSettings.sav` is preserved and carried during conversion, but favorites, pinned recipes, tutorial history, popup state, and host preferences do not have a dedicated editor. Password-like host settings should not be surfaced as ordinary form values.

### World and placed-object state

- **Story rewind consequences:** rewinding chapter flags clears forward progression and related codex/player state, but physical consequences such as opened doors or dead characters are not generally reversed. Build a reviewed consequence map and make any rollback preview explicit before adding writes.
- **Narrative character details:** the saved NPC map has fields such as health maps, custom names, and dynamic properties that do not have a complete semantic editor. Do not turn arbitrary script stages or the saved `IsDead` field into universal story/alive controls without verified per-character meaning.
- **Complete tram destinations:** the offline station picker derives its choices from stations occupied by trams in the save. It cannot enumerate empty stations. Extract the full station catalog and track connections from level assets, label destinations, and validate which destinations each tram can reach.
- **Static world-object placement:** moving a saved position does not necessarily move an actor placed in a level asset. Define which objects can be relocated, which require runtime operations, and which can only be located on the map.
- **Per-player entitlements:** no fixture has a top-level `UserEntitlements` map; the name is only the struct type of `ServerEntitlements` values, and every observed token is `EarlyAccess` or `SupportersEdition`. A read-only per-player report now exists. Capture a save that holds recipe-style entitlement tokens before designing any recipe-entitlement editor.
- **Unmodeled fields vary by game build and save:** readers preserve unknown properties, but preserved data is not automatically editable. Use the compatibility report and `UNKWN` diagnostics to add concrete fields to this list when a current save or game update reveals them.

### Deployables, pets, and progression

- **Planting and clearing crops:** garden care exposes supported existing plot state, but empty-spot planting/clearing does not have a verified serialized shape. Capture real saves for empty, planted, harvested, watered, and fertilized transitions before adding those actions.
- **Pet feeding and mutation choices:** the editor provides species information and mutation guidance, but a complete semantic editor for feeding timers and mutation targets is not available. Verify the saved food identity, cooldown, progress, and valid mutation graph before adding controls.
- **Offline chemistry production:** flask contents and recipe context can be inspected and edited, but processing timers are runtime state. There is no offline control that starts a batch or fabricates elapsed production time.
- **Character story-stage names:** saved script phases have no universal friendly mapping or verified per-character editing workflow. Add named controls only for character-specific stages verified from current game data.

### Live-editing gaps

Live support is separate from offline save support and depends on host authority, the running game, and the installed agent version. Keep these limits visible where a shared editor surface is used.

- **Durable live power changes:** socket timer fields are reset by the game's socket-save function. A live routing editor needs the actual connection and disconnection operations and proof that its changes survive the next game save; changing timer fields alone cannot provide that.
- **Offline chemistry transfers:** there is no offline flask-transfer workflow through the live transfer path. Define a staged transfer that preserves complete item/flask metadata and saves both endpoints together.
- **Unloaded actors:** live discovery sees loaded actors. A full-level map needs to distinguish loaded live objects, saved objects, and static asset placements, including when live data becomes stale after streaming or a region change.
- Actions that depend on server RPCs, replication notifications, or game-thread behavior need in-game verification before being described as supported. Stub-harness success alone does not establish multiplayer propagation or save/reload persistence.

## Base-building editor

The missing construction workflow is a spatial editor for individual pieces and whole bases: place, move, rotate, duplicate, delete, align, and connect objects on a full level plan, with an optional 3D view.

### Data sources and boundaries

- **World saves are authoritative for placed state.** Start with `DeployedObjectMap` and related region/world maps for object identity, class, transform, construction values, custom names, paint, power, and storage. Confirm which map owns each value and how the map key relates to the in-game actor.
- **`Mappings.usmap` describes reflected Unreal types.** Use it to resolve class/property layouts and enums, not as a source of placed-object coordinates or a complete asset catalog.
- **Mounted game paks provide presentation assets.** Use CUE4Parse to resolve meshes, textures, materials, and level data. Account for missing native decoders, unloaded assets, game updates, and modded content with honest placeholders.
- **Three.js should remain a view and interaction layer.** Keep the Core save model and writer independent of Three.js so the CLI and existing editor surfaces remain usable without a 3D renderer.

### Phased plan

1. **Close the schema inventory.** Build a representative census of placed classes and save layouts across regions, including transforms, construction mode/level, ownership/name, paint, inventory, power, and actor paths. Separate persistent fields from runtime-only fields. Record unknown and modded classes without dropping them.
2. **Prove coordinate spaces.** Determine world, level, and streamed-sublevel transforms. Compare save coordinates with installed level geometry and in-game screenshots for multiple regions and elevations. Do not enable movement until coordinate conversion is reproducible.
3. **Ship a read-only 3D viewer.** *(Landed 28 September 2026 with placeholder boxes.)* Render one region at a time with a camera, floor/region filtering, search, object selection, labels, and an inspector that links to existing semantic editors. Render unresolved classes as selectable placeholders. Keep the existing 2D Bases view available.
4. **Add selection and staged transforms.** *(Landed as an experimental opt-in for player-built objects; not verified in-game.)* Add move and rotate with numeric entry and gizmos, then undo/revert. Preserve identity, actor path, and unknown struct fields. Save only the selected staged changes and provide a clear before/after preview.
5. **Add safe construction operations.** *(Delete, duplicate, multi-select transforms, snapping, alignment and proximity hints landed in Core and the CLI; copy/paste and collision are not done; UI wiring pending.)* After transform round-trips are proven, consider duplicate, delete, multi-select, copy/paste, snapping, alignment, and collision/overlap hints. Validate class-specific required fields and inventory links before writing.
6. **Integrate build-piece semantics.** Use verified class catalogs and game data for friendly names, icons, costs, construction levels, paint, and supported variants. Do not infer placement validity or collision from a mesh alone.
7. **Validate against the game.** Use disposable save copies: load and render, stage edits, save, reopen, compare untouched bytes/entries, then load in-game. Test multiple regions, streamed levels, large bases, missing assets, unsupported classes, and modded entries before enabling writes by default.

### Completion criteria

- Placed objects resolve to stable save identities and correct locations across the supported regions and levels.
- Unsupported assets remain visible as placeholders and survive a save unchanged.
- Move, rotate, duplicate, and delete preserve unrelated save data and can be reverted before writing.
- A saved scene agrees with the in-game result for the verified classes and transformations.
- Large regions remain usable through asset caching, culling, and incremental loading.

## Full level maps and object locations

**Gap:** there is no complete, floor-aware blueprint-style map of every level shared by all object editors. A coordinate readout or a marker on a sector illustration does not provide enough context to locate an object in the actual rooms, corridors, or vertical spaces.

### Required map experience

- Provide a readable plan for each level, streamed sublevel, portal world, and floor. Include walls, rooms, corridors, doors, stairs, lifts, landmarks, and connections between floors/levels where those can be established from assets.
- Add **Show on map** to NPCs, creatures, doors, dropped items, containers, resource nodes, sockets, deployables, and other locatable entities. Open the correct level and floor, center the selected object, and retain the selection when switching between its editor and the map.
- Clicking a map marker should open that object's details and supported editing actions. Support search, category filters, overlapping-marker selection, and height/floor filtering.
- Distinguish static placements, spawn points, last saved positions, and current live positions. An item carried by a player or stored in a container should locate its owner/container; it has no independent floor position. An unspawned creature or potential loot spawn must not be shown as a confirmed present entity.
- Identify unresolved locations explicitly. Missing geometry, unloaded actors, obsolete actor paths, or unsupported assets must not produce a marker at an invented origin.
- Respect discovery/spoiler settings for unexplored rooms, characters, and content. Offer an explicit full-map view for users who want it.

### Work required

1. Inventory level packages, streaming relationships, root/component transforms, floors, and geometry suitable for floor-plan extraction. A usmap supplies type information; room geometry and actor placements come from level assets.
2. Build a shared location index keyed by region, level, actor path, and save identity. Join fixed placements to saved state, and overlay live positions when available. Resolve duplicate actor names within their full level context.
3. Generate readable 2D plans or tiled floor slices from geometry, with versioned calibration and metadata. Use the same coordinates for a Three.js view so selection remains consistent between 2D and 3D.
4. Extend asset extraction and caching for maps and geometry. Plan a desktop extraction path and a versioned fallback for browser use or absent game installations; record asset provenance and review what can be distributed.
5. Wire map navigation into each editor and verify reference positions on every supported level and floor. Track coverage per level, including failed exports and unresolved actors, rather than describing partial coverage as a complete map.

**Done when:** selecting a resolvable entity opens its correct location on the right level/floor, selecting its marker returns to the same entity, and unresolved or historical locations are clearly labeled. Every supported level has a reviewed floor plan and coordinate alignment.

## Power routing and network construction

**Gap:** sockets need a full power-building workflow: creating extensions, configuring socket/strip combinations, connecting devices, rerouting branches, and inspecting the resulting network on the level map.

### Required building experience

- Show the connection graph over the floor plan: source sockets, extensions/cables, plug strips, batteries, switches, and consuming devices, for the device types confirmed by the game data.
- Select any socket or device to trace its upstream source and downstream connections. Show disconnected branches, missing endpoints, and powered/unpowered status with the source of that status identified.
- Place supported extension devices, choose compatible input/output endpoints, connect/disconnect them, reroute a branch, and configure multi-device combinations. Moving, duplicating, or deleting a connected object must update its references coherently.
- Support connections across streamed levels or region saves. Keep them selectable when an endpoint is outside the current map view.
- Offer a proposed-connection preview, undo/revert, and a grouped save for all affected records/files. Report unresolved endpoints before applying changes.

### Research and implementation plan

1. Trace `PowerSocketMap.PowerSocket_`, `PluggedInDeviceAssetID_`, and `ExtraPoweredDeviceAssetIDs_` into deployable identities, component data, and runtime actor references. Identify which side owns each link and whether reciprocal records are required.
2. Capture real before/after examples for connecting a device, adding an extension, branching through a strip, inserting a battery, disconnecting, relocating, and removing a device. Inspect game functions for their side effects and save behavior.
3. Establish endpoint types, maximum connections, distance limits, branching/loop rules, and day/night or battery behavior from game data and observed operations. Treat these as unresolved rules until verified; do not assume an electrical simulation from field names.
4. Create a Core graph model and validator that handles missing devices and cross-save links. A stored relationship, a live powered state, and a predicted power result must remain distinguishable.
5. Add graph visualization and inspection, then staged offline connection edits and device placement. Validate all touched records before writing and recover the whole operation if a multi-file save fails.
6. Add live operations through the game's supported connection functions, with host/capability checks, refresh after each operation, and save/reload verification. Timer values alone are insufficient: the socket-save function resets them.
7. Integrate the power graph with the construction tools so duplicating a base remaps internal identities and asks how to handle connections to devices outside the selection. *(Offline duplicate and delete now remap or refuse power links by policy; unverified in-game.)*

**Done when:** a user can build and reroute a verified power network from the map, trace its devices across files/levels, and see the same connections after loading and saving in-game. Unsupported device types and uncertain simulation results remain explicit.

## Compatibility across game versions

**Gap:** compatibility reporting does not yet establish a tested editing contract for each supported older game build. The registry records world/metadata save version 3 and character version 1 against one validated build. Its classification does not reject versions below the recorded minimum, and a matching header does not prove matching field layouts, defaults, catalogs, or gameplay behavior.

- **Support matrix:** define the older game builds to support, then record read, unchanged round-trip, each editing area, and in-game reload separately for Steam, Game Pass, and dedicated-server fixtures. Record unsupported and unverified combinations explicitly.
- **Version detection:** combine available game-build, engine/custom-version, save-class, and schema evidence. Permit an unknown result where a save header cannot identify the exact game build; do not infer it from the installed game alone.
- **Version-specific fields and defaults:** audit exact hash-suffixed property names, missing/default-valued tags, structs, enums, and item metadata. Writers must select layouts appropriate to the target save instead of inserting a current-build tag into an older structure.
- **Matching game data:** associate mappings, catalogs, level maps, extracted art, and geometry with their source game build. Prevent current item rows, recipes, actors, or building pieces from being offered as valid writes to an older game without evidence.
- **Migration policy:** distinguish editing a save in its original format from deliberately upgrading it. Define supported migrations and report unsupported downgrade requests. Changing a version number alone is not a migration.
- **Modded and unknown content:** preserve unknown rows and component state through edits, transfers, and base duplication. Show which operations are unavailable when the defining mod or compatible assets are missing.
- **Live compatibility:** track game build, agent protocol/capabilities, and UE4SS runtime independently. Enable an action only when the combination supports its required operation.
- **Regression fixtures:** maintain sanitized saves from the supported releases and confirm unchanged round-trips, narrow edits, newly created objects, and multi-file consistency. Add in-game reload evidence where serialization checks alone cannot establish behavior.

**Done when:** the editor can explain which operations are supported for the selected save/game combination and avoid unsupported writes without preventing known-safe inspection. Each claimed older-version editing capability has fixtures and recorded verification.

## Extract Game Pass support for other games and modding tools

**Gap:** the Game Pass findings and storage implementation live inside Abiotic Editor Core. Other game editors and modding tools cannot consume them through a focused, independently versioned package. This work includes migrating the code, documenting its contracts, and providing extension points for different games.

### Package boundaries

| Component | Responsibility |
| --- | --- |
| Shared Xbox storage package | Wgs index/manifests, container/blob identities, opaque payload access, observed state transitions, ETag handling, timestamps, write preflight, snapshots, backup/recovery, and commit ordering. |
| Platform integration | Package/account discovery, game-process checks, filesystem access, and lock/write-availability handling through replaceable platform services. Keep account and title selection explicit. |
| Game adapter | Payload recognition, game-specific bundles and compression, checksums, save classes, member names, account profiles, and platform conversion rules. |
| Editor/modding integration | Semantic save operations, content/version catalogs, migration steps, previews, validation, and tooling built on the storage package and a game adapter. |

Abiotic Factor's `ABF_SAVE_VERSION` bundle, headerless GVAS member reconstruction, settings encoding, and world/profile conversion belong in its adapter. Oodle should be an optional codec dependency with documented installation and licensing requirements. Other games may use different payloads, multiple blobs, integrity checks, or container layouts; discover those differences before making the current single-title assumptions public API contracts.

### Findings to carry into the package

- Document index, manifest, blob, and payload boundaries with byte layouts, sanitized examples, and the source of each observation. Label confirmed Abiotic behavior, cross-game evidence, and unresolved interpretations separately.
- Preserve service-issued ETags, their relationship to local container states, timestamp precision, upload/conflict flags, and the distinction between current and previous blob IDs. Include ambiguous or interrupted sync states in the read/write rules.
- Inventory the locking and unlocking behavior we have investigated: file sharing, active game processes, write refusal, resource acquisition/release, and storage state transitions. Specify exactly what is locked, what permits a write, and how resources are released on success, failure, or cancellation. Keep gameplay unlock/relock operations in game adapters. Local file access does not establish control over Xbox cloud sync.
- Record backup scope, write ordering, atomic file replacement, generation cleanup, orphan recovery, and conflict reporting. Make repair an explicit operation with a preview; a read must not silently repair a store.
- Define concurrent-change detection between inspection and commit. A store changed by the game or sync client must be re-evaluated before writing; multiple atomic file replacements do not constitute a transaction across the whole store.

### Migration plan

1. **Inventory dependencies and assumptions.** Map `WgsContainerStore`, `WgsSnapshot`, discovery/environment checks, `GamePassSaveSet`, codecs, and conversion code to the boundaries above. Identify hard-coded title IDs, container names, blob counts, compression formats, and account-file conventions.
2. **Capture behavior before moving code.** Assemble sanitized fixtures for ordinary reads/writes, created/modified containers, conflicting ETags, locked files, interrupted commits, missing blobs, orphaned containers, Unicode names, and recovery. Record expected outcomes and untouched-byte preservation.
3. **Define the public contracts.** Provide inspect/read, enumerate containers/blobs, plan edits, validate, commit, backup/restore, and diagnose APIs. Use typed results for lock conflicts, concurrent changes, unsupported layouts, and recovery choices. Allow injected filesystem, clock, codec, discovery, and logging services where needed.
4. **Extract generic storage first.** Move the container layer into its own project/package and route Abiotic Editor through an adapter. Preserve the published Core API where callers depend on it, using forwarding wrappers during migration. Keep CLI and desktop behavior aligned through the same package.
5. **Extract Abiotic payload handling.** Move its bundle/member/header conversion and profile semantics behind the adapter contract. Document supported source/target versions for conversion and upgrade operations, including what happens to unknown or modded data.
6. **Prove reuse with another game.** A synthetic adapter can exercise the API boundary early, but support claims require at least one second game's real sanitized fixtures and verified read/write behavior. Publish a title capability matrix; do not advertise generic write support from Abiotic fixtures alone.
7. **Package and document.** Choose package/repository naming, version the API independently, document dependency licenses and supported platforms, and provide a small CLI/example adapter. Publish the format findings as durable reference material alongside the package.

### Modding extension goals

- Allow a game adapter or plugin to register recognized save versions, custom structures, item/class catalogs, validators, and deliberate migrations. Unknown content must survive unrelated edits when its defining mod is absent.
- Expose semantic operations with parameters, a dry-run change plan, and declared target-game/version support. Route cooperative operations through the same backup, validation, and commit pipeline as built-in editing.
- Support reusable save transformations: configurable unlock/relock actions, custom item edits, base templates, and power-network layouts where the game adapter has a verified contract. Include dependent-state changes in the preview.
- Define versioned manifests, dependencies, adapter compatibility, operation discovery, and actionable errors for missing mods or codecs. Document the trust model: in-process plugins with full filesystem access are not isolated by API conventions alone.
- Treat runtime mod deployment and live editing as a further adapter capability requiring game-specific loader/protocol integration. Track installation, update/removal, authority, and persistence requirements before including them in the supported modding workflow.

**Done when:** Abiotic Editor uses the extracted package; another game integration demonstrates the boundary with real fixtures; findings and lock/state rules are documented; and a sample extension can inspect, preview, validate, and commit a semantic save change through the shared workflow. Broader runtime modding support remains a separate deliverable until a target game's adapter implements it.

## Additional gaps to track

- **Group operations and linked identities:** whole-base copy, cross-world placement, and power-network duplication need identity remapping for containers, beds/owners, teleporters, and connected devices. Define how external references are retained, rebound, or reported before a group operation is applied.
- **Power device navigation:** cables, batteries, and plug strips need a useful inspector and navigation target, including endpoints in other save files. A raw asset ID or a container-only jump is insufficient for network editing.
- **Summoned companions:** armor-set summons have no supported persistent add/move/edit workflow. Research their lifecycle and ownership before treating them as ordinary saved pets.
- **Map and asset completeness:** account for undecodable textures, unsupported meshes/materials, absent actor positions, and missing portraits. Track these by class/level/build so extraction failures become actionable gaps.
- **Coverage audit:** compare current save/property inventories, blueprint classes, catalogs, and actual UI actions. Record whether each gap is missing serialization, missing semantics, missing UI, unavailable live behavior, or missing in-game verification. Historical research lists must be checked against current code before adding an item.

## Work order and review checkpoints

1. Define the compatibility matrix, collect save/asset evidence, and establish the reusable Game Pass package and adapter boundaries.
2. Build the shared level/actor location index and full 2D floor plans; add Show on map throughout the editor.
3. Implement power graph discovery, cross-file endpoint resolution, and network inspection on those maps.
4. Prove base transforms and connection semantics, then add staged placement and power-routing edits with undo and grouped saves.
5. Add whole-base operations and the optional Three.js construction view using the same identities and coordinate system.
6. Migrate Abiotic Game Pass support into the shared package, prove a second game adapter, and deliver the documented extension example. This can progress independently once the storage fixtures and API boundaries are established.
7. Close the remaining character/account, crop/pet, story-consequence, and live-operation gaps in independently reviewable increments.

For each task, record its player-facing outcome, dependencies, evidence, remaining unknowns, and completion check. A task leaves this roadmap only when that outcome is delivered and its required verification is recorded.

## Evidence used for this review

These are implementation pointers for reviewing the gaps, not a list of completed features:

- [Compatibility registry](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/Compatibility/SaveVersionRegistry.cs) and [analyzer](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/Compatibility/CompatibilityAnalyzer.cs).
- [Level actor position resolver](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/World/DoorLocationResolver.cs).
- [Socket save fields and device links](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/WorldMapFeatures/PowerSocketMapFeature.cs) and [live socket constraints](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/LiveEditing/World/LivePowerSocketsChannel.cs).
- [Tram destination limitation](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/WorldMapFeatures/TramMapFeature.cs).
- [Unsurfaced per-player recipe entitlements](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Services/WorldMapFeatures/ServerEntitlementsFeature.cs).

- [Game Pass adapter and codecs](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Infrastructure/GamePass), the [GamePassStorage library](https://github.com/ChristopherVR/GamePassStorage), [format findings](reference/game-pass-format.md), and [save operation runner](https://github.com/ChristopherVR/AbioticEditor/blob/main/src/AbioticEditor.Core/Plugins/SaveOperationRunner.cs).
