# Live-edit a running game

Live editing connects the desktop editor to an open game through a UE4SS Lua mod and a companion
helper. Changes affect the running session. The game can later persist them in its normal saves.
The browser edition does not support live connections.

::: warning Live changes have no editor backup
Vitals and skills apply automatically after a short pause in input. Other controls send changes
when you use their action or commit the edit. There is no universal SAVE or undo step for live
changes, and no `.bak` from the editor. Back up your world before starting a session you want to experiment with.
:::

## Set up a game on this PC

Automatic local setup is **Windows-only**. The Linux desktop editor can edit save files, but
cannot run the bundled Windows helper or automatically install the live agent locally.

1. Install [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) for Abiotic Factor if it is not already
   installed. The editor does not install this third-party mod framework for you.
2. In the desktop editor, choose live editing and **This PC**.
3. If asked, approve copying the bundled agent into the detected game's
   `Binaries/Win64/ue4ss/Mods/AbioticEditorLiveAgentLua` folder. The editor also enables its
   entry in `mods.txt`. An updated agent can require this step again.
4. Start or restart the game after installing/updating the mod, then load a world.
5. The editor starts its bundled helper and reads the local connection details automatically.
   Wait for it to connect, then select a player or world surface.

If the editor cannot find your game, check **Settings > Game Data**. If it reports that the
helper is unavailable, check your extracted release contains the `live-agent` folder. Source
builds may require building the helper separately; the [agent source instructions](https://github.com/ChristopherVR/AbioticEditor/tree/main/live-agent)
cover manual installation and compilation.

## Connect to a server you run

Install the Lua agent and run the helper on the game server machine. Choose the remote-server
option in the desktop editor and enter the host, port, and token supplied by that helper.
The default port is `42117`; use the helper's actual port if it selected another one.
Local connection files live under `%LOCALAPPDATA%\AbioticEditorLiveAgent` (`token.txt` and `port.txt`).

The helper must be reachable from the editor and the server must have a world loaded. A token
connects the editor to the agent; joining an ordinary multiplayer server does not install an
agent there or give you authority over that world. See the [protocol reference](/reference/live-editing-protocol)
for the connection format and the agent README for listener configuration.

## What is available

The available controls depend on the selected player, host authority, and the objects the game
currently has loaded. World changes require host authority. A client can edit supported parts
of their own character; a host can select other connected players.

| Area | Current controls and limits |
| --- | --- |
| Vitals and skills | Read and change values; edits apply automatically |
| Inventory and transmog | Edit supported slots, give items, and change transmog visibility |
| Recipes | Unlock or lock recipes, including batched bulk changes |
| GatePal | Fish, emails, journals, and supported compendium sections; kill-requirement sections stay read-only |
| General and spawn | Background/PhD and supported discovery fields, position/respawn actions; traits and crafted counters remain read-only |
| Story and quest flags | Change chapter and flags with prerequisite planning; world-wide recipe unlocks are read-only |
| Clock and weather | World day, time, and supported weather actions |
| Containers and ground items | Edit loaded storage, add ground items, and remove individual or multiple dropped items |
| Creatures | Select a loaded creature to inspect its status and use kill/revive or other available controls |
| Story NPCs, traders, and pets | Supported narrative, availability, and pet controls; pet species changes are unavailable |
| Doors, portals, vehicles, and containment | Controls for loaded objects, according to the agent's supported operations |
| Bases | Supported deployable controls; live bench-upgrade installation is disabled |

Some operations have been verified in the real game; others have only source/data evidence and
offline harness coverage. The [protocol reference](/reference/live-editing-protocol) records these
limits per command. Bench-upgrade calls were disabled following a reported native crash, and
are not available as an experimental toggle.

## Missing objects or a failed connection

- **An object is absent:** only objects in loaded sectors can appear. Move a player near it and refresh.
- **No world is available:** load into the game, rather than staying on its main menu.
- **A world action is unavailable:** check that you are hosting and that the agent supports that action.
- **Connection times out:** check UE4SS loaded the mod, restart after an agent update, and check the helper is running.
- **A change fails:** read the displayed error. Refresh to see the game's actual state before retrying.

Disconnecting stops editing; it does not reverse changes. To edit files instead, disconnect,
close the game or stop the server, and follow the [save-file guide](./getting-started).
