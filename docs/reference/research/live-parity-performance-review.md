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
| Inventory | Full saved slot data | Ammo now crosses the live channel. Liquid type/level, custom strings, asset IDs, and visual variants now cross both inventory channels. Dynamic property arrays, gameplay tags, coating fields, and complete moved-item fidelity remain open; the new metadata paths still need in-game persistence verification. |
| Character | Appearance, background, traits | Background supported; traits remain read-only and full saved appearance is not exposed through the live player facade. |
| General | Owner ID, discoveries, counters | Owner identity remains save-only. Hosts can now add crafted-item discoveries when the agent reports support. |
| Recipes | Unlock and relock | Hosts can now relock. Bulk unlock retains batching through the shared player facade. |
| GatePal | Set and clear known state | Hosts can now clear supported known entries. Kill-only compendium entries remain read-only. Mark All is batched. |
| Skills | Saved skills and progress | Live writes depend on existing supported game skill entries. |
| Spawn | Saved region, bed, terminal, coordinates | Live teleport and terminal selection exist; saved-world integration is unavailable. |
| World story | Flags, clock, metadata, global unlock arrays | Global recipe editing is implemented for hosts with TSet support. Minutes-passed editing and full cross-file revert equivalence remain open. |
| Bases | Bench upgrades and deployable data | Bench-upgrade installation remains disabled after a native crash report. Requires grounded game API research before enabling. |
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

### Remaining implementation and game verification

1. Preserve all item dynamic properties, gameplay tags, coatings, and item-table overrides
   through swaps, sorting, upgrades, and cross-inventory transfers. Verify that clear/reuse
   does not carry old item state into a new item.
2. Traits: the exported SetTraits writes the list and preserves Sundisk. InitializeTraits
   calls Server_AddTraitBuff, but also grants items and changes skills. Implement incremental
   effect updates using real buff row handles, then test add/remove, reconnect, and save/reload.
   This corrects earlier notes claiming trait buffs were unrelated to selected traits.
3. Appearance: map Server_ApplyCustomizationChange's real row-handle, customization enum,
   voice object, and vector parameters, then verify both visible appearance and saved state.
4. Bench upgrades: obtain real upgrade handles and trace install/remove effects. Keep the
   previously crashing path disabled until an isolated runtime test establishes safety.
5. Complete world clock counters, species changes, follower despawning, loaded/unloaded actor
   behavior, and story-revert consequences. Check each against the offline writer's fields.
6. Decide explicit live semantics for save-file operations: account identity, raw file data,
   entitlements, backups, and undo cannot be represented as arbitrary connected-player edits.
7. In a disposable copied world, verify every new edit via agent readback, visible game state,
   a second connected client where applicable, and save/reload. Exercise host/client capability
   changes and an older agent. A test-world/launch question is pending in the conversation.

Research is reproducible with LiveParityClassProbe and LIVE_PARITY_PROBE_OUT. The generated
exports stay under uncommitted artifacts; only the opt-in probe is source-controlled.
See [the protocol](../live-editing-protocol.md) for current wire fields and upstream API evidence.
