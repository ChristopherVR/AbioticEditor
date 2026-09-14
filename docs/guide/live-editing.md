# Live editing (experimental)

Live editing lets the editor talk to an Abiotic Factor world that is already running. It is handy for a quick field adjustment, but offline editing is the safer workbench: it has more tools, lets you review changes before **SAVE**, and creates a `.bak` backup each time it writes.

Live changes happen as you make them. There is no universal **SAVE**, undo button, or editor backup, so make a normal in-game backup before experimenting.

| | Offline editing | Live editing |
| --- | --- | --- |
| Start here | Close the game or stop the server | Load a world in the running game |
| Changes happen | When you choose **SAVE** | As you edit or use an action |
| Safety net | A `.bak` backup on every write | Make your own backup first |
| Tools | Full save-file editor | Supported live tools only |
| Browser edition | Yes | No |


![Offline and experimental live editing choices](/screenshots/00-editing-modes.png)

*Choose the mode first. Offline edits wait for Save; live changes apply immediately.*

## Set up this PC (Windows)

Live setup works in the complete Windows desktop release. The Linux desktop app can edit save files, but cannot run the Windows helper locally. The browser edition cannot connect to a live game.

The editor needs **UE4SS**, a separate mod loader. It does not bundle, download, or install UE4SS for you.

1. Close Abiotic Factor, or stop the server.
2. In the editor, open the editing-mode button, choose **Set up live editing**, then **This PC**.
3. If prompted, choose **Get UE4SS**. Download the standard `UE4SS_*.zip` from the [official experimental release page](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/experimental-latest). Do not use `zDEV` or a source-code archive.
4. Choose **Open game folder** and extract the ZIP contents into the `Binaries/Win64` folder the editor shows. For a usual Steam install, that is `<Steam library>/steamapps/common/AbioticFactor/AbioticFactor/Binaries/Win64/`.
5. Keep the ZIP's folder structure. The folder needs `dwmapi.dll`, `ue4ss/UE4SS.dll`, and `ue4ss/Mods/shared/UEHelpers/UEHelpers.lua`. Do not put them inside an extra ZIP-named folder and do not put them in your saves folder.
6. Return to the editor and choose **Check again**. When it detects UE4SS, choose **Set up editor helper** while the game is still closed.
7. Start the game, load a world, and wait for the editor to connect.

If the editor cannot find Abiotic Factor, set the **installed game folder** in **Settings ▸ Game Data ▸ Set game folder**, then try again. This is not your saves folder.

::: warning Existing mods
If you already use UE4SS, keep its settings and other mods. Follow the loader publisher's upgrade guidance instead of overwriting files blindly. The editor recognises the usual nested `ue4ss/` layout and older flat installs, but custom redirected paths are not detected automatically.
:::

The linked UE4SS channel is experimental and game updates can change compatibility. Read its [official installation guide](https://docs.ue4ss.com/dev/installation-guide.html) if your install uses a different layout.


![Choose a local game or dedicated server](/screenshots/40-live-location.png)

*This PC guides local setup. A dedicated server needs details from its owner.*


![Helper setup after UE4SS has been detected](/screenshots/41-live-helper.png)

*UE4SS is already installed separately in this example. This button installs the editor helper, not UE4SS.*

## Updating or reconnecting

Choose **This PC** again. The editor checks its bundled agent files and offers an update when needed. Close the game, run **Set up editor helper**, then restart the game and load your world. Update UE4SS separately through its publisher.

If the `live-agent` folder is missing from a Windows release, extract the complete release again. Source-build help is in the [agent source instructions](https://github.com/ChristopherVR/AbioticEditor/tree/main/live-agent).

## Connecting to a server you run

Set up the helper on the **server machine**, while its game is stopped. On a Windows machine with desktop access, install UE4SS there and use **This PC**. Start the server afterward and keep the helper running under the same Windows account as the game.

Then choose the remote-server option in the desktop editor and enter the helper's host, port, and token. The usual port is `42117`; use the helper's actual port if it differs. Local details are in `%LOCALAPPDATA%\AbioticEditorLiveAgent` as `token.txt` and `port.txt`.

Only the server owner can make the helper reachable through their network and firewall. Treat its token like a password. Do not include it in screenshots or reports. Joining somebody else's multiplayer server does not give the editor control over it.


![Remote connection with an empty token field](/screenshots/42-live-server.png)

*Enter the server helper details. Never share a screenshot containing your token.*

## What you can change

Live controls depend on the selected player, your host authority, and what the game currently has loaded. Hosts can work with other connected players and world objects. A client can change supported parts of their own character.

You can currently use supported controls for vitals, skills, inventories, transmog, recipes, GatePal records, selected background and spawn fields, story flags, clock and weather, loaded containers and ground items, creatures, story NPCs, traders, pets, doors, portals, vehicles, containment, and deployables.

The updated agent adds player magazine-ammo editing and keeps ammo with a weapon when moving
or sorting it. Update the helper while the game is closed before using this addition.
This new field still needs in-game verification. Recipe **Unlock All** and GatePal **Mark All**
now send one grouped request instead of a separate request for every entry.

These limits are deliberate:

- Bench-upgrade installation is disabled after a reported native game crash.
- Traits, world-wide recipe unlocks, pet species, and some missing skill entries cannot be changed live.
- Objects outside loaded sectors cannot appear. Move close to them and refresh, or edit their region save offline.

The [live-editing protocol](/reference/live-editing-protocol) records the technical status of each action. Live editing is still experimental, so a game update can change what works.

## Field fixes

| Problem | What to do |
| --- | --- |
| Game not found | Set it in **Settings ▸ Game Data**, then retry |
| Game must be closed | Exit the game or stop the server, then retry **This PC** |
| UE4SS still missing | Check the displayed Win64 folder for the runtime and shared `UEHelpers` file, with no extra folder level |
| Permission error | Use a Windows account allowed to write to the game folder |
| Agent update needed | Close the game, run helper setup, then restart and load the world |
| No world or object listed | Load into a world, move near the object, then refresh |
| Connection failed | Check that UE4SS loaded the mod and the helper is running, then restart after an update |

### An item exists but is invisible

Old agent versions could leave behind the wrong item details. Update the agent by closing the game and running **This PC** setup again. Restart the game, refresh the inventory or container, and apply the intended item again. For a ground item, remove it and add it again.

If validation fails, check **Settings ▸ Game Data** points at the same game install and reload its data. A failed batch does not partly edit your inventory or container.

### Turning the helper off

Disconnecting stops editing but does not undo changes or uninstall the agent. To disable it, close the game and change `AbioticEditorLiveAgentLua : 0` in `mods.txt`. Keep shared UE4SS files if another mod needs them. Running helper setup again can enable the agent.
