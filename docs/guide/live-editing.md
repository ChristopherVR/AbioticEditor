# Live-edit a running game

::: warning Early
This is not full parity with the file editor yet, but it now covers most of it: player vitals,
skills, backpack / gear / hotbar, and NPCs near you, plus (when you are hosting) the world clock
and weather, quest flags, doors, storage containers, and items lying on the ground. It needs a
mod you install yourself - see below.
:::

Editing a save file works with the game closed. Live editing is the opposite: you connect to a
game that is **open right now** - your own locally-hosted session, or a dedicated server you run
- and changes take effect immediately, no save/reload needed.

## What you need

1. **[UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)** installed for the game (it already is, if
   you use other Abiotic Factor mods - most of them need it too).
2. **The AbioticEditorLiveAgentLua Scripts folder**, installed like any other UE4SS Lua mod, into
   the game's (or your dedicated server's) `Binaries/Win64/ue4ss/Mods/` folder.
3. **AbioticEditorLiveAgentHelper.exe**, a small companion program, running alongside the game
   (it is not injected into anything - just a console window you leave open). See
   `live-agent/README.md` in the editor's source repository for how to build and run both -
   this part is maintainer/advanced-user territory for now, not a one-click install yet.
4. The **token** the helper program prints on first run and writes to its own `token.txt`.

## Connecting

1. Launch the game (or your dedicated server) with the Lua mod installed, and start
   `AbioticEditorLiveAgentHelper.exe` alongside it.
2. Open the desktop editor. On the "What do you want to do?" screen, choose **Live-edit a
   running game**.
3. Enter:
   - **Host**: `127.0.0.1` for a game running on this PC, or your dedicated server's address.
   - **Port**: `42117` unless you changed it.
   - **Token**: from the helper program's `token.txt`.
4. Press **Connect** (a game on this PC connects by itself). The tabs fill in with the running game's current values.

## Editing

Changes apply **the moment you press Apply** - there is no `.bak` backup the way a file edit
gets, because there is no file being written. If you want to back out, press **Revert** before
Apply, not after.

### What you can change

| Tab | What it does | Who can use it |
|---|---|---|
| **Character stats** / **Skills** | The same vitals and skill editors as the file editor, staged until APPLY. | Anyone (your own character; other connected players if you host). |
| **Inventory** | Backpack, gear and hotbar slots - item, count, durability - one slot at a time. | Anyone, for their own character. |
| **NPCs** | Kill, revive, disable or make invincible any creature currently loaded. | Host only. |
| **World** | Set the time of day or day number; start a weather event now (or clear it) or queue one for tomorrow. | Host only. |
| **Quest flags** | Set or clear story flags in the running world. Setting a flag also sets the story steps it depends on, and the game reacts (doors unlock, effects play) exactly as if you had earned it. | Host only. |
| **Doors** | Open, close, lock or unlock any hinged or security door currently loaded around the players. | Host only. |
| **Containers** | Every crate, locker and cabinet loaded around you: pick one and edit its slots like an inventory. | Host only. |
| **Dropped items** | Everything lying loose in the loaded world, with a REMOVE button per item. | Host only. |

"Loaded" matters: the game only keeps the sectors near the players in memory, so a door or crate
on the far side of the Facility will not be in the list until someone walks near it. Use REFRESH
after moving. The world tabs also need a world to be loaded - from the main menu they say so and
the character tabs still work.

::: tip Joined a friend's game?
As a client you can still edit your own character. Everything under the world tabs is owned by
the host's game, and the tabs say so - changes made from a client would just be overwritten.
:::

::: tip No file-editing risk
Live editing never touches the save file on disk. It reads and writes the game's own live memory
through the mod. Your save file stays exactly as it was until the game itself writes it (which
still makes normal backups, unaffected by any of this).
:::

## Only the browser editor is excluded

Live editing is a desktop-app feature only. The browser-based editor (the one published to
GitHub Pages) does not offer it - a browser tab has no way to reach a TCP port on your own
machine or a game server, so the choice screen does not even appear there. Everything else about
the browser editor is unchanged.
