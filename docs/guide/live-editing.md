# Live editing (experimental)

Live editing changes your game while you play. **It is experimental and has fewer tools than
offline editing.** Game updates can affect compatibility. Choose offline editing when you need
the full set of tools or want to review changes before saving.

**UE4SS is a separate prerequisite.** The editor does not bundle, download, or install it.
The app shows you the official download, the exact game folder to install into, and a button
to check again when you are done. After that, it handles its own agent and helper for you.

## Choose the right mode

| | Offline editing | Live editing |
| --- | --- | --- |
| Game state | Close the game or stop the server | Load a world in the running game |
| Changes apply | When you choose **Save** | As you edit or use an action |
| Backups | A `.bak` on every file write | No editor backup of live changes |
| Tools | Full save-file editing | Supported live tools only |
| Objects available | Data in the loaded save files | Objects currently loaded by the game |
| Browser edition | Supported | Not supported |

::: warning Live changes have no editor backup
Vitals and skills apply automatically after a short pause in input. Other controls send changes
when you use their action or commit the edit. There is no universal SAVE or undo step for live
changes, and no `.bak` from the editor. Back up your world before starting a session you want to experiment with.
:::

## Set up a game on this PC

Local helper setup is **Windows-only**. The Linux desktop editor can edit save files but cannot
run the bundled Windows helper locally. The browser edition cannot connect live.

### 1. Let the app find your game

Close Abiotic Factor or stop its server. In the complete Windows desktop release, choose the
editing-mode button, **Set up live editing**, then **This PC**. If UE4SS is missing or incomplete,
the app shows **Install UE4SS first** and the detected game's `Binaries/Win64` folder.

If the game cannot be found, choose its installation under **Settings > Game data** and retry.
This is the installed game folder, not the folder containing your saved worlds.

### 2. Get UE4SS from its official publisher

Choose **Get UE4SS** in the app to open the
[official experimental release page](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/experimental-latest).
Download the standard `UE4SS_*.zip` package, rather than `zDEV` or a source-code archive.
The linked experimental channel changes over time; compatibility with every Abiotic Factor
patch is not guaranteed. An existing working installation does not need to be replaced merely
to use the editor.

UE4SS is the mod loader that allows the editor's agent to communicate with the game. It is
provided separately by its own publisher. See the
[official installation instructions](https://docs.ue4ss.com/dev/installation-guide.html) for
loader configuration and alternate layouts.

### 3. Extract it into the folder the app shows

Use **Open game folder**, or copy the displayed path into File Explorer. Extract the ZIP's
contents there, keeping the included folder structure. For a typical Steam installation the
destination is:

```text
<Steam library>/steamapps/common/AbioticFactor/AbioticFactor/Binaries/Win64/
```

The currently linked package uses this layout:

```text
Win64/
  AbioticFactor-Win64-Shipping.exe
  dwmapi.dll
  ue4ss/
    UE4SS.dll
    UE4SS-settings.ini
    Mods/
      shared/UEHelpers/UEHelpers.lua
```

Place the archive contents directly in `Win64`, not inside an extra folder named after the ZIP.
Do not extract into your saves folder. If you already use UE4SS or another loader, follow its
upgrade instructions and preserve your existing mods and settings instead of blindly replacing
files. The editor itself does not alter the UE4SS runtime.

The editor recognizes both the nested `ue4ss/` layout above and older flat installations where
`UE4SS.dll` and `Mods/` sit directly in `Win64`. Detection requires the runtime and its shared
`UEHelpers` module. Custom redirected runtime paths are not automatically detected.

### 4. Return to the editor

Choose **Check again**. Once UE4SS is detected, the app asks to install its own agent in the
shown mod folder. Choose **Set up editor helper** while the game is still closed.

The app copies its bundled agent scripts, enables its entry in `mods.txt`, and starts its
bundled helper in the background. No compiler, manual agent copying, token entry, or command
window is needed for **This PC**. Then start the game, load a world, and wait for the editor to
connect. Select a player or world editing area.

### Reconnect or update

Choose **This PC** again. The editor checks every bundled agent script for updates, including
individual feature modules. An editor update may ask to update those files: close the game,
allow helper setup, then restart and load your world. UE4SS updates remain a separate manual
step using the publisher's instructions. The app does not automatically download them.

A complete Windows release includes the editor's `live-agent` folder. If that folder is
missing, extract the full release again. Source builds may require building the helper
separately; see the [agent source instructions](https://github.com/ChristopherVR/AbioticEditor/tree/main/live-agent).

## Connect to a server you run

Prepare live support on the **game server machine**. With desktop access on Windows, run the
editor there, install UE4SS using its guidance, then use **This PC** helper setup while the server is stopped. Start the server afterward
and keep its helper running under the same Windows account as the game. Headless installations
can use the manual agent instructions linked above. Installing support on your own PC cannot
change a server hosted elsewhere.

Choose the remote-server
option in the desktop editor and enter the host, port, and token supplied by that helper.
The default port is `42117`; use the helper's actual port if it selected another one.
Local connection files live under `%LOCALAPPDATA%\AbioticEditorLiveAgent` (`token.txt` and `port.txt`).

The helper must be reachable from the editor and the server must have a world loaded. A token
connects the editor to the agent; joining an ordinary multiplayer server does not install an
agent there or give you authority over that world. See the [protocol reference](/reference/live-editing-protocol)
for the connection format and the agent README for listener configuration.

Automatic local setup does not configure remote networking or the server firewall. The server
owner must make the helper reachable. Treat its token as a password; leave it out of shared
screenshots and bug reports.

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

### Why some features remain offline-only

These are current missing features, not options you need to enable:

- **Bench upgrades:** attempted live calls caused a native game crash. Installation and removal
  remain disabled, and the UI now respects that capability.
- **Traits:** the available character-initialization routines can reset other character state.
  A reliable targeted live setter has not been established.
- **World-wide unlock lists:** adding all required entries to the game's collections does not
  have a verified live path.
- **Pet species:** replacing a live pet needs a verified spawn and replacement operation.
- **Unloaded objects:** the game has not created them in memory. Move near the area and refresh,
  or edit that region's save offline.

Some missing skill entries also cannot be created live. An automated harness can check request
handling, but cannot prove that every game version will accept a live operation.

## Setup recovery

| Message or problem | Next step |
| --- | --- |
| Game not found | Select the installed game in **Settings > Game data**, then retry |
| Close the game | Exit the game or stop its server, return to **This PC**, and retry |
| UE4SS still missing | Check the displayed Win64 folder contains the runtime and shared UEHelpers module, without an extra archive-name folder |
| Folder permission error | Check your Windows account can write to the selected game folder |
| Existing or incomplete mod loader | Close the game, back up your mod folder, and follow the publisher's repair instructions. The editor does not overwrite the runtime |
| Missing live-support files | Extract the full Windows release again, including its `live-agent` folder; source builds need the helper build described in the agent README |

### Files and logs

These locations are for troubleshooting. The editor manages its own agent and helper; UE4SS files belong to your separate installation.

| Location | Purpose |
| --- | --- |
| `<game>/Binaries/Win64/dwmapi.dll` | Loader from the separate UE4SS installation |
| `<game>/Binaries/Win64/ue4ss/` | Runtime, shared files and settings |
| `ue4ss/Mods/AbioticEditorLiveAgentLua/Scripts/` | Bundled agent scripts |
| `ue4ss/Mods/mods.txt` | Enabled mod list |
| `%LOCALAPPDATA%/AbioticEditorLiveAgent/helper.log` | Helper startup and connection diagnostics |
| `%LOCALAPPDATA%/AbioticEditorLiveAgent/token.txt` and `port.txt` | Local credentials and actual listening port |

## Missing objects or a failed connection

- **An object is absent:** only objects in loaded sectors can appear. Move a player near it and refresh.
- **No world is available:** load into the game, rather than staying on its main menu.
- **A world action is unavailable:** check that you are hosting and that the agent supports that action.
- **Connection times out:** check UE4SS loaded the mod, restart after an agent update, and check the helper is running.
- **A change fails:** read the displayed error. Refresh to see the game's actual state before retrying.

Disconnecting stops editing; it does not reverse changes. To edit files instead, disconnect,
close the game or stop the server, and follow the [save-file guide](./getting-started).

Disconnecting does not uninstall the agent. To disable it, close the game and set
`AbioticEditorLiveAgentLua : 0` in its `mods.txt`. Keep shared UE4SS files if other mods use them.
Choosing editor helper setup again can re-enable the agent.
