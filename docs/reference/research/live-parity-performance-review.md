# Live parity and performance review (2026-09-15)

Source review of the shared player/world tabs, live session adapters, Core channels, Lua
handlers, polling, catalog/image services, and save export/index services. This is an initial
implementation pass, not a complete gameplay certification or a measured memory profile.

## Feature comparison and remaining work

| Area | Offline | Live gaps after this pass |
| --- | --- | --- |
| Inventory | Full saved slot data | Ammo now crosses the live channel. Liquid type/level, custom strings, variants, and other instance metadata still lack complete player inventory round trips. Several shared controls currently appear despite those limits. Transfers of these richer items need further work. |
| Character | Appearance, background, traits | Background supported; traits remain read-only and full saved appearance is not exposed through the live player facade. |
| General | Owner ID, discoveries, counters | Owner identity remains save-only; crafted-item discovery cannot be forced live. |
| Recipes | Unlock and relock | Unlock only. Bulk unlock now retains batching through the shared player facade. |
| GatePal | Set and clear known state | Supported sections unlock only; kill-only compendium entries remain read-only. Added a batched Mark All operation. |
| Skills | Saved skills and progress | Live writes depend on existing supported game skill entries. |
| Spawn | Saved region, bed, terminal, coordinates | Live teleport and terminal selection exist; saved-world integration is unavailable. |
| World story | Flags, clock, metadata, global unlock arrays | Global recipe editing and minutes-passed editing unavailable; no full equivalent to cross-file offline revert. |
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
- Remaining candidates for profiling: GatePal rebuilds rows on every poll, some map/actor
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
