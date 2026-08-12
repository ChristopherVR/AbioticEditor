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

## Before you edit: the offline routine

::: danger Read this once, then it's muscle memory
A Game Pass save is not the only copy of your world. Xbox keeps one in the cloud, and **when the
two disagree the cloud copy can win** - which means an edit can vanish hours later, with nothing
having looked wrong at the time. The editor marks every write as the newest version, but it cannot
overrule Xbox on its own.

The routine that makes edits stick:

1. Fully close Abiotic Factor **and** the Xbox app (check the system tray).
2. **Go offline** (Wi-Fi off / airplane mode). This is the step that does the work.
3. Make your edits in the editor and press **SAVE**.
4. **Still offline**, launch the game once, load the save, save in-game, and quit. Now the game
   itself owns your edit as the newest copy.
5. Reconnect. If Xbox asks, choose **keep local / upload**.

If you save while online, or pick "download from cloud" at the prompt, the cloud copy wins and the
edit is gone. The editor shows this as a reminder the first time you open a Game Pass world, and it
backs the whole save folder up before every write either way.
:::

If the editor says the save **needs a quick repair**, that means Xbox left it half-synced: the save
points at a piece of data that is not on disk. The editor found the real one, and offers to fix the
pointer so the warning stops. Do it with the game and Xbox app closed - and note that a save in this
state was already broken before the editor touched it.

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

Both directions carry the world's **difficulty settings** (`SandboxSettings.ini`) along with the
saves, so a converted world plays the way the original did.

::: warning Where to put a converted save
- A converted **Steam** world goes under
  `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<SteamID64>\Worlds\`.
- A converted **Game Pass** container is **a save folder of its own**, not something you can drop
  next to your existing Xbox saves: each folder carries a list of what is in it, so copying one over
  another would hide every world already there. To put a converted world into your real Xbox saves,
  **merge** it in with `gamepass to-gamepass --into` (below), with the game and the Xbox app closed.
- The player ids are kept unless you re-homed them, so on the target platform a character may need
  re-homing to that account.

The editor refuses to convert into a folder that already holds a save, rather than writing over it.
:::

## Command line

The CLI exposes the same operations under `abioticeditor gamepass`:

```console
abioticeditor gamepass discover                       # list Game Pass saves on this machine
abioticeditor gamepass list <wgs-folder>              # the worlds/players packed in a save
abioticeditor gamepass extract <wgs-folder> <member> <out.sav>
abioticeditor gamepass import  <wgs-folder> <member> <in.sav>   # backs up the folder
abioticeditor gamepass rename-player <wgs-folder> <member> <new-id>   # re-home a packed character
abioticeditor gamepass repair  <wgs-folder>           # fix a half-synced save
abioticeditor gamepass to-steam    <wgs-folder> <dest> [--container <name>] [--player-id <id>]
abioticeditor gamepass to-gamepass <steam-world> <dest> [--player-id <id>] [--into]
```

Use `rename-player` rather than extracting, running `steamid`, and importing again: the container
keeps its own list of names, so an imported file goes back under the **old** name and the old
account id quietly returns. `--into` adds a converted world to the Xbox save folder already at
`<dest>`, keeping the worlds already in it.

`gamepass discover` prints each detected save's account id and folder, so it doubles as "where are
my Game Pass saves".

## How the format works

You don't need any of this to edit a Game Pass save - the editor handles the packaging for you.
If you're curious how the wgs container, the `ABF_SAVE_VERSION` bundle, the headerless members, and
the Oodle compression fit together, that's documented under
**[Game Pass format](/reference/game-pass-format)** in the technical reference.
