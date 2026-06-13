# Research: dedicated-server save tree ("Cascade" endgame world)

Probe: `tests/AbioticEditor.Tests/ServerSaveDeepDiveTests.cs` (run 2026-06-12).
Subject: `D:\Development\uesave\b31789b5-37b6-4f61-9053-9529ce64722b\SaveGames\Server` -
a dedicated-server world that has finished the main story (`StoryProgressionRow=EndGame`,
47 009 minutes) and covers every late-game region the checked-in fixture and the client
tree never reach. All 374 `.sav` files (live + all 5 backup generations) were parsed,
round-tripped and schema-walked. Compare with `research-new-save-gaps.md` (client tree).

## 1. File tree and layout

```
SaveGames/Server/                          ← server root is literally "Server", no steamid64
├─ Admin.ini                               171 B  [Moderators]/[BannedPlayers] steamid lists
├─ Worlds/
│  └─ Cascade/                             63 .sav + 1 ini, 68.1 MB total (live saves)
│     ├─ SandboxSettings.ini               363 B  (per-WORLD, not per-server - see §5)
│     ├─ PlayerData/Player_<steamid64>.sav ×4 [Abiotic_CharacterSave_C]
│     ├─ WorldSave_MetaData.sav            [Abiotic_WorldMetadataSave_C]  story=EndGame
│     └─ WorldSave_*.sav                   ×58 [Abiotic_WorldSave_C]
└─ Backups/
   └─ Cascade/1..5/                        full copies of Worlds/Cascade (incl. PlayerData
                                           and SandboxSettings.ini), ~67.6 MB each
```

- Engine header identical across all three trees: GVAS v3, UE4 pkg 522 / UE5 pkg 1012,
  engine `5.4.4--2146453646 '++DF+ABF'`, 74 custom formats. **The server is the same
  game build as the fixture** (the client `Worlds/Chrissie` tree remains the only
  newer-build sample, `-2146453647`).
- ABF custom header on every world/metadata save: `ABF_SAVE_VERSION v3/id1` - same as
  fixture and client. Player saves carry the bare `Version+DataLength` character header
  (no `ABF_SAVE_VERSION` marker) in all three trees.
- **No new save class.** Server saves use the exact class paths we already register
  (`Abiotic_WorldSave_C`, `Abiotic_WorldMetadataSave_C`, `Abiotic_CharacterSave_C`).
  No `[SaveClassPath]` addition needed.

### Layout differences vs the client tree (`Saved/SaveGames/<steamid64>/...`)

| | Client | Server |
|---|---|---|
| Root folder | `<steamid64>/` | `Server/` (under a GUID install dir) |
| Account-level saves | `PlayerStatsSave.sav`, `ScientistCustomization_1.sav`, `Unlocks.sav`, `UserSettings.sav`, `steam_autocloud.vdf` | **none** - only `Admin.ini` |
| PlayerData | `Worlds/<World>/PlayerData/` (host only) | `Worlds/<World>/PlayerData/` - one `Player_<steamid64>.sav` **per connected player** (4 here) |
| `WorldSave_MetaData.sav` | per world folder | per world folder (same) |
| `SandboxSettings.ini` | per world folder | per world folder (same), copied into each backup |
| Backups | `Backups/<World>/1..5` | `Backups/<World>/1..5` (same scheme) |
| `Admin.ini` | present (same format) | present - moderator/ban steamid lists |

So the *world-folder* layout is identical; only the root differs (no account folder, no
account-level saves). The editor's folder scanner (`SaveFolderScanner.Scan` is recursive
from any root) and `Fixtures`-style discovery work unchanged; a "open server folder"
flow only needs to accept a root that contains `Worlds/` without a steamid64 segment.

### Backup generations

Each generation is a complete snapshot (62–63 `.sav` + `SandboxSettings.ini`). Content
dating (file mtimes were flattened by the copy): gen 1 MetaData is `story=DarkLens`,
42 689 min - older than live (`EndGame`, 47 009 min); gens 1–4 predate the world's
first visit to `WorldSave_V_ISLAND.sav` (the post-boss island), which only gen 5 and
live contain. So generation 5 is the newest snapshot here.

### Region coverage vs fixture

Server-only regions (17, absent from `tests/fixtures/Cascade/`):
`Facility_Botanical`, `Facility_DarkFusion`, `Facility_DF_Central`, `Facility_DF_Labs`,
`Facility_DF_Overgrowth`, `Facility_DF_War`, `Facility_Fracture`, `Facility_Plant`,
`Facility_Pool`, `Facility_Residence`, `V_Anteverse_C`, `V_BOTANICAL`, `V_Inq`,
`V_ISLAND`, `V_Signal`, `V_SUOMI`, `V_TheWall`. The fixture has nothing the server
lacks. `V_ISLAND` is absent even from the client Cascade tree - this is the only tree
with a post-final-boss island save.

## 2. Parse results (typed readers)

| Reader | Files | Result |
|---|---|---|
| `PlayerSaveReader` | 8 (4 live + 4 backup gen 1) | **All OK.** skills=15, equip=13, hotbar=8; PhDs HumanBio ×2, TheoreticalPhys, NutritonalSci; recipes up to 470. |
| `WorldSaveReader` | 120 (60 live + 60 backup gen 1, incl. MetaData) | **All OK.** Largest: live Facility - 292 containers, 214 flags, 728 dropped, 947 deployables. |

No parse failures anywhere, including every late-game region.

## 3. Round-trips - nothing would corrupt a server save today

- **Binary:** all **374** server `.sav` files (live + every backup generation)
  round-trip **byte-identical** through `SaveGame.LoadFrom` -> `WriteTo`.
- **JSON** (`SaveJsonBridge` / `SaveGameSerializer`) on the sample set - MetaData,
  small world (`H_Japan`), the 16.5 MB `Facility` (-> 133 MB JSON), late-game
  `DF_Labs` (-> 18.7 MB JSON), and a player save (-> 1.1 MB JSON) - **byte-identical**,
  and the `ABF_SAVE_VERSION v3/id1` header survives export+import. The serializer fix
  (commit 1917a22 + the registered `AbioticWorldSaveJsonSerializer` /
  `AbioticCharacterSaveJsonSerializer`) holds on server saves.

Caveats (pre-existing, not server-specific): the delta-serialization writer no-op
issue from `research-new-save-gaps.md` §5 still applies to fresh characters, and the
133 MB Facility JSON is far beyond what the in-app raw-JSON text tab should attempt
(use the export-to-file path).

## 4. Schema findings (late-game regions vs fixture)

**Nothing is version-new** - every top-level key in every late-game region already
appears somewhere in the fixture (same game build). Two keys are however *unmodeled
and previously undocumented* because the earlier deep dive only walked
`WorldSave_Facility`, which carries neither:

- **`DayDiscovered` (IntProperty)** - on *every* region save except `Facility` itself
  (40 fixture files have it too). Day the level was first entered. Trivial to model;
  read-only display value for a "world map" view.
- **`CorpseMap` (MapProperty)** - `Map<Str actorPath, {ActorPath (SoftObjectPath),
  IsGibbed (Bool), IsLooted (Bool)}>`; NPC corpses, e.g.
  `...PersistentLevel.CharacterCorpse_OrderSniper_C_1`. Present in 9 server and 5
  fixture files. Harmless to leave unmodeled (round-trips untouched).

-> both should be added to `docs/world-save-schema.md` and the deep-dive tests'
`KnownUnmodeledWorldKeys`.

Late-game *content* the fixture lacks (all parse fine with existing readers):

- **67 deployable classes new to the server tree** (fixture 212 -> server 262).
  Functional ones the base-manager UI will now meet: `Deployed_TeleporterPad_C`,
  `Deployed_LaserEmitter_C` / `Deployed_LaserPowerConverter_C` /
  `Container_LaserCollector_C`, `Deployed_NeutrinoEmitter_C`,
  `Deployed_ChargingPad_C` / `Deployed_StorageCrate_Charging_C`,
  `Deployed_GatekeeperForceField_C`, `Deployed_Inhibitor_PestScarecrow_C` /
  `Deployed_Inhibitor_RadioScrambler_C`, `Deployed_PestTrap_C`,
  `Deployed_Moisture_Teleporter_C`, `Deployed_StorageCrate_Makeshift_T4_C`,
  `GardenPlot_Large_C`, `Faucet_Alien_C`, plus ~40 cosmetics (VOTV crossover
  furniture, bobbleheads, figurines, cubicles).
- **8 narrative-NPC actor classes new to the server tree** (fixture 7 -> server 15):
  `NarrativeNPC_ExorChieftain_C`, `NarrativeNPC_HammeringHank_C`,
  `NarrativeNPC_Kyliebot_C`, `NarrativeNPC_Nibbles_C`,
  `NarrativeNPC_Penguin_VWinter_C`, `NarrativeNPC_SignPost_VWinter_C`,
  `NarrativeNPC_UnlostMage_C`, `NarrativeNPC_Waterbot_C`. (`NPCClass` inside the
  struct serializes as `None` - the actor class must be taken from the map key.)
- **105 world flags new to the server tree** (fixture 109 -> server 214): whole
  late-game chapters - `Dams_*` (pumps/Kylie/witch), `Fracture_DL_*` (Dark Lens eyes
  and bosses), `Plant_*` (Anteverse C, warhead), `Reactors_*` (~40 flags for the
  DF reactor-station sequence), `EndBoss*`/`End_*` (incl. `End_MainStoryComplete`,
  post-boss island NPC meets), `KylieVocalizer_Activated`, `Flathill_CornWeb`.
  `QuestFlagCatalog`/story-sync should be checked against this list - it is the first
  complete-story flag inventory we have.
- Value-struct shapes (`DeployedObjectMap` 18 fields, `NarrativeNPCMap` 8 fields,
  `DestructibleMap`, `NPCSpawnMap` 7 fields) are identical to the client tree census -
  no field drift.

## 5. `SandboxSettings.ini` - yes, worth an editor feature

Per-world, plain ini, `[SandboxSettings]` section. Keys observed:

| Key | Effective value here |
|---|---|
| `GameDifficulty` | 3 |
| `EnemySpawnRate` | 1.5 |
| `PlayerXPGainMultiplier` | 1.5 |
| `ItemStackSizeMultiplier` | 3.0 |
| `RefrigerationEffectivenessMultiplier` | 3.0 |
| `SinkRefillRate` | 2.0 |
| `DamageToAlliesMultiplier` | 0 |

Quirk that matters for an editor: **the game appends rather than rewrites** - the file
contains two blocks and 6 of 7 keys appear twice (last occurrence wins). An editor
should parse last-wins, and on save either rewrite the file canonically (the game
accepts a clean single block - the first block here was clearly hand-written) or
append, but never naively "update the first match". Low effort, high value for server
admins: it's the only sandbox-settings surface outside the in-game host menu, and the
same file/format exists in client worlds too.

`Admin.ini` (server root): `[Moderators] Moderator=<steamid64>` lines and
`[BannedPlayers] BannedPlayer=<id>` lines - equally trivial to expose alongside it.

## 6. Prioritized gaps / actions

1. **No corruption risk found.** Binary and JSON round-trips are clean on all server
   files; safe to edit with today's code. (The JSON-import header loss documented in
   `research-new-save-gaps.md` §4 is confirmed fixed.)
2. **Adopt this tree as the checked-in fixture** (see below).
3. Doc/test upkeep: add `DayDiscovered` + `CorpseMap` to `world-save-schema.md` and to
   `KnownUnmodeledWorldKeys` in the deep-dive tests.
4. `QuestFlagCatalog`/`StoryProgressionCatalog`: extend with the 105 endgame flags and
   the `EndGame` story row (first sample of a completed story).
5. SandboxSettings.ini + Admin.ini editor panel (last-wins parse, canonical rewrite).
6. Friendly names for the new functional deployables (teleporter pads, laser network,
   charging crates) in whatever catalog drives the base-manager display.

## 7. Fixture recommendation

**Yes - this tree is a strictly better fixture than the current
`tests/fixtures/Cascade/`:**

- Same game build (engine build `-2146453646`) - swapping does not change version
  coverage, it only adds content.
- Superset of regions: 17 extra saves including all DarkFusion/Fracture/Botanical and
  the unique post-boss `V_ISLAND` (absent even from the client tree).
- Completed story: `EndGame` row, end-boss + post-boss flags, 105 extra flags,
  67 extra deployable classes, 8 extra NPC classes, `CorpseMap` populated.
- Adds the server-specific artifacts (`Admin.ini`, per-world `SandboxSettings.ini`
  with the append quirk) - good test material for §5's feature.
- Cost: live `Worlds/Cascade` is 68.1 MB vs the current fixture's 51.9 MB (backups not
  needed in a fixture; keep one small backup generation only if rotation logic ever
  gets tested). Keep `Fixtures.CascadeDir` pointing at the world folder
  (`.../Worlds/Cascade` content moved to `tests/fixtures/Cascade`) and existing
  tests keep working - file names are a superset and `WorldSave_MetaData.sav` (the
  discovery sentinel) is present. Hash-suffix-prefix matching means no reader changes.
  Tests that assert exact counts/story values against the old fixture would need
  re-baselining (e.g. story row `EndGame` vs the old fixture's value).
