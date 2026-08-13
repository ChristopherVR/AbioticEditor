# Game Pass / Microsoft Store saves

Abiotic Factor on **Game Pass / Microsoft Store** keeps your progress in a different shape from the
Steam version. The editor opens it anyway, and once a Game Pass world is open everything works
exactly as it does for a Steam save: edit, press **SAVE**, play on.

There is one thing about Game Pass that is not like Steam, and it is the thing that catches people
out. **Your save is not the only copy.** Xbox keeps a copy in the cloud, and when the two disagree
the cloud copy can win. That is why a Game Pass edit can look perfectly fine and then be gone hours
later, with nothing having gone wrong at the time.

The routine below is how you stop that happening. If something has already gone wrong, skip to
[Something already went wrong](#something-already-went-wrong).

## Before you edit: the offline routine

::: danger Read this once. Then it is muscle memory.
Do all six steps, in this order, every time you edit a Game Pass save.

1. **Close the game and the Xbox app.** Both of them. Check the system tray for the Xbox app,
   because closing its window does not always close it.
2. **Wait about a minute.** Xbox keeps uploading for up to half a minute after a game exits, and
   editing into that window is asking for a fight you will lose.
3. **Go offline.** Wi-Fi off, or airplane mode. This is the step that does the real work: with no
   connection there is nothing to overrule your edit.
4. **Make your edits and press SAVE.** Still offline.
5. **Still offline, launch the game once.** Load the world, save in-game, quit.
6. **Now reconnect.** If Xbox asks which copy you want to keep, choose the one from **this
   device** (the wording varies: "keep local", "upload to cloud", "this PC").

Step 5 is the one people skip, and it is not superstition. Microsoft's own documentation for its
save tooling says that data written straight to disk only gets uploaded "the next time the title
launches and acquires the Game Saves provider". In plain terms: the game itself has to pick your
edit up and call it the newest version. Until it does, Xbox has no reason to believe your copy is
the good one.

If you save while online, or you pick the cloud copy when asked, the cloud copy wins and your edit
is gone. The editor shows you this reminder the first time you open a Game Pass world, and it
copies the whole save folder to a backup before it writes, either way.
:::

::: warning The "which copy do you want?" prompt deletes the other one
When Xbox asks you to choose between the copy on your PC and the copy in the cloud, the one you do
**not** pick is deleted. There is no undo, no version history, and it does not show you what is
inside either one before you choose.

If you have just edited offline and then launched the game offline, the copy on your PC is the one
you want. If you are not sure, copy your whole save folder somewhere safe first.

Two related traps worth knowing about. A Game Pass game can refuse to launch while you are offline
if you have never played it on that PC before, so play it online once first. And the prompt does
not always appear: sometimes the save just quietly reverts instead.
:::

::: tip Things that are simply not possible on PC
There is no per-game switch to turn Xbox cloud saves off on PC, and no way to delete a cloud save
from the PC Xbox app. Going offline at the right moment really is the only lever you have.

If you would rather not deal with any of this, you can
[convert your world to the Steam layout](#converting-between-steam-and-game-pass) and edit that
instead. A Steam-style save has no cloud copy arguing with it.
:::

## Something already went wrong

Start here in all three cases:

```console
abioticeditor gamepass status "<your Game Pass save folder>"
```

`gamepass status` only looks. It changes nothing, so it is always safe to run. It tells you how
Xbox currently sees the save: whether there is an argument between your PC and the cloud that Xbox
has not settled yet, and whether any part of the save is in a state Xbox does not recognise. Run
`abioticeditor gamepass discover` first if you do not know where your save folder is.

### My world will not load any more

Most likely cause: **an older version of this editor (before v2.5) mislabelled your save.**

Every Xbox save folder carries a note for the cloud about each part of the save, saying whether it
matches the cloud copy. Older versions of the editor treated that note as a counter and bumped it
up on every save. Bumped far enough, a world ends up claiming to be *deleted*, or claiming
something Xbox has no meaning for at all. Either is enough for Xbox or the game to skip straight
past it. That is what "my world stopped loading after I edited it" looks like from the inside.

`gamepass status` reports this as parts of the save carrying "a state Xbox does not define". To fix
it, with the game and the Xbox app closed:

```console
abioticeditor gamepass repair "<your Game Pass save folder>"
```

The repair copies your whole save folder to a backup first, then puts each mislabelled part back to
an honest description of itself. In the app the same fix is offered as a **Repair now** button when
you open a save that needs it.

Repairing puts the note right. It does not make Xbox forget the cloud copy, so follow the
[offline routine](#before-you-edit-the-offline-routine) afterwards: stay offline, launch the game
once and save in-game, and only then reconnect.

### My edits keep reverting

This is the cloud copy winning. Work through the checklist:

- Were the game **and** the Xbox app fully closed, and had you waited a minute?
- Were you offline for the whole edit, including pressing SAVE?
- Did you launch the game once **while still offline** and save in-game before reconnecting?
- When Xbox asked which copy to keep, did you pick the one from this device?

If all of that was right and edits still revert, run `gamepass status`. An unsettled conflict shows
up there as a warning. A save in that state is already mid-argument with the cloud before you touch
it, and anything you write is one side of an argument that gets decided later, out of sight.
Launch the game, let it load and save that world normally, and let Xbox settle it before you edit
again.

You can also catch it in the act. Take a fingerprint of the folder, let Xbox do its thing, then
compare:

```console
abioticeditor gamepass snapshot "<save folder>" before.json
abioticeditor gamepass compare  "<save folder>" before.json
```

`compare` lists which parts of the save were dropped, rolled back or changed in between. That turns
"I think my edit vanished" into something you can actually show.

### My world has vanished

If a world you know exists no longer shows up in the editor or in the game, Xbox has most likely
dropped it from the save folder's own list of contents. The world's data usually **still sits on
disk**, unlisted.

With the game and the Xbox app closed:

1. Look next to your Game Pass save folder for folders with the same name plus `.bak`. The editor
   copies the whole save folder there before every write, and keeps the last eight.
2. Pick the newest one from before things went wrong and check it with
   `abioticeditor gamepass list "<that .bak folder>"`. It lists the worlds and characters inside.
3. When you find the one you want, copy that backup folder's contents back over your save folder.
4. Follow the [offline routine](#before-you-edit-the-offline-routine) so your restored copy is the
   one Xbox keeps.

If the editor ever refuses to open a save because there are **two versions of its data on disk**,
that is Xbox part-way through a sync. Nothing on disk says which one is meant to win, so the editor
will not guess for you. Close the game and the Xbox app, go back online, let syncing finish, and
try again.

::: warning What the editor cannot promise
The editor writes a Game Pass save in the same shape the game writes it, and it now tells Xbox the
truth about what it changed. What it cannot do is settle an argument with the Xbox service on your
behalf, restore a cloud copy, or guarantee Xbox accepts a rewritten save. Always check that a world
loads in-game before you rely on it.
:::

## Opening a Game Pass save in the app

Nothing special to do:

- **Auto-detected worlds.** Game Pass worlds appear in the start screen's list of discovered worlds
  with a **GAME PASS** tag. Click one to open it.
- **Open Folder.** Point the editor at the Game Pass save folder itself and it works that out too.

Once open, the sidebar shows a **GAME PASS** badge so you always know what kind of save you are
editing. Edit and press **SAVE** as usual; there is no separate "apply" step. The first write copies
the whole save folder to a backup next to it.

Three things the editor does for you along the way:

- It shows the cloud-sync reminder the first time you open a Game Pass world in a session.
- It offers a repair if the save needs one (see [above](#my-world-will-not-load-any-more)).
- If writing back into your Xbox save fails, **your work is not thrown away**. The editor keeps
  your edited copy and tells you which folder it is in, so you can close whatever was holding the
  file open and press SAVE again.

## Where Game Pass saves and installs live

The editor finds both of these on its own, so you should never need to type a path. For reference,
saves are in one of these (the editor checks both, on every drive):

```
%LOCALAPPDATA%\Packages\<AbioticFactor package>\SystemAppData\wgs\<account id>_<id>\
<drive>:\XboxGames\GameSave\wgs\<account id>_<id>\
```

Each of those folders is one Xbox account's saves. The discovered-worlds list shows the account id
and the folder, so you can see exactly which one you are about to open. `gamepass discover` prints
the same thing from the command line.

The **game install** matters too, because that is where item names, icons, recipes and trader data
come from. It is detected from `<drive>:\XboxGames\<Game Name>\`. If yours lives somewhere unusual,
set it in **Settings ▸ Game Data ▸ Set game folder**.

## Converting between Steam and Game Pass

A Game Pass world and a Steam world hold exactly the same data, packed differently, so a world
converts either way without losing anything. **Settings ▸ Convert** offers both directions, and
your world's difficulty settings travel with it, so a converted world plays the way the original
did.

You can optionally give a **player account id** while converting, to hand the character over to a
different account. For example, convert a Game Pass world to Steam and give it your own Steam id so
your Steam copy of the game loads it as yours. Leave it blank to keep things as they are.

When you **create a new world** in the editor, it writes a Game Pass copy next to the Steam one, so
the world is ready for either.

::: warning Where a converted save can go
- A converted **Steam** world belongs under
  `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<your steam id>\Worlds\`.
- A converted **Game Pass** save is a save folder of its own. You cannot drop it on top of your
  existing Xbox saves: each folder carries its own list of what is inside it, so one folder written
  over another would hide every world already there. To put a converted world into your real Xbox
  saves, **merge** it in with `gamepass to-gamepass --into` (below), with the game and the Xbox app
  closed.
- Player ids are kept unless you changed them during the conversion, so a character may still need
  handing over to the right account on the other side.

The editor refuses to convert into a folder that already holds a save rather than writing over it.
:::

## Command line

Every Game Pass operation is available from the command line under `abioticeditor gamepass`.

```console
abioticeditor gamepass discover                       # find Game Pass saves on this machine
abioticeditor gamepass status <save-folder>           # how Xbox sees this save (changes nothing)
abioticeditor gamepass list <save-folder>             # the worlds and characters packed inside
abioticeditor gamepass repair <save-folder>           # fix a mislabelled or half-synced save
abioticeditor gamepass extract <save-folder> <name> <out.sav>
abioticeditor gamepass import <save-folder> <name> <in.sav>
abioticeditor gamepass rename-player <save-folder> <name> <new-id>
abioticeditor gamepass snapshot <save-folder> <out.json>
abioticeditor gamepass compare  <save-folder> <snapshot.json>
abioticeditor gamepass to-steam    <save-folder> <dest> [--container <name>] [--player-id <id>]
abioticeditor gamepass to-gamepass <steam-world> <dest> [--world <name>] [--player-id <id>] [--into]
```

`<name>` is a world or character from `gamepass list`, for example `WorldSave_Facility` or
`Player_2533274900397709`. Anything that writes copies the whole save folder to a backup first.

A few notes worth having:

- **`status` and `list` never change anything**, so they are safe to run at any time.
- **`repair`** puts back parts of the save that are mislabelled or that point at data which is not
  there. Close the game and the Xbox app first.
- **`rename-player`** is how you hand a character to a different account. Do not extract it, change
  the id, and import it back: the save keeps its own list of names, so the character would go back
  in under the old name and the old id would quietly return.
- **`to-gamepass --into`** adds a converted world to the Xbox save folder already at that location
  and keeps the worlds already in it. Without `--into`, the editor refuses to write into a folder
  that already holds saves.
- **`snapshot`** and **`compare`** are the way to prove whether a real Xbox sync kept your edit.
  Snapshot, let the sync happen, compare.

## How the format works

You need none of this to edit a Game Pass save; the editor handles the packaging for you. If you
are curious about the container, the compressed bundle inside it, and the notes the save keeps for
the Xbox cloud, that is all written up in
**[Game Pass format](/reference/game-pass-format)** in the technical reference.
