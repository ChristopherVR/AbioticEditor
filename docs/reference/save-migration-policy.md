# Save migration policy

## Two different operations

**Editing in the original format.** The editor reads a save, changes some values, and writes it back in the same
format it arrived in: the same GVAS container version, engine stamp, custom-version table and ABF_SAVE_VERSION. Every
byte the edit did not touch is preserved (`CompatibilitySupportTests` proves the unchanged round-trip for every
fixture). This is the only write the editor performs today.

**Upgrading.** Deliberately converting a save to a newer format: new tags with their default hash-suffixed names,
changed struct layouts, a new version stamp. An upgrade is a set of concrete transformations that each have a fixture
pair (old input, new output written by the game). None is implemented.

**Changing a version number is neither.** A save whose header claims version N+1 but whose body is laid out for
version N is corrupt under both readings. Nothing in the editor may raise or lower a stamp (ABF_SAVE_VERSION, GVAS
container version, engine changelist, custom-version table) to make a save look compatible.

## Rules

1. Edits keep the source stamp. The verdict for writing comes from `CompatibilityReport.Operations`: a save older than
   any fixture is blocked (writes Unsupported), a save on a build seen only in round-trip fixtures or on a newer build
   is Unverified and needs an explicit user acknowledgement.
2. A downgrade request (writing a save so an older game can read it) is refused by policy; no code offers one today. The editor has no evidence of what an
   older layout requires, and new fields would be silently dropped or misread.
3. A migration is supported only when all are true: source and target builds are recorded in the registry; a
   transformation exists for every field that differs; a fixture pair proves it; the result is checked by the same
   round-trip test; an in-game reload note is recorded in the support matrix.
4. Until a migration is supported, an old save is edited in its own format or not at all. Writers select tag layouts
   from what the target file already contains; where a tag is absent, they create it with its exact full hash-suffixed
   name only when that name is confirmed for the target build.

## Audit: does any code path "migrate" by changing a number?

Searched `src/` for writes to ABF_SAVE_VERSION, the GVAS version fields and the plugin upgrade path.

| Location | What it does | Verdict |
| --- | --- | --- |
| `AbioticCharacterSave` / `AbioticWorldSave` (`Serialization/Gvas`) | Read the ABF version and write the same value back. | Preserves the stamp. No issue. |
| `SaveVersionRegistry.TrySetAbfVersion` | Public helper that overwrites the stamp on a loaded save. Only tests call it (to fabricate future or past versions). | Not used by any write path. Its doc says tooling/tests only; treat it as test API. Kept for compatibility. |
| `AbioticSaveJsonSerializers` (`HeaderFromJson`) | The raw JSON import can set `Version` from the JSON text. | A power-user raw edit, not a migration. The report classifies the result on the next open, so an edited stamp shows as older or newer. |
| `AbfSaveBundle` (Game Pass) | Creates new bundles as version 3 and preserves the version of an existing one. | This is the Game Pass container format, not the save version. New bundles use the value observed in real containers. No issue. |
| `PlayerSaveFactory` / `WorldSaveFactory` | Build new saves. | Contain no version-number references (searched case-insensitively). No issue. |
| Sample plugin `plugins/VersionShim` (`FixSaveVersionUpgrader`) | Rewrites the 4-byte GVAS `SaveGameVersion` field to 3 when a file holds another value, then lets the host load it. | **This is exactly a version-number-only change.** It is a sample plugin (not part of the app, full-trust, opt-in) and `SaveUpgradeService` only writes the result back when `persist` is true, keeping a pre-upgrade backup. No UI calls `SaveUpgradeService`. It should stay labelled as a demonstration of the `ISaveUpgrader` contract, not offered as a real migration; a real upgrader must follow rule 3. |

No path in `src/` changes a version stamp as part of a normal edit, conversion or save.

## What the editor tells the user

- Older than any tested version: "You can inspect it, but writes are blocked because its field layout has not been
  verified."
- Newer version or unrecognized build: writes allowed only as Unverified, with the reason.
- Exact build not identifiable: reported as Unknown, never assumed from the installed game.
