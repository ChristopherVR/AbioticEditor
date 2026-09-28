# Game Pass extraction inventory

Working inventory for the roadmap item "Extract Game Pass support for other games and modding
tools" (migration step 1). It maps every type under
`src/AbioticEditor.Core/Infrastructure/GamePass` to the four package boundaries in the roadmap
table, lists the single-title assumptions that must not become public API, and records what steps
2 to 4 actually moved. The byte-level findings live in [game-pass-format.md](game-pass-format.md).

Boundaries used below:

- **Storage** - the shared Xbox storage package (`AbioticEditor.GamePass.Storage`): wgs index,
  manifests, container/blob identities, ETag and state handling, snapshots, write ordering.
- **Platform** - package/account discovery, process checks, lock and write-availability handling.
- **Adapter** - game-specific payload: bundles, compression, headers, save classes, member names,
  account profiles, platform conversion.
- **Editor** - semantic operations, previews, backups the user sees, tooling on top.

## Type map

| Type (Core namespace `AbioticEditor.Core.GamePass`) | Boundary | Status after steps 3 and 4 |
| --- | --- | --- |
| `WgsContainerStore` (index read/write, manifests, blob read/write, orphans, repair) | Storage | Logic moved to `WgsStore` in the new package. Core keeps `WgsContainerStore` as the Abiotic adapter: it supplies the package family name, the world-bundle recogniser, the log, and its own write guard, then forwards. Public members unchanged. |
| `WgsEntryState`, `WgsSyncState` (enums) | Storage | Defined in the package. Core keeps same-valued enums so existing callers compile; a test asserts the values never drift. |
| `WgsContainer` | Storage | Entry model lives in the package. The Core type is a thin view over it (same properties, writes go through). |
| `WgsOrphanedContainer` | Storage | Package record has a neutral `Label` in place of `WorldName`. Core keeps its record (`WorldName`) and maps. |
| `WgsSnapshot`, `WgsContainerState` | Storage | Capture and Compare live in the package. Core keeps the records (they are JSON-serialized by the CLI `snapshot`/`compare` commands) and maps. |
| write-verdict plumbing inside `WgsContainerStore.CheckWritable/EnsureWritable` | Storage + Platform | The package has `IWgsWriteGate` and a structural default gate (conflict marker, deleted or undefined states). Core plugs in `AllowAll` and runs its own guard first, because its guard also needs the process scan. |
| `GamePassEnvironment` (process scan, `IsInsideConnectedStorage`), `GamePassProcessScan`, `GamePassRunningProcess`, `GamePassProcessRole` | Platform | Stays in Core. Hard-codes the Abiotic process prefixes (`AbioticFactor`). Candidate for a platform package that takes the title's process names as data. |
| `GamePassWriteCheck`, `GamePassWriteRisk`, `GamePassWriteConcern`, `GamePassWriteOverride`, `GamePassUnsafeWriteException` | Platform (contract), Editor (messages) | Stays in Core. Messages are player-facing and mention Abiotic. The generic equivalents (`WgsWriteAssessment`, `WgsWriteConcern`, `WgsWriteRefusedException`) are in the package. |
| `GamePassDiscovery`, `DiscoveredGamePassSave` | Platform | Stays in Core. Roots are `%LOCALAPPDATA%\Packages\<pkg>\SystemAppData\wgs` and `<drive>:\XboxGames\GameSave\wgs`; the package match is the substring `Abiotic`. Account folder rule: `<XUID>_<TitleScid>`, both hex. |
| `GamePassSaveSet`, `GamePassSaveEntry`, `GamePassSaveKind`, `GamePassContainerFault`, `GamePassWorldBackup` | Adapter + Editor | Stays in Core. Backup-before-write (`BackupOnce`, `.bak` sibling, 8 kept) is an Editor policy; the mechanism is now also offered as `WgsStore.CopyStoreTo`. |
| `AbfSaveBundle`, `AbfMember` | Adapter | Stays in Core (step 5 boundary). |
| `GamePassMemberCodec`, `GvasHeaderTemplates` | Adapter | Stays in Core (step 5). Header templates are captured Abiotic GVAS prefixes. |
| `OodleCodec`, `OodleUnavailableException` | Adapter (optional codec) | Stays in Core. Should become an injectable codec interface with documented install and licensing (step 5 and 7). |
| `GamePassConverter` (Steam <-> Game Pass world conversion, profile carry, player-id rename) | Adapter | Stays in Core. |
| `AbfBlobInspector` (new, internal) | Adapter | Reads the world name from the uncompressed table of contents so orphaned worlds can be named with no Oodle. This is the first implementation of the package's `IWgsBlobInspector` hook. |

## Single-title assumptions found (do not make these public contracts)

| Assumption | Where | Note |
| --- | --- | --- |
| Package family name `PlayStack.AbioticFactor_3wcqaesafpzfy!AppAbioticFactorShipping` | `WgsContainerStore.AbioticPackageFamilyName`, used when a store is created from scratch | The package now takes the family name as a parameter on `WriteNewContainer`. |
| Title recognition by substring `Abiotic` in the family name or in `Packages\` folder names | `IsAbioticContainerFolder`, `GamePassDiscovery` | Discovery must take an explicit title/account selection for other games. |
| Container names: `<World>-WC` (world bundle), `<World>-WC-B` (the game's own backup), `Settings`, `GameUserSettings`, `ProfileUnlocks`, `ProfilePlayerStatsSave`, `ProfileUserSettings`, `ProfileScientistCustomization_<n>` | `GamePassSaveSet` (`WorldSuffix`, `BackupSuffix`, profile constants) | Adapter data. The storage package treats names as opaque strings (case-insensitive lookup). |
| One manifest, one blob named `Data`, blob count 1 | `WriteManifest`, `ReadManifest` (fixed 128-byte UTF-16 name field, two 16-byte ids) | Reads of a manifest that declares more than one blob work only for the first entry. The package reports `MultiBlobContainers` in a diagnosis and `TryReadBlob` returns `UnsupportedLayout`, so a second game's layout is detected instead of silently mis-read. Multi-blob write support is not modelled. |
| Index version 14 | `WriteNewContainer` writes 14; reads accept any version | `WgsDiagnosis.IsKnownIndexVersion` flags others. Only 14 is observed. |
| Compression: bundle payload method `1` = Oodle, whole-member stream, uncompressed size passed verbatim | `AbfSaveBundle` | Adapter. Loading needs Oodle from the game install, `ABIOTIC_OODLE_DLL`, or a CUE4Parse download. |
| Settings encoding: `Settings` and `GameUserSettings` ini text stored with every byte incremented by one; `SandboxSettings.ini` member uses the same | `GamePassMemberCodec.EncodeIniText` | Adapter. |
| Headerless GVAS members with save class in the TOC; three save classes (`Abiotic_CharacterSave_C`, `Abiotic_WorldSave_C`, `Abiotic_WorldMetadataSave_C`) | `GamePassMemberCodec`, `GvasHeaderTemplates` | Adapter. |
| Account files: player saves are `Player_<XUID or SteamID64>.sav`; a member path prefix `Profile/Worlds/<World>/`; account-level containers map to `Unlocks.sav`, `PlayerStatsSave.sav`, `UserSettings.sav`, `ScientistCustomization_<n>.sav` under the Steam account folder | `GamePassConverter`, `GamePassSaveSet` | Adapter and Editor. |
| Account wgs folder naming `<XUID>_<TitleScid>` | `GamePassDiscovery.IsAccountFolderName` | Platform. Non-matching folders are accepted with a warning, deliberately. |
| Backup policy: whole folder copied to `<folder>.bak` (timestamp suffix when it exists), 8 kept | `GamePassSaveSet.BackupOnce` | Editor policy. |
| Entry FILETIME truncated to whole milliseconds; index FILETIME full precision and strictly advancing | `WriteIndex`, `NowEntryFileTime` | Storage. Time comes from an injected `IWgsClock`. |
| Container number is a `u8` and wraps (255 + 1 = 0) | `WriteBlob` | Storage. Snapshot comparison handles the wrap by comparing content as well. |
| Superseded generation deleted after commit | `PruneSupersededGenerations` | Storage. One manifest plus one blob per folder, as the game keeps it. |

## Locking and concurrency, as implemented

- The editor holds no OS locks on the store. "Locked" means a write or replace failed because
  another process (game, Xbox app, gaming services) held a file. The package classifies that as
  `WgsOperationStatus.LockConflict` (sharing violation HRESULT `0x80070020`/`0x80070021` or access
  denied) on the `Try*` commit and open methods. The throwing methods still throw the original
  `IOException`.
- Whether the game is running is a Platform concern (`GamePassEnvironment.Scan`), reached through
  the write gate. The package's default gate never looks at processes.
- Concurrent change: the store fingerprints `containers.index` when it is read and after each of
  its own writes. `TryWriteBlob` and `TryAddOrReplaceContainer` compare that fingerprint before
  touching anything and return `ConcurrentChange`. The individual file replacements are atomic
  (temp file plus replace); the sequence of them is not a transaction, which is why the order is
  blob, manifest, index, prune. An interrupted commit leaves the previous generation fully
  described (covered by a test).
- Reads never repair. Repair is `RepairRecoveredManifests`, previewed by
  `ContainersNeedingRepair`. `Diagnose()` gathers the same information without changing anything.

## What steps 2 to 4 delivered

- **Step 2, characterization tests** (`tests/AbioticEditor.Tests/WgsContainerStoreCharacterizationTests.cs`):
  written and committed before any code moved. They cover the sanitized Game Pass fixture (index
  fields, blob size, read purity, snapshot stability, a write on a copy) and synthetic temp-dir
  stores (created versus modified state, ETag echo, container number wrap, index timestamp,
  untouched-byte preservation of other containers, Unicode names and ETags, orphan discovery and
  re-registering, ABF world naming, missing manifest and folder, truncated index, folder
  resolution). Not covered by real fixtures: locked files (needs Windows sharing semantics; covered
  through fault injection in the package tests), interrupted sync with two live blobs (existing
  `GamePassSafetyTests`), and any second game.
- **Step 3, contracts** (`src/AbioticEditor.GamePass.Storage`): inspect and read (`Open`,
  `TryOpen`, `TryReadBlob`), enumerate (`Containers`, `OrphanedContainers`), plan (`PlanWrite`),
  validate (`AssessWrite`, `Diagnose`), commit (`TryWriteBlob`, `TryAddOrReplaceContainer`,
  `WriteBlob`, `AddOrReplaceContainer`), backup (`CopyStoreTo`), diagnose (`Diagnose`,
  `DetectExternalChange`), repair (explicit). Typed results: `WgsOpenResult`, `WgsReadResult`,
  `WgsCommitResult`, with statuses for lock conflicts, concurrent changes, unsupported layouts,
  missing blobs and in-flight syncs. Injected services: `IWgsFileSystem`, `IWgsClock`, `IWgsLog`,
  `IWgsBlobInspector`, `IWgsWriteGate`. A restore call is not provided: restoring is copying the
  backup folder back over the store, which is an Editor decision.
- **Step 4, extraction**: Core routes every wgs read and write through the package. Public Core
  types and namespaces are unchanged. The Core NuGet package bundles the package DLL (like the
  submodule assemblies) so it needs no unpublished dependency.

## Remaining

- Step 5: move bundle, member, header and conversion code behind an adapter interface; make Oodle
  an injectable codec; document supported source and target versions and the treatment of unknown
  or modded data.
- Step 6: a second real game with sanitized fixtures. The `IWgsFileSystem` in-memory tests show the
  boundary, they do not count as support.
- Step 7: package naming and versioning, publishing workflow (a `nuget` pack step and a change
  detector for `src/AbioticEditor.GamePass.Storage/`), dependency licences, supported platforms,
  CLI or example adapter.
- Platform split: discovery and the process scan take the title's package name and process names
  as data; then they can leave Core.
