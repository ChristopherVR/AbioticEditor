# Game Pass save format (internals)

How a Game Pass / Microsoft Store save is packaged on disk, for anyone working on the editor or
curious about the format. **You do not need any of this to edit a Game Pass save**: the app and the
CLI unpack, edit and repack it for you. For the workflow (opening, editing, converting, and the
offline routine that keeps edits from being reverted) see the
[Game Pass saves guide](/guide/game-pass).

## Why it differs from Steam

A **Steam** save is a folder of loose `.sav` files: one per world region, plus a player file per
character. A **Game Pass** save packs that entire world (every `WorldSave_*` region, every player,
and the world's `SandboxSettings.ini`) into **one compressed blob** inside an Xbox Connected Storage
("wgs") container. The save *content* is identical to Steam (the same GVAS data); only the
**packaging** differs, which is why a world converts losslessly between the two.

Player ids inside a Game Pass save are 16-19 digit **Xbox account ids (XUIDs)** rather than
SteamID64s. They fail a SteamID check but pass the editor's opaque-id rules, so they load, re-home
and claim beds normally.

## Four nested layers

From outside in:

1. **wgs container store** - a `containers.index` maps logical container names to GUID-named
   folders; each folder holds a `container.N` manifest naming a GUID blob file.
2. **`ABF_SAVE_VERSION` bundle** - the world blob: a table of contents (member path, size, save
   class, flag) followed by one **Oodle-compressed** stream holding every member body.
3. **Headerless members** - each packed save is the GVAS *property body* only; the save class lives
   in the table of contents. The editor splices a class-matched header back on to read it, and
   strips it again on write.
4. **GVAS** - the same save data the Steam version stores. See the
   [player](/reference/player-save-schema) and [world](/reference/world-save-schema) save schemas.

Oodle (de)compression uses the same library the editor already uses for the game's pak files: taken
from the game install, from `ABIOTIC_OODLE_DLL`, or downloaded once by CUE4Parse. On Linux the
downloaded file is `liboodle-data-shared.so`.

Logical container names seen in a real store: `<World>-WC` (the world bundle), `<World>-WC-B` (the
game's own backup of it), `GameUserSettings`, `Settings`, `ProfilePlayerStatsSave`,
`ProfileScientistCustomization_<n>`, `ProfileUnlocks`, `ProfileUserSettings`. The editor treats
`-WC` containers as worlds and reads/writes `ProfileScientistCustomization_<n>` for character looks.
`GameUserSettings` and `Settings` are ini text stored with every byte incremented by one.

## `containers.index` is a sync protocol, not a file listing

This is the part that matters most, and the part the editor got wrong for a long time. The index is
one half of a conversation with the Xbox cloud service. It records what the service should believe
about each container. Getting it wrong does not fail loudly at write time; it loses an argument
later, out of sight, and takes the edit with it.

Layout (little-endian):

| Field | Type | Notes |
| --- | --- | --- |
| version | `u32` | 14 in every observed store |
| container count | `u32` | rewritten from the live list on every save |
| reserved | `u32` | 0. Really an empty length-prefixed display name |
| package family name | wstring | `u32` char count + UTF-16. `PlayStack.AbioticFactor_3wcqaesafpzfy!AppAbioticFactorShipping` |
| index FILETIME | `i64` | the recency token sync compares. Full precision |
| sync flags | `u32` | see below. Previously misread as a constant `3` |
| root GUID | wstring | |
| reserved | 8 bytes | `00 00 00 10 00 00 00 00` as the game writes it |

Then one entry per container:

| Field | Type | Notes |
| --- | --- | --- |
| name | wstring | e.g. `ForScience-WC` |
| name2 | wstring | same as name in practice |
| ETag | wstring | a version token **issued by the service**, e.g. `"0x8DEBCCC41BE9635"` |
| container number | `u8` | the `N` in `container.N` |
| state | `u32` | see below |
| folder GUID | 16 bytes | mixed-endian, matches the GUID folder name |
| FILETIME | `i64` | millisecond granular (every observed value divides by 10,000 ticks) |
| reserved | `i64` | really `Type` + `UnkInt3`, both 0 |
| blob size | `i64` | must equal the real blob byte count |

### Container state

| Value | Meaning |
| --- | --- |
| 0 | undefined |
| 1 | Synced. Local and cloud agree; the resting state |
| 2 | Modified. Changed locally, still based on a known cloud version. Keeps its ETag |
| 3 | Deleted. A tombstone, so the deletion can reach the cloud |
| 4 | undefined |
| 5 | Created. Made locally, never uploaded, so no ETag |

**Two public reverse-engineering lineages disagree about 2, 4 and 5**, and picking the wrong one
writes something that means the opposite. The mapping above follows
[libNOM.io](https://github.com/zencq/libNOM.io) (the engine behind the mainstream No Man's Sky
editor). [LukeFZ/XblContainerReader](https://github.com/LukeFZ/XblContainerReader) instead calls 5
"Modified" and 2 "Unknown". Two pieces of evidence settle it:

- In a real Abiotic Factor store, containers the game itself writes are only ever 1 or 2, never 4
  or 5, and always carry an ETag.
- Two independently written parsers (`palworld-xgp-import`, `palworld-save-pal`) hard-error when
  `state & 4` disagrees with "the ETag is empty". So bit 2 means local-only-never-uploaded, which 4
  and 5 both are and 2 cannot be.

**The write rule follows from that invariant.** A container with an ETag becomes `Modified`; one
without stays `Created`. Never break the pairing, and never mint an ETag: only the service issues
them, and it uses the one you echo back to recognise which cloud version your copy was based on.

::: danger The bug this documents
The editor used to treat this field as a **write counter** and increment it on every save. Live
stores were found with the world container at 7, having marched through `Deleted` (3) and out past
the end of the range. A container claiming to be deleted, or carrying a value nothing defines, is
one the service and the game are entitled to ignore. That is what "my world stopped loading after I
edited it" was.

It also minted its own ETag as `"0x{ToFileTimeUtc():X}"`, while the game writes a token of
`DateTime.Ticks` magnitude (`0x8DE…` against our `0x1DD…`, roughly 4.7x smaller, which reads as the
year 1826). So every edit also looked decades old to the cloud.

Both are fixed, and `gamepass repair` resets containers left in an undefined or `Deleted` state, or
whose state and ETag contradict each other.
:::

### Sync flags

The header `u32` after the index FILETIME is a flags field:

| Bit | Meaning |
| --- | --- |
| 1 | FullyUploaded |
| 2 | FullyDownloaded |
| 16 | HasUnresolvedConflicts |

A healthy fully-synced store reads 3. A live store found mid-problem read 18
(`FullyDownloaded | HasUnresolvedConflicts`). The editor used to preserve the header verbatim, so
every edit re-stamped whatever was there, including an unresolved conflict.

On write it now clears `FullyUploaded`, because the store holds something the cloud does not.
It deliberately does **not** clear the conflict bit: only Xbox can decide a conflict is resolved,
and clearing it locally would hide a real problem rather than fix one. `gamepass status` reports it
instead.

The index FILETIME is refreshed on every write and **strictly advances** (`max(now, previous + 1)`).
Cloud sync compares it to decide which copy is newer, so a same-or-older stamp is enough to lose an
edit to the cloud copy. Confirmed by diffing two game-written indexes: the game rewrites this value
on every save.

## `container.N` holds two blob ids, and they are not a duplicate

```
u32 constant (4)
u32 blob count (1)
128-byte fixed UTF-16 name field ("Data", zero padded)
16 bytes  blob id as the cloud last knew it
16 bytes  blob id of the file on disk
```

Confirmed across four independent implementations (Z1ni/XGP-save-extractor, LukeFZ,
Fr33dan/GPSaveConverter, libNOM.io). In a settled container the two are identical, which is what the
editor writes. They differ while a sync is in flight, which gives the read rules:

- **Both present and different:** a sync is genuinely in flight and nothing on disk says which side
  wins. Refuse, rather than hand back the wrong save as though it were the right one.
- **Current missing, previous present:** use the previous one. It is a recorded alternative, not a
  guess.
- **Neither present:** last resort, scan the folder for a GUID-named blob, and accept it only when
  its size matches the size the index records. A sole candidate with no size check used to be
  accepted, and `gamepass repair` then made that guess permanent.

The game keeps exactly **one** `container.N` and one blob per folder, so the editor prunes the
superseded generation after committing the index. Keeping old generations is what made the
folder-scan fallback ambiguous in the first place. The whole-folder `.bak` (capped at eight) is the
real rollback.

`containers.index` and the manifests are written through a temp file plus an atomic replace: a
truncated index loses every container in the store at once, which no per-save backup can undo.

## The `ABF_SAVE_VERSION` bundle

```
FString  "ABF_SAVE_VERSION"
u32      version (3)
u32      total uncompressed size of all members (passed to the Oodle decoder as rawLen)
u32      16
u32      member count
  per member: FString path, u32 uncompressed size, FString save class, u32 flag
u32      method (1 = Oodle)
u32      compressed size
         one Oodle stream, decompressing to every member body concatenated in TOC order
```

Members are carved out of the decompressed stream by their recorded sizes. Member paths look like
`Profile/Worlds/<World>/WorldSave_Facility` and `Profile/Worlds/<World>/PlayerData/Player_<id>`,
with no extension.

Bundle FStrings follow the engine's convention: a **positive** length counts ASCII bytes, a
**negative** one counts UTF-16 characters. The reader was ASCII-only for a while, so any non-ASCII
world name failed to open.

Re-serialization is faithful for everything untouched. The Oodle bytes differ from the game's own
compressor but decompress to identical bytes, which is what the game actually reads.

### The difficulty-settings member

One member carries **flag = 1** instead of 0: the world's `SandboxSettings.ini`. It is the only
member that is not a GVAS body. Its bytes are **plaintext with every byte decremented by one**, so
`[SandboxSettings]` is stored as `` ZR`mcanwRdsshmfr\ ``; decoding adds one back. Verified against a
real dump.

It is identified by that flag and never by its path, because the game records it under the
**absolute Windows path it had on the machine that wrote the save**
(`C:/Users/<someone>/AppData/.../SandboxSettings.ini`). Member paths are therefore genuinely
untrusted input, and extraction uses the leaf name only.

Both conversion directions used to drop this member, which silently reset every converted world to
default difficulty. It now travels with the world.

## Headerless members

A full Abiotic Factor save on disk is: GVAS header (magic, versions, custom formats, class name),
then a class-specific custom header, then one unknown byte, then the properties.

A bundle member **begins at that unknown byte**. The custom header sizes are:

| Save class | Custom header | Size |
| --- | --- | --- |
| `Abiotic_CharacterSave_C` | `[int Version][int DataLength]` | 8 |
| `Abiotic_WorldSave_C` | `[FString "ABF_SAVE_VERSION"][int Version][int Id][int DataLength]` | 33 |
| `Abiotic_WorldMetadataSave_C` | same as world | 33 |

To read a member the editor prepends a class-matched header captured from a real save. To write one
it re-serializes normally and then finds the body boundary by locating the save class name and
skipping the custom header, so it works on any save of that class rather than only ones the editor
wrote. `DataLength` is recomputed by the writer and ignored on read, so the splice is lossless.

Abiotic Factor has no per-save checksum (unlike, say, Starfield's CRC32 table of contents), so byte
edits need no integrity fixup.

## What a write actually does

1. Back up the whole wgs folder (once per session, eight kept).
2. Re-serialize the edited member, rebuild the bundle, Oodle-compress it.
3. Write a fresh GUID blob file.
4. Write `container.<N+1>` naming it (both ids identical).
5. Update the index entry: number, size, entry FILETIME, and state set to `Modified` or `Created`
   per the ETag rule. The ETag itself is echoed untouched.
6. Rewrite the index header: container count, strictly advanced FILETIME, `FullyUploaded` cleared.
7. Prune the superseded manifest and blob.

Order matters: blob, then the manifest naming it, then the index naming the manifest. A crash at any
point leaves the previous generation still fully described, never a manifest pointing at a blob that
does not exist.

## Where this knowledge came from, and what is still unverified

Sources:

- A real Game Pass save dump analysed byte by byte (2026-06-19), plus a live store from a player
  whose world kept vanishing, plus sixteen editor backup folders of it showing the state field
  climbing 1, 2, 3, 4, 5, 6, 7 over successive edits.
- [libNOM.io](https://github.com/zencq/libNOM.io) for the container state mapping and the
  prune-on-write behaviour.
- [LukeFZ/XblContainerReader](https://github.com/LukeFZ/XblContainerReader) (LibXblContainer) for
  the index layout, the sync flags, and the fact that its `SetModified()` never touches the ETag.
  Its state mapping is the one **not** to follow.
- `palworld-xgp-import`, `palworld-save-pal`, Z1ni/XGP-save-extractor and Fr33dan/GPSaveConverter as
  independent cross-checks on the state/ETag invariant and the two blob ids.
- Microsoft's Connected Storage documentation for sync behaviour: conflict resolution is
  user-driven, and data written straight to disk uploads only the next time the title launches and
  acquires the Game Saves provider.

Still unverified from here:

- **Whether Xbox accepts a rewritten container in-game.** That needs a real sync cycle on a real
  machine with a signed-in account. Connected Storage sync cannot be invoked from outside the title
  (it needs the title's service configuration id and a signed-in Xbox Live account), so observing
  the before/after of a real sync is the only proof available. `gamepass snapshot` and
  `gamepass compare` exist for exactly that, and `GamePassSyncFidelityTests` models the recency rule
  because real Xbox infrastructure is out of reach.
- The meaning of the reserved header bytes and the `16` bundle header field. Both are round-tripped
  verbatim.
- Whether any state value above 5 has a meaning at all. The editor treats them as damage, on the
  evidence that only its own older builds are known to have produced them.

The editor writes wgs files directly rather than through the Connected Storage API, so it cannot
update any service-side sync database and cannot repair a corrupt **cloud** copy. That limit is why
the offline routine in the [player guide](/guide/game-pass) is not optional advice.
