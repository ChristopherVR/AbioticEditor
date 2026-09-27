# Known editing gaps and base-building roadmap

Status reviewed 27 September 2026 against the current save readers/writers, editor surfaces, and the documented fixture and game-data audits. The list focuses on known gaps. It is a working plan and should be updated as each item is researched and completed.

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
- **World-state maps expose only selected fields:** the existing tabs cover many maps, but some values stay display-only when their meaning or valid choices are unclear. Known examples include vehicle transforms and on-board inventory contents, power-socket timer modes, tram stations that are not represented by any currently parked tram, and actor positions that cannot safely be moved by editing only the save record. Research each map and expose only fields with a verified write contract.
- **Server entitlements:** metadata can contain `ServerEntitlements`, but there is no admin/entitlement editor. Establish the intended server semantics and permission model first.
- **Unmodeled fields vary by game build and save:** readers preserve unknown properties, but preserved data is not automatically editable. Use the compatibility report and `UNKWN` diagnostics to add concrete fields to this list when a current save or game update reveals them.

### Deployables, pets, and progression

- **Planting and clearing crops:** garden care exposes supported existing plot state, but empty-spot planting/clearing does not have a verified serialized shape. Capture real saves for empty, planted, harvested, watered, and fertilized transitions before adding those actions.
- **Pet feeding and mutation choices:** the editor provides species information and mutation guidance, but a complete semantic editor for feeding timers and mutation targets is not available. Verify the saved food identity, cooldown, progress, and valid mutation graph before adding controls.
- **Offline chemistry production:** flask contents and recipe context can be inspected and edited, but processing timers are runtime state. There is no offline control that starts a batch or fabricates elapsed production time.
- **Character story-stage names:** saved script phases can be displayed and selected, but there is no universal friendly mapping for those values. Add named controls only for character-specific stages verified from current game data.

### Live-editing gaps

Live support is separate from offline save support and depends on host authority, the running game, and the installed agent version. Keep these limits visible where a shared editor surface is used.

- The live power-socket surface is read-only because the game’s timer enum and write behavior are not sufficiently verified.
- Some deployed-object care and world-list operations are offline-only or capability-gated live. In particular, chemistry processing is not offered as a live action, and recipe/list writes depend on the connected host and agent runtime capabilities.
- Actions that depend on server RPCs, replication notifications, or game-thread behavior need in-game verification before being described as supported. Stub-harness success alone does not establish multiplayer propagation or save/reload persistence.

## Base-building editor

The current Bases experience helps find and name bench-based bases on a map. It is not a full construction editor: it does not provide a 3D scene for all placed objects or a safe workflow for moving, rotating, duplicating, snapping, and deleting arbitrary build pieces.

### Data sources and boundaries

- **World saves are authoritative for placed state.** Start with `DeployedObjectMap` and related region/world maps for object identity, class, transform, construction values, custom names, paint, power, and storage. Confirm which map owns each value and how the map key relates to the in-game actor.
- **`Mappings.usmap` describes reflected Unreal types.** Use it to resolve class/property layouts and enums, not as a source of placed-object coordinates or a complete asset catalog.
- **Mounted game paks provide presentation assets.** Use CUE4Parse to resolve meshes, textures, materials, and level data. Account for missing native decoders, unloaded assets, game updates, and modded content with honest placeholders.
- **Three.js should remain a view and interaction layer.** Keep the Core save model and writer independent of Three.js so the CLI and existing editor surfaces remain usable without a 3D renderer.

### Phased plan

1. **Close the schema inventory.** Build a representative census of placed classes and save layouts across regions, including transforms, construction mode/level, ownership/name, paint, inventory, power, and actor paths. Separate persistent fields from runtime-only fields. Record unknown and modded classes without dropping them.
2. **Prove coordinate spaces.** Determine world, level, and streamed-sublevel transforms. Compare save coordinates with installed level geometry and in-game screenshots for multiple regions and elevations. Do not enable movement until coordinate conversion is reproducible.
3. **Ship a read-only 3D viewer.** Render one region at a time with a camera, floor/region filtering, search, object selection, labels, and an inspector that links to existing semantic editors. Render unresolved classes as selectable placeholders. Keep the existing 2D Bases view available.
4. **Add selection and staged transforms.** Add move and rotate with numeric entry and gizmos, then undo/revert. Preserve identity, actor path, and unknown struct fields. Save only the selected staged changes and provide a clear before/after preview.
5. **Add safe construction operations.** After transform round-trips are proven, consider duplicate, delete, multi-select, copy/paste, snapping, alignment, and collision/overlap hints. Validate class-specific required fields and inventory links before writing.
6. **Integrate build-piece semantics.** Use verified class catalogs and game data for friendly names, icons, costs, construction levels, paint, and supported variants. Do not infer placement validity or collision from a mesh alone.
7. **Validate against the game.** Use disposable save copies: load and render, stage edits, save, reopen, compare untouched bytes/entries, then load in-game. Test multiple regions, streamed levels, large bases, missing assets, unsupported classes, and modded entries before enabling writes by default.

### Completion criteria

- Placed objects resolve to stable save identities and correct locations across the supported regions and levels.
- Unsupported assets remain visible as placeholders and survive a save unchanged.
- Move, rotate, duplicate, and delete preserve unrelated save data and can be reverted before writing.
- A saved scene agrees with the in-game result for the verified classes and transformations.
- Large regions remain usable through asset caching, culling, and incremental loading.

## Work order

1. Close high-value account and character gaps with verified schemas and fixture-backed writers.
2. Complete the story-rewind consequence map and explicitly separate reversible progression from physical world consequences.
3. Improve semantic coverage for deployed maps and pets only where the game’s persisted contract is known.
4. Establish the base-building schema and coordinate proofs before implementing 3D write tools.
5. Track each gap with evidence, player impact, data layout, implementation owner/status, and verification state. Keep unresolved hypotheses out of editable controls.
