# Desktop app tour

The desktop app is the full Abiotic Editor for Windows, Linux, Steam Deck and macOS. It finds your saves, opens in its own window, and has every tool, including the 3D view with the game's own models. Your saves stay on your computer.

::: tip Work offline first
Close Abiotic Factor or stop the server before editing a save. Changes wait in the editor until you choose **SAVE**, and every file it writes gets a `.bak` copy of the old version. Choose **REVERT** to throw away changes you have not saved.
:::

## Open a world

Start the app and choose **Open offline editor**. The start page lists the worlds it found under **Your worlds**, tagged STEAM, GAME PASS or SERVER. Choose **OPEN** beside one, use **OPEN FOLDER** to pick one yourself, or drag a folder onto the window. **NEW WORLD** makes a fresh world.

![A world open in the editor](/screenshots/01-loaded.png)

The sidebar sorts the world into story progress, players, regions and server settings. Use its search box when a big world has become a maze.

Choose the account folder above **Worlds** when you can. That also loads your saved character looks, which are shared between your worlds.

## Tune up a player

Open a player from the sidebar. Each tab covers one part of your survivor.

![Player vitals tab](/screenshots/10-player-vitals.png)

| Tab | What you can do |
| --- | --- |
| **General** | See which account the save belongs to, and move it to another Steam account. |
| **Vitals** | Top up hunger, thirst, sanity, fatigue, continence, body health and money. **HEAL ALL** restores every limb. |
| **Character** | Set your background, add or remove traits, and change hair, clothing and other looks. |
| **Transmog** | Change how your armor looks without changing the armor, or hide pieces. |
| **Spawn** | Change your respawn point and teleporter-pad tags. |
| **Inventory** | Edit pockets, equipment, hotbar, backpack and deployed backpack storage. Change an item, stack, durability or weapon coating, and add anything from the searchable item catalogue. |
| **Companions** | Rename and heal carried pets, and see what they eat. |
| **Skills** | Set level or exact XP, or use **MAX** and **MAX ALL**. |
| **Recipes** | Unlock or hide recipes, or **UNLOCK ALL**. The in-game "NEW" highlight stays in step. |
| **Gatepal** | Mark e-mails, notes, compendium entries and fish as read or caught. |
| **Achievements** | View the Steam achievement records on this PC. Read only. |
| **Advanced Data** | Look at values that have no friendly control yet. |

![Player inventory tab](/screenshots/11-player-inventory.png)

Selecting an item shows a **Stats** block with the same numbers the wiki publishes: weapon damage, armor and resistances, food values, and what it repairs or salvages into.

## Edit a world

Open a region such as **WorldSave_Facility.sav**. Most places you build, loot or repair live in a region save. The header shows the world day and time. Which tabs appear depends on what that region holds.

![World containers tab](/screenshots/20-world.png)

| Tab | What you can do |
| --- | --- |
| **Containers** | Restock or clear out storage with the same slot tools as an inventory. |
| **Story Events** | Review story switches by chapter. When a choice needs earlier steps, the editor offers to set them too. Only in the main Facility save. |
| **Doors** | Open or close doors (either way round for a swinging door). |
| **Ground Items** | Edit or remove items lying on the floor. |
| **NPCs** | Revive a story character, change a story stage, or edit a creature. |
| **Pets** | Rename, heal or change tamed pets. |
| **Vehicles** | Make a vehicle drivable, fix a wreck, move it back to its spawn, and fill its cargo. |
| **Bases** | Your bases on a map and in 3D. See [Bases and the 3D view](./3d-view). |
| **Buttons, Elevators, Trams, World Teleporters** and more | Change the saved state of things placed in the level. |
| **Resource Nodes** | Refill harvestables and restore broken glass panes. |
| **Garden plots, Chemistry benches** and more | See [More world tools](./review-features). |
| **Advanced Data** | Look at world values a normal tab does not cover. |

Most lists have a **Show in 3D** button that takes you to the thing you picked. The lists also show a picture of each thing and a small picture of where it stands; click that one to see it large.

![A world list entry with its pictures](/screenshots/56-world-list-pictures.png)

### The story save

**WorldSave_MetaData.sav** holds the story. It has its own tabs:

- **Story** moves the main chapter and edits the world-wide seen and read lists.
- **Traders** shows every trader, what they sell and how to unlock them. **UNLOCK** marks the story events they need as done. With spoiler protection on, traders you have not met stay hidden until you reveal them.
- **Containment** sets which creature each containment cell holds.

![The Traders tab with a trader selected](/screenshots/55-world-traders.png)

### Refill a harvestable or restore a window

Open the region that contains it, then choose **Resource Nodes**. Search for its name, select it, and turn **Harvested** off. For damaged office windows, search for **Glass Pane** in the Office region. Leave **Day Picked Up** alone unless you want to change when it respawns.

### Move loot to another world

From **Containers**, choose **Move items to a different world save**. The full walkthrough is [Transfer items](./transfer-items).

## Adjust a dedicated server

When you open a dedicated-server folder, the sidebar also shows **Config Files**. Select **Admin.ini** or a world's **SandboxSettings.ini** to change difficulty, XP, stacking, spawns and refills. Choose **SAVE INI** when ready. These files get backup copies too. **Add a setting** offers options that are missing from the file, with the game's default value.

![Sandbox settings editor](/screenshots/25-config-ini.png)

## Settings

Open **SETTINGS** in the corner of the window.

| Tab | What is there |
| --- | --- |
| **General** | Theme (Facility Blue, Hazard Orange, GATE Teal), light mode, language, release notes and diagnostics. |
| **Editor** | Spoiler protection: traders, recipes, flags, achievements and codex entries you have not reached stay **CLASSIFIED** until you reveal them. |
| **Game data** | Where your game is installed. See [Game data](./game-data). |
| **Convert** | Move a world between Steam and Game Pass. See [Game Pass saves](./game-pass). |
| **Plugins** | Community add-ons and language packs. See [Plugins](./plugins). |
| **Compare** | Compare two saves. |

![Settings panel](/screenshots/30-settings.png)

### Compare two saves

**Compare** shows what changed between two saves, or between a world and a backup. It skips background noise and calls out real differences such as items, recipes, traits, fish and story flags.

![Compare saves panel](/screenshots/31-compare.png)

## Reference pictures from the wiki

Some screens, such as fish, creatures and story characters, show a picture from the [Abiotic Factor Wiki](https://abioticfactor.wiki.gg). The editor uses the current wiki picture when it can, and its own included copy when you are offline. A missing picture never affects your save.

## Live editing

The desktop app can also connect to a running game on Windows, and on Linux for a game running through Steam Play (Proton). Live edits apply while you play and get no editor backup. Read the [live editing guide](./live-editing) before setting it up.

## Reporting a bug

A clear report gives a broken tool its best chance of being fixed.

1. Open **SETTINGS ▸ General ▸ Diagnostics** and turn on **Diagnostic logging**.
2. Repeat the problem if you can.
3. Choose **OPEN LOG FOLDER** and attach the newest log file.
4. Say what you were doing, whether your save is Steam or Game Pass, and the editor version shown at the top of the window.

Crashes and write failures are logged even when diagnostic logging is off. Report it on [GitHub Issues](https://github.com/ChristopherVR/AbioticEditor/issues/new/choose) or in the [POSTS tab on Nexus Mods](https://www.nexusmods.com/abioticfactor/mods/244?tab=posts).
