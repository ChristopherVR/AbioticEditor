# Research: why Game Pass -> Steam world conversion produces "Incompatible World Save"

Investigation of GitHub issue #24 (run 2026-08-13). Subject: the reporter-style Game Pass
dump at repo root `00090000068E7E8D_0000000000000000000000007B483EAA/` (world `ForScience`,
70 members, 204 h 50 m played, `StoryProgressionRow=EndGame`), converted with
`abioticeditor gamepass to-steam` and compared against the real Steam trees in
`tests/fixtures/SteamSaves/` and the live client saves on this machine.

Ground truth for the game side came from the installed Steam build
(`steamapps/common/AbioticFactor`, Steam buildid 24343447), which ships a **full PDB**
next to `AbioticFactor-Win64-Shipping.exe`. Function RVAs were resolved from the PDB's
public-symbol stream and disassembled with capstone. Everything in §3 and §4 below is read
directly off that disassembly, not inferred.

---

## 1. Summary

**Confirmed.** The conversion is structurally complete and content-faithful. The single
reason the game rejects it is the `DataLength` field in the reconstructed GVAS custom
header, which is copied verbatim from the captured header template
(`Infrastructure/GamePass/GvasHeaderTemplates.cs`) instead of being recomputed. That field
is not a redundant checksum: it is the **element count of a bulk-serialized
`TArray<uint8>`** that the game uses to decide how many bytes of the file to read. Getting
it wrong truncates (or over-runs) the property stream, `SaveVersion` never gets set, it
stays at its constructor default of `1`, and `UWorldEntryItem::GetSaveIntegrityState`
returns `ESaveIntegrityState::Incompatible` - the exact message in the report.

**Confirmed.** The reporter's own hypothesis ("only world information, no player
progression") is **wrong**. Every file a real Steam world folder contains is produced by
the conversion, including `PlayerData/Player_*.sav`, which is where per-character
progression lives.

**Confirmed.** Account-level Game Pass containers (`ProfileUnlocks`,
`ProfilePlayerStatsSave`, `ProfileScientistCustomization_*`, `ProfileUserSettings`,
`GameUserSettings`, `Settings`) are not carried by any converter path. Their Steam
equivalents sit **outside** the world folder. Their contents are cosmetic unlocks,
achievements/stats and settings - not gameplay progression - so this is not the reporter's
problem, but it is a real gap (§5).

**Confirmed.** Bed-claim owner strings inside `WorldSave_*.sav` still carry the old Xbox
account ids after conversion, and `WorldSteamIdPatcher` cannot fix them because an Xbox
XUID (16 digits) and a SteamID64 (17 digits) differ in length (§6).

**Not a factor (checked and cleared).** The `ABF_SAVE_VERSION` custom-header int, the
header `Id` field, the engine/custom-format block of the template, and the
`bHasBeenCompressed` flag are all currently harmless. Each is analysed below because each
looked like a candidate.

---

## 2. Structural completeness: no files are missing

`gamepass to-steam` on the dump produced 69 `.sav` files plus `SandboxSettings.ini`.
Compared name-set against the real Steam world `tests/fixtures/SteamSaves/SaveGames/
76561197993781479/Worlds/Cascade/`:

```
$ comm -3 <(ls .../Worlds/Cascade) <(ls <converted>)
        WorldSave_H_Garden.sav        # only in the converted world
        WorldSave_V_ISLAND.sav        # (regions the Cascade world never visited)
```

Nothing is present in a real Steam world folder and absent from a converted one. The
layout matches exactly:

```
<World>/
├─ SandboxSettings.ini              (optional - the Steam world "Chrissie" has none)
├─ WorldSave_MetaData.sav
├─ WorldSave_*.sav                  ×61
└─ PlayerData/Player_<id>.sav       ×9
```

The bundle round-trip is also lossless in content: the container TOC declares
`field1(rawLen) = 80 349 863` and the member sizes sum to exactly the same value, and the
extracted bytes reproduce the declared per-member sizes (`WorldSave_Facility` = 39 008 967 B
from a 921 465 B Oodle stream).

---

## 3. The real cause: `DataLength` is a `TArray` length, and it is wrong on every file

### 3.1 Measured

`GamePassMemberCodec.ToGvas` splices a captured header template in front of the headerless
member body. The template's trailing `DataLength` is whatever the capture save happened to
have. Measured over the whole converted tree:

| tree | saves | `DataLength` mismatches |
|---|---|---|
| `tests/fixtures/SteamSaves` (real) | 131 | **0** |
| `tests/fixtures/DedicatedServerSaves` (real) | 64 | **0** |
| converted from the Game Pass dump | 69 | **69** |

Concrete values (`DataLength` vs the bytes that actually follow the header):

```
WorldSave_MetaData.sav     claims    40 441   actual    48 097
WorldSave_Facility.sav     claims 13 489 980  actual 39 008 967
WorldSave_H_Cabin.sav      claims 13 489 980  actual      1 909
Player_2533274900397709.sav claims   174 507  actual    203 684
```

13 489 980 = `0x00CDD73C`, the last four bytes of `GvasHeaderTemplates.WorldSaveB64`;
40 441 = `0x00009DF9` from `WorldMetadataSaveB64`; 174 507 = `0x0002A9AB` from
`CharacterSaveB64`. Every world save in a converted folder carries the *same* wrong number.

The one exception is a re-homed player save: `GamePassToSteamWorld(..., newPlayerId)` calls
`PlayerSaveIdentity.ChangeSteamId`, which re-serializes through UeSaveGame, and
`AbioticWorldSave.SerializeHeader` / `AbioticCharacterSave.SerializeHeader` recompute
`DataLength`. Verified on a single-player Game Pass world: the re-homed
`Player_76561197993781479.sav` is correct (100 521 = 100 521) while
`WorldSave_MetaData.sav` in the same folder still claims 40 441 against 370 actual bytes.
This is why the reporter saw no difference "with and without player account id" - the
world files are broken either way.

### 3.2 What the game does with it

`UAbioticSaveStatics::LoadWorld` (RVA `0x4714160`), reading the file:

```
FSaveGameHeader::Read(reader)                 ; stock GVAS header (magic, versions, class)
operator<<(reader, FString)                   ; read a string
operator<<(reader, int32)                     ; read one int32
Stricmp(readString, L"ABF_SAVE_VERSION")
  != 0  -> ebx = 2  -> UGameplayStatics::LoadGameFromSlot   (legacy path)
  == 0  -> ebx = 3  -> fall through to 0x47143bc            (ABF path)

0x47143bc:
  UGameplayStatics::LoadDataFromSlot(&bytes, path, 0)
  TArray<uint8>::BulkSerialize(&inner, reader, false)   <-- 0x47143ea
  UClass::TryFindTypeSlow / StaticLoadObject(header.SaveGameClassName)
  NewObject(...)                                        ; then properties are read from `inner`
```

`UAbioticSaveStatics::LoadPlayer` (RVA `0x4714670`) is the same shape with **no** marker
string and **no** version int: `FSaveGameHeader::Read` at `0x47147fa` is followed
immediately by `BulkSerialize` at `0x4714815`.

`TArray::BulkSerialize` on load is, in UE:

```cpp
int32 SerializedElementSize = sizeof(ElementType);
Ar << SerializedElementSize;          // <-- the field the editor calls `Id`
int32 NewArrayNum = 0;
Ar << NewArrayNum;                    // <-- the field the editor calls `DataLength`
Empty(NewArrayNum); AddUninitialized(NewArrayNum);
Ar.Serialize(GetData(), NewArrayNum * SerializedElementSize);
```

So the custom headers are not what `AbioticWorldSave` / `AbioticCharacterSave` model them
as. Their real shape is:

| editor field | actual meaning |
|---|---|
| world/meta `Version` (always 3) | genuine `ABF_SAVE_VERSION` int, read but not used for the compatibility verdict |
| world/meta `Id` (always 1) | `SerializedElementSize` = `sizeof(uint8)` |
| world/meta `DataLength` | `NewArrayNum` - the number of body bytes to read |
| character `Version` (always 1) | **also** `SerializedElementSize`; character saves have no version int at all |
| character `DataLength` | `NewArrayNum` |

That explains why `Id` is 1 on all 195 real fixture saves and why every character save
"version" is 1. It also makes the failure mode concrete:

- `DataLength` **too small** (metadata: 40 441 of 48 097): the inner array gets the first
  40 441 bytes only. The property stream ends mid-way and everything after that offset is
  simply never seen.
- `DataLength` **too large** (`H_Cabin`: 13 489 980 of 1 909): `AddUninitialized` then
  `FMemoryReader::Serialize` past the end sets `ArIsError`; the region fails outright.

### 3.3 The verdict function

`UWorldEntryItem::GetSaveIntegrityState` (RVA `0x470d350`), disassembled in full:

```
if (this->CachedState == 4)              return Corrupt;         // sticky
meta = GetMetaDataSave();
if (!meta)                               return Playable;        // (state 0)
v = meta->SaveVersion;                   // UAbioticSave + 0x38
if (v > 3)                               return LaterVersion;    // 2
if (v < 2)                               return Incompatible;    // 3   <-- our case
if (v < 3)                               return UpgradeRequired; // 1
if (<global flag> && meta->bHasBeenCompressed == 0)
                                         return RequiresCompress;// 5
                                         return Playable;        // 0
```

`ESaveIntegrityState` (enum names recovered from the exe's reflection strings, ordering
recovered from the returns above): `Playable=0, UpgradeRequired=1, LaterVersion=2,
Incompatible=3, Corrupt=4, RequiresCompress=5`.

`UAbioticSave`'s constructor (`InternalConstructor<UAbioticSave>`, RVA `0x4643390`):

```
0x46433b4  mov dword ptr [rbx + 0x38], 1     ; SaveVersion   = 1
0x46433c8  mov byte  ptr [rbx + 0x3c], 0     ; bHasBeenCompressed = false
```

So **an unset `SaveVersion` is `1`, and `1` is exactly the `Incompatible` bucket.**

### 3.4 The closing link

`SaveVersion` is one of the last properties in the metadata body. Measured offsets, relative
to the start of the body (i.e. the value `DataLength` bounds):

| property | converted metadata (body = 48 097 B, claims 40 441) | real Steam metadata (body = 51 919 B, claims 51 919) |
|---|---|---|
| `MinutesPassed` | 5 | 5 |
| `LastPlayed` | 52 | 52 |
| `GlobalUnlocks` | 148 | 148 |
| `StoryProgressionRow` | 47 875 | 51 745 |
| `SaveIdentifier` | 47 937 | 51 808 |
| **`SaveVersion`** | **47 994** | 51 865 |
| `bHasBeenCompressed` | 48 039 | (absent) |

The truncation point is 40 441. `SaveVersion` lives at 47 994, **7 553 bytes past it**. It
is never read, stays at the constructor default `1`, and the load screen reports
`Incompatible`. Chain closed.

This also explains the reporter's second symptom. The world *is* listed in Load Game
because the entry list is built from the folder, and it is blocked before any region is
touched - which is why the state of the 61 region files (all also broken) never becomes
visible.

### 3.5 Direction matters

Only **Game Pass -> Steam** is affected. `SteamWorldToGamePass` calls
`GamePassMemberCodec.ToMemberBody`, which strips the header off a genuine save, and
`AbfSaveBundle.Serialize` writes `m.Body.Length` into the TOC. The length is therefore
computed from the real bytes in that direction. Editing an existing Game Pass save in place
is likewise unaffected: `GamePassSaveSet` re-derives the member body from the edited GVAS
and repacks.

---

## 4. Things that looked like the cause and are not

- **`ABF_SAVE_VERSION` in the header template is hardcoded to 3 (world/meta) and the
  character template's first int to 1.** Correct today: all 195 real fixture saves read 3
  and 1. But note the value is **unrecoverable** from a Game Pass bundle - the bundle stores
  only the headerless body, so nothing in the container records it. If the game ever bumps
  the world header version, `GamePassToSteamWorld` will silently emit the old number and
  there is no way to detect it from the source data. The compatibility verdict does not
  read this int (§3.3 reads the *property* `SaveVersion` from the body, which the body does
  carry), so today the hazard is latent, not active.
- **Template captured from one build.** The template's engine block is
  `5.4.4--2146453646 '++DF+ABF'`, GVAS v3, UE4 pkg 522 / UE5 pkg 1012, 74 custom formats -
  byte-identical to the current Steam fixtures. A newer game build would write a different
  custom-format table, and splicing an old one in front of a newer body could mis-drive
  property deserialization for any type whose serialization is custom-version-gated. Not
  the current failure (the version block matches), but the same class of latent hazard.
- **`Id` field.** Always 1 in the template and in all 195 real saves. Now explained (§3.2):
  it is `sizeof(uint8)`, so it is a constant, and the template's value is correct by luck
  rather than by design.
- **`bHasBeenCompressed`.** Present and `true` in the metadata of *both* Game Pass saves
  examined (the reporter-style dump and a locally created one), and absent - i.e. `false` -
  in all 195 Steam and dedicated-server saves. It looked like a platform marker that would
  make the game try to decompress loose files. Per §3.3 it is only consulted to raise
  `RequiresCompress`, and only when it is **false**; `true` falls through to `Playable`. It
  is harmless on Steam. (The converse is interesting for the future: the game appears to be
  moving toward compressed saves, and a Steam save can be sent down a `RequiresCompress`
  path when that global flag is on.)
- **`SaveVersion` gating the player-save parser.** `LoadPlayer` at `0x4714708` branches on
  `meta->SaveVersion >= 3` to choose the bulk-serialized path over the legacy
  `LoadGameFromSlot`. So a metadata save that reports a stale `SaveVersion` would also send
  every `Player_*.sav` down the wrong parser. One more reason the metadata truncation is
  the load-bearing failure.

---

## 5. Where player progression actually lives

The reporter believed progression sits in files the conversion omits. It does not.

**Inside the world folder (carried by the conversion):**

| file | class | holds |
|---|---|---|
| `PlayerData/Player_<id>.sav` | `Abiotic_CharacterSave_C` | the character: inventory, equipment, skills, needs, position, traits |
| `WorldSave_MetaData.sav` | `Abiotic_WorldMetadataSave_C` | `StoryProgressionRow`, `MinutesPassed`, and the `GlobalUnlocks` struct - `GlobalRecipesUnlocked`, `GlobalRecipesResearched`, `GlobalJournalEntries`, `GlobalEmailsRead`, `GlobalItemsPickedUp`, `GlobalCompendium{Email,Narrative,Exploration}` |
| `WorldSave_*.sav` | `Abiotic_WorldSave_C` | the built base, containers, doors, NPCs, pets, vehicles, quest flags |
| `SandboxSettings.ini` | - | world difficulty |

**Outside the world folder (carried by nothing):**

| Game Pass container | Steam equivalent | class | holds |
|---|---|---|---|
| `ProfileUnlocks` | `SaveGames/<steamid>/Unlocks.sav` | `Abiotic_CustomizationUnlocks_Save_C` | account-wide **cosmetic** unlocks only - observed keys are clothing/hair/glasses/tie/ID-badge names (`UpperBody_Engineer`, `Hair_Singed`, `Tie_DrHud`, `id_reactor`, ...) |
| `ProfilePlayerStatsSave` | `SaveGames/<steamid>/PlayerStatsSave.sav` | `/Script/AbioticFactor.PlayerStatsSave` (native) | achievements (`ACH_*`) and lifetime stats |
| `ProfileScientistCustomization_<n>` | `SaveGames/<steamid>/ScientistCustomization_<n>.sav` | `Abiotic_CustomizationSave_C` | saved character-appearance presets |
| `ProfileUserSettings` | `SaveGames/<steamid>/UserSettings.sav` | `Abiotic_SettingsSave_C` | in-game settings |
| `GameUserSettings` | `Config/Windows/GameUserSettings.ini` | - | engine/graphics settings |
| `Settings` | `Config/Windows/Settings.ini` | - | engine settings |

Container list for the dump, from `gamepass status`:

```
ForScience-WC 170, ForScience-WC-B 113, GameUserSettings 167, ProfilePlayerStatsSave 222,
ProfileScientistCustomization_1 10, ProfileUnlocks 5, ProfileUserSettings 35, Settings 109
```

None of these gate loading a world. Losing them costs cosmetics, achievements and settings,
nothing else.

---

## 6. Account ownership after conversion

For a converted character to be the player's own on Steam, the id must change in **three**
places. `GamePassToSteamWorld(..., newPlayerId)` covers the first two:

1. The file name `PlayerData/Player_<id>.sav` - covered (`PlayerSaveIdentity.ChangeSteamId`).
2. The top-level `SaveIdentifier` StrProperty inside that save - covered (same call).
3. **Bed-claim owner strings inside the world saves - not covered.**

Confirmed by grepping the converted tree for the dump's Xbox ids:

```
2533274900397709 -> PlayerData/Player_2533274900397709.sav
                    PlayerData/Player_2535416824117110.sav
                    WorldSave_Facility.sav          <-- 0xcd7a3: "2533274900397709}|!|{Martian Marz"
2535414045159780 -> ...
                    WorldSave_Facility.sav          <-- 0xa3a922: "2535414045159780}|!|{Ydep"
```

That is exactly the `<ownerId>}|!|{<name>` shape `WorldSteamIdPatcher` targets, but
`GamePassToSteamWorld` never calls it, and calling it would throw:
`PatchFile` refuses when `oldId.Length != newId.Length`, and an Xbox XUID is 16 digits
against a SteamID64's 17. A same-length in-place patch is impossible here; fixing it needs
a full world-save reserialize.

Note also that Game Pass ids are not all XUIDs - the dump contains
`Player_6983760860664838809.sav`, a 19-digit id alongside eight 16-digit ones. Any
length-based assumption is unsafe.

Re-homing is additionally refused outright for multiplayer worlds
(`GuardSingleRehome`), which is the reporter's situation: `ForScience` has nine player
saves. Without `--player-id` the Steam game finds no `Player_<theirSteamID64>.sav` in the
world and offers character creation - the second symptom in the report.

---

## 7. Repro

```console
$ set ABIOTIC_OODLE_DLL=D:\Development\uesave\oodle-data-shared.dll
$ abioticeditor gamepass to-steam <wgs-dir> <out> --container ForScience-WC
$ # every WorldSave_*.sav in <out> now carries DataLength 13489980 regardless of size
```

Header/offset measurements in this note were taken with throwaway scripts (GVAS header
walker + PDB public-symbol reader + capstone disassembler); none were kept in the repo.

## 8. Follow-ups

1. **Fix `DataLength`** on the Game Pass -> Steam path (in progress at time of writing).
   The recompute is trivial - `SerializeHeader` already does it - the splice just has to
   stop trusting the template's value.
2. **Add a regression assert**: for every save the converter writes, `DataLength` must
   equal the byte count after the custom header. The existing fixtures make this a
   one-liner and it would have caught this.
3. **Consider re-labelling the header fields** in `AbioticWorldSave` /
   `AbioticCharacterSave` (`Id` -> `ElementSize`, and drop the notion of a character
   "version"), and correct `SaveVersionRegistry`'s `SaveKind.Character` row, which
   currently documents `MinKnownVersion=MaxKnownVersion=1` for a field that is
   `sizeof(uint8)`. The real character-save compatibility signal is the metadata's
   `SaveVersion` property.
4. **Carry the `Profile*` containers** (or at least tell the user they are being left
   behind). Cosmetics and achievements are cheap to lose but surprising to lose silently.
5. **Bed claims across a different-length id change** need a reserialize-based rewrite
   before a converted multiplayer world can be fully re-homed.
