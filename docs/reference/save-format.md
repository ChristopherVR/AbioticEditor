# How saves work

Abiotic Factor saves are Unreal Engine **GVAS** files. This section documents the format and the
contract the editor follows when it edits one. You don't need any of it to *use* the editor - it's
here for contributors, plugin authors, and the curious. For editing saves, start with the
[guide](/guide/getting-started).

## Save kinds

A world's saves live in `Worlds/<WorldName>` and come in three kinds:

| File | What it holds |
|---|---|
| `Player_<steamid64>.sav` | One character: vitals, inventory, equipment, skills, traits, recipes, codex, transmog, spawn. See the [player save schema](/reference/player-save-schema). |
| `WorldSave_<Region>.sav` | One world region: containers, doors, dropped items, NPCs, bases, world flags, and per-actor state maps. The Facility region is the large one. See the [world save schema](/reference/world-save-schema). |
| `WorldSave_MetaData.sav` | Story / metadata for the world. |

A player's SteamID64 lives in **both** the file name and the top-level `SaveIdentifier` property; the
editor keeps them in sync. Game Pass saves pack all of these into one container - see the
[Game Pass format](/reference/game-pass-format).

## The editing contract: read, mutate in place, re-serialize

For each save kind a **reader** parses the raw GVAS tree into a typed model, and a **writer** mutates
that same raw tree in place and re-serializes it. Anything the editor didn't touch is written back
**byte-for-byte** as it was found, which is why the app and CLI produce identical output and why an
edit can't disturb fields the editor doesn't model.

Two rules follow from how the game stores data, and they shape every edit:

- **Delta-serialization.** The game omits any property still at its blueprint default, so a healthy
  save legitimately has missing tags. A reader can't assume a field is present.
- **Hash-suffixed names.** Property names carry blueprint-compiler hash suffixes (e.g.
  `Hunger_2_A6C5CC6E…`) that can change across game patches. Readers therefore **match by prefix**
  (`Hunger_`), while writers that need to *create* a missing tag use its exact full hash-suffixed
  name.

## In this section

- [Player save schema](/reference/player-save-schema) - every field in `Player_<id>.sav`.
- [World save schema](/reference/world-save-schema) - the region saves and their state maps.
- [Game Pass format](/reference/game-pass-format) - how the Xbox container packages all of the above.
- [Research notes](/reference/research/research-slot-types) - working notes behind the schemas.
