# Game Pass save format (internals)

This page documents how a Game Pass / Microsoft Store save is packaged on disk, for anyone working
on the editor or curious about the format. **You do not need any of this to edit a Game Pass save**:
the desktop app and CLI unpack, edit, and repack the container for you. For the user-facing workflow
(opening, editing, converting) see the [Game Pass saves guide](/guide/game-pass).

## Why it differs from Steam

A **Steam** save is a folder of loose `.sav` files - one per world region, plus a player file per
character. A **Game Pass** save packs that entire world (every `WorldSave_*` region and every player)
into **one compressed blob** inside an Xbox Connected Storage ("wgs") container. The save *content*
is identical to Steam (the same GVAS data); only the **packaging** differs, which is why a world
converts losslessly between the two.

## Four nested layers

From outside in:

1. **wgs container** - `containers.index` maps logical container names to GUID-named folders; each
   folder holds a `container.N` manifest pointing at a GUID blob file.
2. **`ABF_SAVE_VERSION` bundle** - the world blob is a small archive: a table of contents (member
   path + save class + size) followed by one **Oodle-compressed** stream of every member.
3. **Headerless members** - each packed save is the GVAS *property body* only; the save class lives
   in the table of contents. The editor splices a class-matched header back on to read it, and
   strips it again on write.
4. **GVAS** - the same save data the Steam version stores. See the
   [player](/reference/player-save-schema) and [world](/reference/world-save-schema) save schemas.

Oodle (de)compression uses the same library the editor already uses for the game's pak files; it is
downloaded on demand if it isn't already present.

## Account ids

Game Pass players are owned by a 16-19 digit **Xbox account id (XUID)** instead of a SteamID64. Each
`<XUID>_<id>` folder (the one containing `containers.index`) is one Xbox account's save store.
Converting to Steam can optionally re-home the player to a SteamID64; see
[Converting between Steam and Game Pass](/guide/game-pass#converting-between-steam-and-game-pass).
