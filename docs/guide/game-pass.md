# Game Pass / Microsoft Store saves

Abiotic Factor on **Game Pass / Microsoft Store** stores its saves completely differently from the
Steam version. The editor reads, edits and writes them anyway, and can convert a world between the
two. This page explains how the format works, how to open a Game Pass save, where it lives on disk,
and how conversion works.

## How a Game Pass save differs from Steam

A **Steam** save is a folder of loose files:

```
…/SaveGames/<SteamID64>/Worlds/<World>/
  WorldSave_MetaData.sav
  WorldSave_Facility.sav
  …
  PlayerData/Player_<SteamID64>.sav
```

A **Game Pass** save is an Xbox **"wgs" (Connected Storage) container**: the whole world - every
`WorldSave_*` region and every player character - is packed into **one compressed blob** inside a
folder of GUID-named files plus a `containers.index`. There are no loose `.sav` files, and players
are owned by a 16-19 digit **Xbox account id (XUID)** instead of a SteamID64.

The save *content* is identical to Steam (the same GVAS data); only the **packaging** differs. The
editor unpacks the container, edits the saves with the normal tools, and repacks it - so a Game
Pass save behaves like any other save in the app.

## Opening a Game Pass save in the desktop app

You don't need to do anything special:

- **Auto-detected worlds** - Game Pass worlds appear in the start screen's discovered-worlds list
  with a **GAME PASS** tag. Click one to open it.
- **Open Folder** - pick the Game Pass save folder directly (the one that contains
  `containers.index`). The editor detects it and opens the world.

Once open, the sidebar shows a **GAME PASS** badge so you always know the save type. Edit and press
**SAVE** as usual - the editor writes your changes straight back into the Xbox container and keeps a
backup of the whole save folder on the first write. There is no separate "apply" step.

::: tip
After editing, **verify the save loads in-game** before relying on it. The editor produces a valid
container, but only the Xbox app / the game on your machine can confirm it accepts the rewrite.
:::

## Where Game Pass saves and installs live

The editor auto-detects both of these; you normally never type a path.

**Saves** are in one of (the editor scans both, on every fixed drive):

```
%LOCALAPPDATA%\Packages\<AbioticPackage>\SystemAppData\wgs\<XUID>_<id>\
<drive>:\XboxGames\GameSave\wgs\<XUID>_<id>\
```

Each `<XUID>_<id>` folder (the one with `containers.index`) is one Xbox account's save store. The
discovered-worlds list shows the account id and the folder, so you can see exactly which one you're
opening.

**The game install** (needed for item names, icons, recipes and trader data) is auto-detected from:

```
<drive>:\XboxGames\<Game Name>\Content\…\AbioticFactor\Content\Paks
```

If your install is somewhere the editor can't find, set it in **Settings → Game Data → Set game
folder**. The Game Data card shows the install path currently in use.

## Converting between Steam and Game Pass

Because only the packaging differs, a world converts losslessly in either direction. **Settings →
Convert** offers both:

- **Steam → Game Pass** - pick a Steam world folder; a Game Pass container copy is written next to
  it.
- **Game Pass → Steam** - pick a Game Pass save folder; a loose-file Steam world folder is written
  next to it.

You can optionally enter a **player account id** to re-home the (single) player to a different
account while converting - for example, converting a Game Pass world to Steam and giving it *your*
SteamID64 so your Steam game loads it as yours. Leave it blank to keep the existing ids.

When you **create a new world**, the editor also writes a Game Pass copy next to the Steam folder, so
the world is ready for either platform.

::: warning Where to put a converted save
- A converted **Steam** world goes under
  `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<SteamID64>\Worlds\`.
- A converted **Game Pass** container goes under the game's wgs storage (see the paths above). The
  player ids are kept unless you re-homed them, so on the target platform a character may need
  re-homing to that account.
:::

## Command line

The CLI exposes the same operations under `abioticeditor gamepass`:

```console
abioticeditor gamepass discover                       # list Game Pass saves on this machine
abioticeditor gamepass list <wgs-folder>              # the worlds/players packed in a save
abioticeditor gamepass extract <wgs-folder> <member> <out.sav>
abioticeditor gamepass import  <wgs-folder> <member> <in.sav>   # backs up the folder
abioticeditor gamepass to-steam    <wgs-folder> <dest> [--player-id <id>]
abioticeditor gamepass to-gamepass <steam-world> <dest> [--player-id <id>]
```

`gamepass discover` prints each detected save's account id and folder, so it doubles as "where are
my Game Pass saves".

## How it works internally (for the curious)

Four nested layers, from outside in:

1. **wgs container** - `containers.index` maps logical container names to GUID folders; each folder
   has a `container.N` manifest pointing at a GUID blob file.
2. **`ABF_SAVE_VERSION` bundle** - the world blob is a small archive: a table of contents (member
   path + save class + size) followed by one **Oodle-compressed** stream of every member.
3. **Headerless members** - each packed save is the GVAS *property body* only; the save class lives
   in the table of contents. The editor splices a class-matched header back on to read it, and
   strips it again on write.
4. **GVAS** - the same save data the Steam version stores.

Oodle (de)compression uses the same library the editor already uses for the game's pak files; it is
downloaded on demand if it isn't already present.
