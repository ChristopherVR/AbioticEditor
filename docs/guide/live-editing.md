# Live editing (experimental)

Live editing changes your game while you play. **It is experimental and has fewer tools than
offline editing.** Game updates can affect compatibility. Choose offline editing when you need
the full set of tools or want to review changes before saving.

The Windows desktop app takes care of local setup. You do not need a separate installer,
manual mod copying, a compiler, or developer tools. After you approve setup, the app downloads
missing support files and prepares its bundled agent and helper. This adds files to your game
folder: live editing still needs an in-game component, but the app handles it for you.

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

Automatic local setup is **Windows-only**. The Linux desktop editor can edit save files, but
cannot run the bundled Windows helper or automatically install the live agent locally.

1. Close Abiotic Factor or stop its server before installing or updating live support.
2. In the complete Windows desktop release, use the editing-mode button in the top bar.
   Choose **Set up live editing**, then **This PC**.
3. Review the detected game folder and choose **Set up automatically**. The first download
   needs internet access.
4. Wait while the app prepares the files. When prompted, start the game and load a world.
5. Keep the editor open. It starts the helper and reads your connection details automatically.
   Once connected, select a player or world editing area.

If the editor cannot find your game, check **Settings > Game Data**. If it reports that the
helper is unavailable, check your extracted release contains the `live-agent` folder. Source
builds may require building the helper separately; the [agent source instructions](https://github.com/ChristopherVR/AbioticEditor/tree/main/live-agent)
cover manual installation and compilation.

### What automatic setup installs

The app downloads a missing UE4SS runtime from the
[official release service](https://github.com/UE4SS-RE/RE-UE4SS/releases), checks its size and
published SHA-256 digest, and installs the required runtime and shared files. Example and cheat
mods from that package are excluded. The app then copies its own bundled Lua agent, enables its
entry in `mods.txt`, and starts the bundled helper in the background. You do not need to type a
token or keep a command window open for **This PC**.

The download uses UE4SS's `experimental-latest` channel. Verification checks the downloaded
file against its publisher's digest; it does not guarantee compatibility with every game patch.

Existing complete UE4SS installations are reused without automatically upgrading them. Other
mods and their settings are preserved. An incomplete installation or conflicting mod loader
stops setup rather than being overwritten.

### Reconnect or update

Choose live editing and **This PC** again. If everything is current, no download is needed.
The app checks every bundled agent script for updates, including individual feature modules.
An editor update may ask to update those files: close the game first, allow setup, then restart
and load your world. Selecting a save folder does not configure live setup; use the installed
game folder under **Settings > Game data**.

## Connect to a server you run

Prepare live support on the **game server machine**. With desktop access on Windows, run the
editor there and use **This PC** setup while the server is stopped. Start the server afterward
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
| Download or verification failed | Check access to GitHub and retry; unverified files are not installed |
| Folder permission error | Check your Windows account can write to the selected game folder |
| Existing or incomplete mod loader | Close the game and back up your mod folder. Repair your existing UE4SS installation using its own instructions before retrying; setup leaves conflicting files unchanged |
| Missing live-support files | Extract the full Windows release again, including its `live-agent` folder; source builds need the helper build described in the agent README |

### Files and logs

These locations are for troubleshooting. Normal setup manages them for you.

| Location | Purpose |
| --- | --- |
| `<game>/Binaries/Win64/dwmapi.dll` | Loader added during fresh automatic setup |
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
Choosing automatic live setup again can re-enable the agent.
