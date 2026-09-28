# Compatibility support matrix

What the editor can prove for each fixture family, per platform. Every cell says what evidence exists; anything not
proven is written as **unverified**, never left blank. Code: `SaveVersionRegistry`, `SaveHeaderEvidence`,
`CompatibilityAnalyzer` and `OperationSupportEvaluator` under `src/AbioticEditor.Core/Services/Compatibility/`.

## What a save header can identify

Every GVAS header carries the container version (3), the UE4/UE5 object versions (522 / 1012), the engine version
(5.4.4), an engine changelist with a licensee bit, the branch (`++DF+ABF`), and a 74-entry custom-version table. It does
**not** carry the game's own version string or the build hash, so the exact game build can never be read from a save.
The editor therefore reports one of four results (`BuildIdentification`):

| Result | Meaning |
| --- | --- |
| `Unknown` | No readable header evidence. The exact build is not known. Nothing is inferred from the installed game. |
| `ValidatedEngineBuild` | Changelist 1030002, the build the mappings and write paths were validated against. |
| `ObservedEngineBuild` | Changelist 1030001: seen in fixtures, byte-exact round-trip proven, no per-area edit evidence. |
| `UnrecognizedEngineBuild` | Header parsed, build never seen. Higher than every known build: writes unverified. Lower than every known build: writes unsupported. |

Build stamps are **per file**. The game restamps a region only when it saves that region, so one world folder can mix
changelists (the Steam client "Chrissie" world is 1030001 apart from a few stragglers; the rest is 1030002). Classify
each file, not each folder.

Older or newer ABF_SAVE_VERSION values are handled separately: below the recorded minimum is `OlderVersion` (writes
unsupported), above the maximum is `NewerVersion` (writes unverified). Evidence combined into a report: ABF version,
engine build and custom-version fingerprint (header), save class (kind), and unknown flags/keys/enums (schema).

## Fixture inventory

Engine changelist counts come from `SaveHeaderEvidence` over every `.sav` under `tests/fixtures/`.

| Fixture family | Files | Engine build | Custom-version fingerprint |
| --- | --- | --- | --- |
| Dedicated server (`DedicatedServerSaves/Worlds/Cascade`) | 64 | 1030002 (64) | one shared table |
| Steam client (`SteamSaves/SaveGames`) | 88 | 1030002 (66), 1030001 (22) | one shared table |
| Steam legacy standalone world (`SteamSaves/Legacy/Cascade`) | 47 | 1030002 (45), 1030001 (2) | one shared table |
| Game Pass wgs container (`GamePassSaves`) | 1 container | derived from a Steam save, see below | same as its source |

All three save families share one custom-version table, so no fixture demonstrates a schema change between builds.
"Older game build" support therefore currently means "1030001 and 1030002 only"; nothing older exists in the fixtures.

## Matrix

Legend: **proven** = fixture test asserts it; **unverified** = no evidence recorded; **n/a** = does not exist for that
platform.

| Column | Steam client | Steam legacy | Dedicated server | Game Pass |
| --- | --- | --- | --- | --- |
| Read (parse to typed model) | proven, all files (`CompatibilitySupportTests`, reader tests) | proven | proven | proven for the bundle members the fixture holds |
| Unchanged round-trip, byte for byte | proven, every `.sav` (`Every_fixture_round_trips_byte_for_byte_and_has_identified_header`) | proven | proven | proven for decoded members (`Game_pass_fixture_members_round_trip_when_the_bundle_can_be_read`; skips if the Oodle library is missing) |
| Edit: player stats, inventory, skills | proven on fixtures, build 1030002 (`PlayerSave*`, `SkillEditTests`, `InventoryAssetIdWriteTests`) | proven on the same code path; build 1030002 files only | proven on the same code path (server `Player_*.sav`) | unverified beyond wrapper conversion |
| Edit: world containers, deployables, creatures | proven on fixtures, 1030002 (`WorldSave*`, `PetEditTests`, `VehicleEditTests`) | unverified on 1030001 regions | proven, 1030002 | unverified |
| Edit: metadata (story flags, chapter) | proven, 1030002 (`StoryProgressionTests`, `FlagGateTests`) | unverified on 1030001 metadata (Chrissie) | proven, 1030002 | unverified |
| Edit: customization | proven (`CustomizationSaveSessionTests`) | n/a | n/a | unverified |
| In-game reload after edit | **unverified** (no recorded evidence in the repo for this matrix) | **unverified** | **unverified** | **unverified** (see `game-pass-format.md` for what was observed by hand) |

Notes:

- The Game Pass fixture is a sanitized container assembled from a Steam save (see `GamePassTests`), not a container
  written by the Xbox app. It proves the wrapper and codec, not that the game accepts a re-synced container.
- "In-game reload" is deliberately never marked proven here. It needs a person launching the game, and this matrix only
  records what a fixture or a dated manual note establishes. Add a dated row when someone does it.
- Cross-platform conversion (Steam to Game Pass and back) is covered by `SaveConversionServiceTests` and
  `SaveBundleTests` for structure; it is not listed as an edit area.

## Per-area verdicts the UI can use

`CompatibilityReport.Operations` (an `OperationSupport`) lists one verdict per editing area for the analyzed save.
Rules, most restrictive first:

1. Unrecognized save class, unreadable version, `OlderVersion`, or an engine build older than every known build:
   **Unsupported** for every write area. Inspection stays available whenever the class is recognized.
2. Unrecognized (newer) engine build, `ObservedEngineBuild`, no header evidence, or `NewerVersion`: **Unverified**. A
   host may allow the write behind an explicit warning.
3. Validated engine build with a known version: **Supported** (fixture-tested on this build; in-game reload is tracked
   in the table above, not by this verdict). Unknown content is preserved untouched and does not lower the verdict.

Areas: `Inspection`, `PlayerStats`, `PlayerInventory`, `PlayerProgression`, `PlayerIdentity`, `WorldStoryAndFlags`,
`WorldContainers`, `WorldDeployables`, `WorldCreatures`, `Customization`, `CatalogWrites`. Which apply depends on the
save kind (`OperationSupport.Find` returns null for an area that does not apply). Today all write areas of a save
share one verdict because the evidence (build, version) is per file, not per area; the per-area shape exists so an area
can be downgraded independently once area-specific evidence (for example a field that differs between builds) appears.

The CLI `info` command prints every non-supported area on stderr. No Razor surface consumes the report yet.

## Live compatibility

The live code already carries these facts, kept independent in `LiveCompatibilityInfo`:

| Field | Source today |
| --- | --- |
| Agent protocol version | `hello` reply (`TcpLiveGameChannel`, editor speaks 1) |
| Agent version | `hello` reply |
| Game build | **Not reported** by the agent. Null, never guessed from the installed game. |
| Agent capabilities | **Not advertised** by protocol 1. Null (distinct from an empty list). |
| UE4SS runtime version | **Not reported** through the connection. The bundled package version is in `live-agent/ue4ss/runtime.json`, which describes what the editor would install, not what runs. Null on a connection. |

`LiveCompatibilityEvaluator.Evaluate(info, requirement)` returns Unsupported for a missing handshake, a protocol
mismatch, or a capability the agent advertises without; Unverified when the game build, capability list or UE4SS version
is unknown or differs from the validated build; Supported only when every fact is known and compatible. Until the
agent reports game build, capabilities and UE4SS version, every live action evaluates to Unverified at best. That is the
honest state, and adding those three fields to `hello` is the remaining work before live actions can be gated per
capability. `TcpLiveGameChannel.AgentHandshake` exposes what the last handshake returned.

Live actions that depend on server RPCs, replication or game-thread behavior additionally need in-game verification
before being called supported; a stub-harness pass does not establish it.

## Remaining evidence to collect

- A save written by an earlier game build than 1030001 (none exists in the fixtures), to test the older-version path
  against real data instead of a synthetic ABF version.
- Edit fixtures on 1030001 regions and metadata, to move `ObservedEngineBuild` toward validated.
- Dated in-game reload notes for each platform and edit area.
- A Game Pass container written by the Xbox app, not derived from Steam.
