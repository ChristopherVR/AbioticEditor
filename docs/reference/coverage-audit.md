# Coverage audit of the known gaps

Each gap in `docs/ROADMAP.md` is classified by what is actually missing. A gap can have more than one cause; the
**primary** blocker is listed first, because that is what has to be closed before the next stage can start.

Categories:

- **Serialization**: the reader/writer cannot represent the data, or the serialized shape is unconfirmed (no fixture
  shows the field or the state transition).
- **Semantics**: the data is read and preserved, but its meaning (valid range, relation to other state, consistent
  updates) is not established.
- **UI**: the meaning is understood and a writer exists (or would be trivial), but no editor control exposes it.
- **Live**: the behavior only exists in a running game (runtime state, game-thread or RPC behavior); an offline editor
  cannot provide it.
- **In-game verification**: implemented and fixture-tested, but nobody has recorded reloading it in the game.

Method: each row was checked against current code with `grep` over `src/` (reader/writer/catalog/UI references), not
copied from the historical research notes. A "recognized and preserved" gap means the property name appears in a
reader but in no writer or UI file.

## Player and account saves

| Gap | Primary | Also | Evidence in code |
| --- | --- | --- | --- |
| Per-slot transmog disable flags (`TransmogDisabledArray_`) | Semantics | UI | Name appears only in `PlayerSaveReader`. Index-to-slot mapping unknown. |
| Favorited slots / distillery history (`FavoritedSlots_`, `ItemsDistilled_`) | Semantics | UI | Only in `PlayerSaveReader`. Consistency when slots move is undefined. |
| Current buffs and debuffs | Semantics | Serialization | No reader/writer references. Which effects persist, and their duration/stack rules, are unknown. |
| Last hotbar selection (`LastHotbarSelection_`) | Serialization | Semantics | No reference anywhere in `src/`: not modeled, so it lands in the unknown-property report. |
| Customization unlock state (`Unlocks.sav`) | UI | Semantics | Save class `Abiotic_CustomizationUnlocks_Save_C` parses and round-trips (fixture) and is carried by conversion; no editor. |
| Account stats / achievement mirror (`PlayerStatsSave.sav`) | Semantics | UI | Carried by Game Pass conversion only. Relation to Steam achievements undefined. |
| Account settings (`UserSettings.sav`) | UI | Semantics | Carried by conversion only; password-like host settings need a redaction rule first. |

## World and placed-object state

| Gap | Primary | Also | Evidence in code |
| --- | --- | --- | --- |
| Story rewind consequences | Semantics | In-game verification | Flag rewind exists; physical consequences (doors, dead characters) have no reviewed consequence map. |
| Narrative character details | Semantics | UI | NPC map is read and preserved; per-character meaning of stages and `IsDead` unverified. |
| Complete tram destinations | Serialization | Semantics | Station picker derives choices from occupied stations only; the full catalog lives in level assets, which need game paks (unavailable here). |
| Static world-object placement | Live | Semantics | Level-placed actors are not moved by a saved position; relocation needs a runtime operation. |
| Per-player recipe entitlements (`UserEntitlements`) | UI | Semantics | Displayed by `ServerEntitlementsFeature` (map feature) but no editor; relation to recipe unlocks unresolved. |
| Unmodeled fields per game build | Serialization | none | Detected by `CompatibilityReport.UnknownPropertyKeys`; each new key must become a concrete row here. Preserved, not editable. |

## Deployables, pets, progression

| Gap | Primary | Also | Evidence in code |
| --- | --- | --- | --- |
| Planting / clearing crops | Serialization | In-game verification | No planting code; the empty/planted/harvested transition shapes are not captured in fixtures. |
| Pet feeding and mutation choices | Semantics | UI | `PetCareCatalog` and `PetGameData` give guidance; feeding timers and the mutation graph are unverified. |
| Offline chemistry production | Live | none | Processing timers are runtime state; an offline editor cannot start a batch. |
| Character story-stage names | Semantics | UI | Stage strings preserved; no verified friendly mapping. |
| Summoned companions | Semantics | Live | Lifecycle and ownership not researched. |
| Group operations and linked identities | Semantics | UI | Identity remapping rules for containers, beds, teleporters and devices undefined. |
| Power device navigation | UI | Semantics | Socket data is read (`PowerSocketMapFeature`); no cable/battery inspector or cross-file endpoint navigation. |
| Map and asset completeness | Live | UI | Depends on undecodable textures, meshes and actor positions from game paks. Not trackable per class/level/build until an extraction failure log exists. |

## Live-editing gaps

| Gap | Primary | Also | Evidence in code |
| --- | --- | --- | --- |
| Durable live power changes | Live | In-game verification | Socket timers are reset by the game's socket-save function; needs real connect/disconnect operations. |
| Offline chemistry transfers | Semantics | UI | No staged flask transfer through the transfer path; complete flask metadata preservation undefined. |
| Unloaded actors | Live | Semantics | Live discovery sees loaded actors only; stale-after-streaming behavior not modeled. |
| RPC / replication / game-thread actions | In-game verification | Live | The Lua harness proves dispatch, not multiplayer propagation or save/reload persistence. |
| Live compatibility tracking | Live | Serialization (wire) | Protocol and agent version are tracked; game build, capabilities and UE4SS version are not reported by `hello`. See the support matrix. |

## Cross-cutting

| Gap | Primary | Notes |
| --- | --- | --- |
| In-game reload evidence for every edit area | In-game verification | No dated reload notes are recorded per platform. See `compatibility-support-matrix.md`. |
| Older-build fixtures | Serialization | No fixture is older than engine changelist 1030001, so older-layout writers cannot be verified. |
| Game-data catalogs tied to a build | Semantics | Catalogs are read from the installed game, so they describe that install, not the save's build. Nothing yet stops offering a current catalog row as a write to an older save; the operation-support verdict is the gate a host should consult. |

## Reading the totals

By primary cause: Semantics dominates (about half the rows). Those are research tasks that need saves captured in
specific game states, not code. Serialization gaps (last hotbar selection, crops, buffs, tram catalog, unmodeled keys)
need new fixtures first; do not add writers before a real fixture shows the shape. UI-only gaps (unlock state,
entitlements, power navigation, settings) are the cheapest to close because the data and meaning are already known
or the data is already read.
