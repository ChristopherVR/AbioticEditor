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

Live setup works in the complete Windows desktop release, and in the Linux desktop release for a game running through Steam Play (Proton) - see [Set up this PC (Linux / Steam Play)](#set-up-this-pc-linux-steam-play) below. The browser edition cannot connect to a live game.

Live editing needs **UE4SS**, a separate open-source mod loader (MIT licensed). The Windows release includes a pinned copy of it and installs it into your game folder with your permission; it never touches an existing UE4SS install.

1. Close Abiotic Factor, or stop the server.
2. In the editor, open the editing-mode button, choose **Set up live editing**, then **This PC**.
3. Pick which copy of the game to use. The editor lists every copy it finds on this PC (each Steam library and any Game Pass install), with the one you used last time already selected, and **Choose another folder** lets you point it at a copy it did not find. The choice is remembered as the game folder in Settings.
4. If UE4SS is not already installed, the editor shows a consent screen naming the bundled UE4SS version and the exact folder it would install into: `Binaries/Win64` for a Steam copy, `Binaries/WinGDK` for a Game Pass copy. Review it, make sure the game is closed, then choose **Install**.
5. The editor installs UE4SS and its own editor helper in one step, then starts the helper.
6. Start the game, load a world, and wait for the editor to connect.

If the editor cannot find Abiotic Factor, use **Choose another folder** on that step (or set the **installed game folder** in **Settings ▸ Game Data ▸ Set game folder**), then try again. This is not your saves folder.

::: tip Game Pass copies
A Game Pass / Microsoft Store copy works the same way; the editor finds it under `XboxGames` on any drive and installs into its `Binaries/WinGDK` folder. The Xbox app keeps that folder locked until you turn on mods for the game: open Abiotic Factor in the Xbox app, use the **...** menu and choose **Enable mods**. If the editor reports the folder is locked, that switch is what it needs.
:::

::: warning Existing mods
If you already use UE4SS, keep its settings and other mods. Follow the loader publisher's upgrade guidance instead of overwriting files blindly. The editor recognises the usual nested `ue4ss/` layout and older flat installs, and never overwrites an install it finds there, but custom redirected paths are not detected automatically.
:::

### If your copy of the editor has no bundled UE4SS

A dev build of the editor, or a build without live-support files, has no UE4SS to install for you. In that case the editor shows the manual steps instead:

1. Choose **Get UE4SS**. Download the standard `UE4SS_*.zip` from the [official experimental release page](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/experimental-latest). Do not use `zDEV` or a source-code archive.
2. Choose **Open game folder** and extract the ZIP contents into the folder the editor shows. For a usual Steam install, that is `<Steam library>/steamapps/common/AbioticFactor/AbioticFactor/Binaries/Win64/`; for a Game Pass install it is `<drive>:/XboxGames/Abiotic Factor/Content/AbioticFactor/Binaries/WinGDK/`.
3. Keep the ZIP's folder structure. The folder needs `dwmapi.dll`, `ue4ss/UE4SS.dll`, and `ue4ss/Mods/shared/UEHelpers/UEHelpers.lua`. Do not put them inside an extra ZIP-named folder and do not put them in your saves folder.
4. Return to the editor and choose **Check again**. When it detects UE4SS, choose **Set up editor helper** while the game is still closed.
5. Start the game, load a world, and wait for the editor to connect.

The linked UE4SS channel is experimental and game updates can change compatibility. Read its [official installation guide](https://docs.ue4ss.com/dev/installation-guide.html) if your install uses a different layout.


![Choose a local game or dedicated server](/screenshots/40-live-location.png)

*This PC guides local setup. A dedicated server needs details from its owner.*


![Helper setup after UE4SS has been detected](/screenshots/41-live-helper.png)

*UE4SS is already installed separately in this example. This button installs the editor helper, not UE4SS.*

## Set up this PC (Linux / Steam Play)

The Linux desktop app can set up live editing for a Steam copy of the game running through Steam Play (Proton), including on the Steam Deck. Abiotic Factor has no native Linux build, so nothing here is different in principle from Windows: the same bundled UE4SS package and the same editor helper are installed into the same `Binaries/Win64` folder inside the game's Steam library. Two things are specific to Proton:

- **The game must have been launched through Steam at least once already.** Steam only creates the per-game Proton profile (called a "prefix") the first time you actually run the game, and the editor's helper needs that profile to find the same `%LOCALAPPDATA%` the in-game mod uses. If setup reports it cannot find your Steam Play profile, start Abiotic Factor once from Steam, let it reach the main menu, quit, then retry **This PC**.
- **UE4SS itself may need a Steam launch option to load under Proton.** UE4SS installs the same way it does on Windows - a `dwmapi.dll` next to the game's executable that the game loads automatically - but Wine ships its own built-in `dwmapi.dll` for desktop-compositing calls, and it can take priority over the one UE4SS drops in the game folder unless Proton is told to prefer the game folder's copy. If UE4SS does not appear to load (no `ue4ss/UE4SS.log` appears next to the game after you play), set this game's Steam launch option (right-click Abiotic Factor in your Steam library → **Properties** → **General** → **Launch Options**) to:
  ```
  WINEDLLOVERRIDES="dwmapi=n,b" %command%
  ```
  This is the standard override the wider UE4SS/Proton community uses for other games with the same DLL-proxy install; it is not something this editor's bundled UE4SS package or its pinned build documents itself (`live-agent/ue4ss/runtime.json` records only the version/checksum, no install notes), so treat it as community guidance to try, not a guarantee.
- **Wine is needed to run the editor's own small helper program.** UE4SS and the Lua mod install and run exactly like on Windows (the game itself is a Windows program either way), but the tiny separate helper the editor also starts has no Linux build yet, so it runs through Wine, pointed at the same Proton prefix as the game. Most Linux setups that can already run the game through Steam Play also have Wine, or can install it from their distribution's package manager (for example `sudo apt install wine` on Debian/Ubuntu, `sudo pacman -S wine` on Arch). If you use a non-standard Wine build (a custom Proton-GE build, a Bottle, and so on) and want the helper to use that one specifically instead of a system `wine`, set the `ABIOTIC_LIVE_WINE` environment variable to its path before starting the editor.

Everything else follows the same steps as [Set up this PC (Windows)](#set-up-this-pc-windows) above: close the game, choose **This PC**, pick the detected copy, and let the editor install UE4SS and its own helper.

::: warning Not yet confirmed on a real Steam Play session
The Proton prefix lookup, the Wine-launched helper, and the shared `%LOCALAPPDATA%` mapping described above have been built and reviewed but not yet exercised against an actual running game under Proton. If **This PC** setup gets stuck or the game never connects after UE4SS and the helper both report ready, please file an issue with the editor's diagnostics log attached - it is the fastest way to find what is different about a real Proton install. `docs/PROGRESS.md`'s Linux live-editing round has the implementation notes.
:::

macOS is not supported for live editing (there is no bundled UE4SS or helper for it, and no Proton-equivalent path to run the Windows ones). Offline save editing still works there.

## Updating or reconnecting

Choose **This PC** again. The editor checks its bundled agent files and offers an update when needed. Close the game, run **Set up editor helper**, then restart the game and load your world.

UE4SS updates ship together with editor releases, and the editor never replaces an install it finds already there. To move to a newer bundled UE4SS build, close the game, delete the `ue4ss` folder and `dwmapi.dll` from `Binaries/Win64` (`Binaries/WinGDK` on Game Pass), then choose **This PC** again to install the newer bundled build. If you manage UE4SS yourself instead, keep updating it separately through its publisher.

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

The editor can now also change traits, character appearance, and bench upgrades while
connected, and moving or editing an item keeps every extra detail it carries (things like pet
progress, weapon coatings, and custom variants) instead of losing them on the way. Weapon
coatings themselves can now be changed live too, the same coating picker offline saves use. You
can also move an item directly between your own inventory and a nearby container, water and
fertilise garden plots, charge Power Chairs, adjust the world's total play time, and edit a
carried pet's stored mutation progress in the Companions tab (negative values are rejected; the
mutation target itself stays read-only). The host can also repaint a
placed object from the Bases tab's new **Painted objects** list. These are new and still being
checked against a running game, so keep an eye on them and update the agent if something looks
off.

These limits are deliberate:

- Pet species and some missing skill entries cannot be changed live. World-wide recipe unlocks
  need a UE4SS build that exposes `TSet.Add`/`Remove`/`ForEach`; on an older build the world
  recipes list shows why the unlock control is disabled instead of just hiding it.
- Objects outside loaded sectors cannot appear. Move close to them and refresh, or edit their region save offline.

The [live-editing protocol](/reference/live-editing-protocol) records the technical status of each action. Live editing is still experimental, so a game update can change what works.

## Field fixes

| Problem | What to do |
| --- | --- |
| Game not found | Use **Choose another folder** on the copy-of-the-game step, or set it in **Settings ▸ Game Data**, then retry |
| Game must be closed | Exit the game or stop the server, then retry **This PC** |
| UE4SS still missing | Check the displayed folder (`Win64`, or `WinGDK` on Game Pass) for the runtime and shared `UEHelpers` file, with no extra folder level |
| Permission error | Use a Windows account allowed to write to the game folder |
| Game Pass folder locked | In the Xbox app, open the game's **...** menu and choose **Enable mods**, then retry |
| Agent update needed | Close the game, run helper setup, then restart and load the world |
| No world or object listed | Load into a world, move near the object, then refresh |
| Connection failed | Check that UE4SS loaded the mod and the helper is running, then restart after an update |

### An item exists but is invisible

Old agent versions could leave behind the wrong item details. Update the agent by closing the game and running **This PC** setup again. Restart the game, refresh the inventory or container, and apply the intended item again. For a ground item, remove it and add it again.

If validation fails, check **Settings ▸ Game Data** points at the same game install and reload its data. A failed batch does not partly edit your inventory or container.

### Turning the helper off

Disconnecting stops editing but does not undo changes or uninstall the agent. To disable it, close the game and change `AbioticEditorLiveAgentLua : 0` in `mods.txt`. Keep shared UE4SS files if another mod needs them. Running helper setup again can enable the agent.
