# Live parity and performance review (2026-09-15)

## Cascade runtime results

Verified against the user's Cascade world in singleplayer, game 1.4.0.28206. The original
75 save files were backed up before launch and restored after the game closed, with all
SHA256 hashes matching. The temporary development hook and test helper were retired.

- Base enumeration returned 1,861 deployables in 229 ms after replacing full item decoding
  with row-name reads for occupied-slot counts. Before the fix it exceeded five seconds.
- That timeout exposed a late-response bug: the next world request received the base list.
  Native/Lua mailbox request IDs now prevent stale responses from answering later requests.
  Both components must be updated together. Native timeout/correlation regression passes.
- Host recipe removal worked, but the old unlock RPC silently failed to restore the recipe.
  Direct authoritative-array updates now handle both directions, with runtime readback passing.
- Global recipe removal/restoration passed with both sets matching their original contents.
  GatePal email clear/restore and an unchanged rich inventory-details write also passed.
- The real buff-handle factory and character buff component resolved in game. Trait effect
  mutations remain unverified, so trait editing is still disabled.

563 Lua checks pass. These results cover agent readback in one host session; full save/reload,
multiplayer propagation, and the remaining parity gaps below still need implementation/testing.

Source review of the shared player/world tabs, live session adapters, Core channels, Lua
handlers, polling, catalog/image services, and save export/index services. This is an initial
implementation pass, not a complete gameplay certification or a measured memory profile.

## Feature comparison and remaining work

| Area | Offline | Live gaps after this pass |
| --- | --- | --- |
| Inventory | Full saved slot data | Ammo, liquid type/level, custom strings, asset IDs, visual variants, dynamic property arrays, gameplay tags, coating fields, and item-table overrides now all cross both inventory channels (`inventory.setcomplete`/`containers.setcomplete`), including a direct player-to-container transfer (`inventory.transfer`). Implemented 2026-09-16; still needs in-game persistence verification. |
| Character | Appearance, background, traits | Background, traits, and appearance are all editable live now (`general.trait.set`, `appearance.get/set/save`). Implemented 2026-09-16; still needs in-game verification. |
| General | Owner ID, discoveries, counters | Owner identity remains save-only. Hosts can now add crafted-item discoveries when the agent reports support. |
| Recipes | Unlock and relock | Hosts can now relock. Bulk unlock retains batching through the shared player facade. |
| GatePal | Set and clear known state | Hosts can now clear supported known entries. Kill-only compendium entries remain read-only. Mark All is batched. |
| Skills | Saved skills and progress | Live writes depend on existing supported game skill entries. |
| Spawn | Saved region, bed, terminal, coordinates | Live teleport and terminal selection exist; saved-world integration is unavailable. |
| World story | Flags, clock, metadata, global unlock arrays | Global recipe editing is implemented for hosts with TSet support. World play time is now settable (`world.setPlaytime`), implemented 2026-09-16, still needs in-game verification. Full cross-file revert equivalence remains open. |
| Bases | Bench upgrades and deployable data | Bench-upgrade installation and removal are both implemented (2026-09-16), writing the bench's own GameplayTag container directly instead of the crash-prone native calls. Still needs in-game verification. |
| Deployed care | Garden plots, Power Chairs, chemistry benches | Watering/fertilising/growth-stage edits, Power Chair charge, and chemistry-bench flask readouts are implemented (2026-09-16) via each deployable's own save-aware functions. Still needs in-game verification. |
| Pets | Saved species and state | Species changes unsupported. Some companion follower families cannot be matched for despawning. |
| Doors, containers, vehicles, NPCs, portals | All persisted region entries | Live scope is loaded actors and available host authority; not all saved fields have live equivalents. |
| Raw data, entitlements, identity, backup/undo | Save-file operations | No general live equivalent. These should not be enabled through speculative game writes. |

Next implementation priority is full inventory metadata support with field capabilities, so
the shared UI cannot offer silent no-op edits. It needs game-backed tests for row handles,
enums, and moved-item metadata. Follow with explicit capability reporting for the remaining
world/player operations. Do not infer support from the presence of a tab alone.

## Performance findings and changes

- The shared player facade inherited the default recipe bulk implementation. That fell back
  to one request per recipe even though the underlying live session supported batching.
  Explicit forwarding now preserves one request. Already unlocked IDs are omitted, and batch
  model updates use a dictionary rather than scanning the entire recipe list for each ID.
- GatePal Mark All used one request and notification per editable entry, including entries
  already known. It now snapshots unknown editable rows, validates ownership before writing,
  groups all sections in one request, and publishes one update after success. Failed requests
  do not mark the local rows known. Game RPC batches are not claimed to be transactional.
- Item-icon and game-texture caches used ConcurrentDictionary factories that immediately
  started async extraction. Competing factories could launch duplicate decodes even though
  only one task stayed cached. Lazy tasks now start extraction only for the retained entry.
  Unknown item URLs no longer accumulate null entries in the item-icon cache.
- Existing strengths: player areas connect lazily, polling refreshes the active area,
  inventory refresh updates selected slot objects in place, icon decode concurrency is
  bounded, and the level index reads save tails instead of whole region saves.
- Remaining candidates for profiling: some map/actor
  caches still start work inside dictionary factories, several asset services mount their
  own providers, and ZIP export buffers output. No measured peak-memory or startup-time
  improvement is claimed by this pass. Benchmark these with the same save/catalog and a
  running game before broader architectural changes.

## Verification scope

Regression tests cover bulk request counts through the actual shared facade, duplicate and
already-known recipes, multi-section codex batches, failed codex writes, and ammo read/edit/move
round trips. Lua harness cases cover ammo validation, omission, clearing, and invalid batches.
Magazine field names come from PlayerSaveWriter.FullNames, not guessed property names.
Host and tests built successfully. The full suite passed 1,292 tests, with three failures
caused by sandbox restrictions on AppData writes and one missing-Lua skip. All three blocked
tests passed on approved reruns. A focused run passed 21 live/progression/inventory tests.
The Lua cases were added but could not execute without a Lua 5.4 interpreter. No installed
game files or existing saves were changed; conversion tests created and removed test worlds.

## Full-parity follow-up (2026-09-15)

Full parity remains the requested target and is not complete. New operations are wired through
Core channels, session capabilities, and shared UI. Direct progression writes preflight the
replication helper, then mark changed properties for push-model replication and invoke local
RepNotify functions where present. A missing helper disables progression capabilities and
rejects rich inventory writes before mutations. Rich commands have distinct names so old agents
cannot acknowledge a write while silently dropping its new fields.

Container swaps now send both sides in one request and refresh once. Unchanged GatePal polls
retain existing row objects and skip rebuilding titles, bodies, and sorted lists.

The full .NET suite passed 1,307 tests with one standalone-Lua skip. The Lua 5.4 harness was run
separately through a workspace-local Lupa runtime: 553 checks passed. Nine focused .NET tests
passed after adding container metadata and world-recipe interface coverage. The host builds.
These are protocol/session checks, not proof of game persistence or client replication.
No game was running and no existing game save was edited during this follow-up.

### Implemented, pending in-game verification (2026-09-16)

The four items below were the top of the previous "remaining implementation" list; all four
landed in the feature/live-editing-parity change and pass the editor's own protocol/session and
Lua tests, but none has been exercised against a running game yet.

1. **Item metadata fidelity.** All item dynamic properties, gameplay tags, coatings, and
   item-table overrides now travel through `inventory.setcomplete`/`containers.setcomplete`
   (`Scripts/item_metadata.lua`) across swaps, sorting, upgrades, and the new cross-inventory
   `inventory.transfer`. Needs in-game verification that clear/reuse does not carry old item
   state into a new item.
2. **Traits.** `general.trait.set` updates `CharacterProgressionComponent.Traits` and calls
   `Server_AddTraitBuff`/`Server_RemoveTraitBuff` with the row's real buff handle, without
   replaying `InitializeTraits` (which also grants items and changes skills). Needs in-game
   add/remove, reconnect, and save/reload verification.
3. **Appearance.** `appearance.get`/`appearance.set`/`appearance.save` write
   `HumanCustomizationComponent`'s real per-slot row-handle fields directly and call each
   field's own `OnRep_<Property>`, with `appearance.save` persisting the local profile through
   the game's own `SaveGameToSlot`. Needs in-game verification of both visible appearance and
   saved-profile state.
4. **Bench upgrades.** `Scripts/bench_tags.lua` installs and removes upgrades by writing the
   bench's own `GameplayTag` container directly, replacing the native `Has Upgrade`/`AddUpgrade`
   calls that crashed the bridge. Needs in-game verification that install/remove/reconnect all
   report the same state.

### Remaining implementation and game verification

1. Multiplayer propagation of every new write above (item metadata, traits, appearance, bench
   upgrades, deployed care, world play time, and the player/container transfer): confirm a
   second connected client sees each change, not just the host's own readback.
2. Save/reload persistence for each new write: confirm the change survives a world save and
   reload, not only an immediate in-session readback.
3. Pet species changes, and matching the remaining companion follower families for despawning.
4. Owner identity: renaming which save file a character belongs to has no running in-game
   concept to change, and stays a file-only operation.
5. Raw save data, entitlements, backups, and undo: these are save-file operations with no live
   equivalent, and should not be simulated through speculative game writes.
6. In a disposable copied world, verify every implemented-pending-verification item above via
   agent readback, visible game state, a second connected client where applicable, and
   save/reload. Exercise host/client capability changes and an older agent.

Research is reproducible with LiveParityClassProbe and LIVE_PARITY_PROBE_OUT. The generated
exports stay under uncommitted artifacts; only the opt-in probe is source-controlled.
See [the protocol](../live-editing-protocol.md) for current wire fields and upstream API evidence.
