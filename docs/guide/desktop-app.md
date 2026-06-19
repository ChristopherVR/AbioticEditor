# Desktop app

The desktop editor is a .NET MAUI app that runs unpackaged on Windows (and on macOS via
Mac Catalyst). It's a thin front-end over the shared Core engine, so it writes the same
output as the CLI. This page is a visual tour of everything it can do.

## 1. Open a save folder

Start the app. The welcome screen lists any client and dedicated-server save folders it
finds on your machine; click **LOAD** on one, or use **OPEN FOLDER** (or drag a folder onto
the window) to pick your own.

- **Client saves:** `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`
- **Dedicated server:** the folder containing `Worlds\<WorldName>`.

![The editor with a save folder loaded](/screenshots/01-loaded.png)

Once a folder is loaded, the sidebar groups everything it found: the **world story /
metadata** save, **players**, **world regions**, and any server **config files**. Use the
search box to filter, or the **+** on the Players group to add a new player save. Every row
has a right-click **Open in Explorer / Finder** action.

::: tip Edits stage until you SAVE
Nothing is written to disk until you press **SAVE**. Every write keeps a `.bak` copy of the
previous file next to it, so you can always roll back. **REVERT** discards staged edits.
:::

## 2. Edit a player

Click a player save in the sidebar to open the **player editor**. It opens on **Vitals**:
hunger, thirst, sanity, fatigue, continence, and per-body-part health as sliders you can drag
or type into, plus your current money. **HEAL ALL** tops up body health in one click.

![Player vitals tab](/screenshots/10-player-vitals.png)

### Inventory

The **Inventory** tab shows every slot (backpack, pockets, equipment, hotbar, and a deployed
backpack's contents) with the real in-game icons. Select a slot to edit the item, quantity,
or durability in the right-hand **Slot Editor**; drag one slot onto another to swap. The
searchable **Item Catalogue** on the right lets you drop in any item in the game, filtered by
category.

![Player inventory tab](/screenshots/11-player-inventory.png)

### Skills

The **Skills** tab lists all fifteen skills with their level, XP, and milestone perks. Nudge a
level, type an exact XP value, **MAX** one skill, or **MAX ALL**. Tap a milestone for its full
detail in the slot panel.

![Player skills tab](/screenshots/12-player-skills.png)

### Recipes

The **Recipes** tab is your full crafting book: search by item name or recipe id, filter by
category, tick recipes on or off individually, or **UNLOCK ALL**. The counter shows how many
of the total you've unlocked.

![Player recipes tab](/screenshots/13-player-recipes.png)

### Character & traits

The **Character** tab sets your background (job id), and lets you add or remove **traits**
(each shown with its point cost and in-game description) and edit your **appearance**
(head / hair / clothing, stored in `ScientistCustomization_*.sav`, which applies to every
world this account plays).

![Player character and traits tab](/screenshots/14-player-character.png)

### GatePal journal

The **GatePal** tab is the in-game journal device: **e-mail**, **notes**, the **compendium**,
and the **fish** journal. Mark entries read or unread, search the full text, and (for fish)
see the unlock and catch requirements: bait, location, time of day, and story gate. Each fish
also shows a reference picture from the Abiotic Factor Wiki (see
[Reference pictures from the wiki](#reference-pictures-from-the-wiki) below).

![Player journal / GatePal tab](/screenshots/15-player-gatepal.png)

### Transmog, spawn & achievements

The remaining tabs cover **Transmog** (armour appearance overrides, slot by slot)…

![Player transmog tab](/screenshots/16-player-transmog.png)

…**Spawn** (your respawn point and teleporter-pad tags), **Achievements**, **General**
(SteamID, raw identity), and **Data** (a raw view for anything else).

::: tip Change a save's Steam account
The **General** tab can re-home a player save to a new SteamID64. The id lives in both the
file name and the save's `SaveIdentifier`, and bed claims in the world saves are updated to
match, all in one step, with a `.bak` kept.
:::

## 3. Edit the world

Click a world region (`WorldSave_<Region>.sav`) to open the **world editor**. The header shows
the **world day** and **time of day**, and how many containers the region holds. It opens on
**Containers**: every storage object in the region, with the same item-editing and catalogue
tools as a player's inventory.

![World containers tab](/screenshots/20-world.png)

### Quest flags

**Quest flags** are the one-way switches the game flips as the story advances. The editor
shows only the flags this save has actually reached, grouped by story chapter, with a
plain-language explainer of how the quest chain works. Set or clear any flag, and when a flag
has prerequisites, the editor offers to set them too, so you never create an impossible story
state.

![World quest flags tab](/screenshots/21-world-questflags.png)

### NPCs, doors & more

- **NPCs** shows story characters and your tamed pets. Revive a dead NPC, rename a pet, or
  change a story character's narrative state.
- **Doors** toggles lock and open state; **Dropped** edits items lying on the ground;
  **Bases** covers player-built structures; **Raw JSON** exposes anything the UI doesn't model.

![World NPCs tab](/screenshots/22-world-npcs.png)

::: tip Editable world-state maps
Beyond the tabs above, the world save holds per-actor state maps: elevators, buttons,
resource nodes, power sockets, teleporter pads, vehicles, and more. These are editable too
(also from the CLI's `world` command and the in-app **Edit World Maps** screen): un-harvest a
node to refill it, re-tag a teleporter pad, and so on.
:::

## 4. Server config files

If you opened a dedicated-server folder, the sidebar's **Config Files** group lists `Admin.ini`
and each world's `SandboxSettings.ini`. Selecting one opens a key/value editor: change
difficulty, XP and stack multipliers, spawn rates, refill rates, and the rest, then **SAVE
INI** (a `.bak` is kept here too).

![Sandbox settings INI editor](/screenshots/25-config-ini.png)

## 5. Settings & spoiler protection

The **Settings** sheet (bottom-right) covers:

- **Theme**: Facility Blue or Hazard Orange, plus a light-mode toggle. Switching rebuilds the
  UI live without losing your loaded save or staged edits.
- **Diagnostics**: opt-in logging that traces every staged change and records any save
  content this build doesn't recognise.
- **Spoiler protection**: seals content you haven't reached yet (future quest flags, traders,
  recipes, hidden achievements, codex entries) behind a `CLASSIFIED` stamp. Tap a sealed item
  to reveal it; revealed items stay visible. Re-seal them all at any time.
- **Game data**: **Import usmap** for a newer game version (see below).
- **Plugins**: enable/disable installed plugins and open the **Manage Plugins** panel.

![Settings panel](/screenshots/30-settings.png)

## 6. Compare two saves

The **Compare** sheet diffs two saves, or two folders of saves (a world vs one of its
backups). It lists every property-level difference, folding out noise (timestamps, instance
ids, positions) so the meaningful changes stand out. For two player or two world saves it also
builds a friendly, domain-aware summary: which recipes, fish, traits, items, or quest flags
differ between them.

![Compare saves panel](/screenshots/31-compare.png)

## Game data and icons

Item, recipe, skill, flag, fish, and trait catalogs (and item icons) come from the installed
game's pak archives, read through a bundled type-mappings file. **When the game isn't
installed, these catalogs come back empty and icons are skipped**, but the editor still opens
and edits saves.

## Reference pictures from the wiki

A few editor surfaces show a representative picture of the thing you're editing, pulled from
the **[Abiotic Factor Wiki](https://abioticfactor.wiki.gg)**:

- the **fish journal** (each species' icon),
- the **vehicles** view (how each vehicle looks),
- and the world **features** and **doors** views (teleporter pads, the tram, power sockets).

<div align="center">

| Antefish | Gem Crab | Gutfish Eel | Forklift | Tram | Teleporter Pad |
|:--:|:--:|:--:|:--:|:--:|:--:|
| ![Antefish](/wiki/Itemicon_antefish.png) | ![Gem Crab](/wiki/Item_Icon_-_Gem_Crab.png) | ![Gutfish Eel](/wiki/Itemicon_eel.png) | ![Forklift](/wiki/Vehicle_-_Forklift.png) | ![Tram](/wiki/Vehicle_-_Tram.png) | ![Teleporter Pad](/wiki/Itemicon_craftedteleporter_lodestone.png) |

<sub>Images: abioticfactor.wiki.gg, licensed CC BY-NC-SA 4.0</sub>

</div>

The editor always tries the live wiki first, so the artwork stays current as the wiki is
updated. **When the wiki is unreachable, it falls back to a bundled copy** shipped next to the
editor (a `wiki\` folder), so the pictures still appear offline. If neither has an image, the
surface simply shows no picture; nothing else is affected. Each image carries the
`Image: abioticfactor.wiki.gg` credit (the content is CC BY-NC-SA).

::: tip For maintainers
The bundled set lives in `assets/wiki/` and is regenerated with the CLI's
[`download-wiki-images`](/guide/cli#maintainer-commands) command. See `assets/wiki/README.md`.
:::

## Keeping the game build in sync (usmap)

Reading the game's data tables needs a `Mappings.usmap` matching the installed game build.
A validated one is bundled; when the game updates, dump a fresh usmap (with
[Dumper-7](https://github.com/Encryqed/Dumper-7) or [FModel](https://fmodel.app/)) and
install it via **Settings ▸ Import usmap**, or copy it to
`%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`. The user-installed file always wins
over the bundled one. Without a matching usmap the editor still opens and edits saves; only
asset-backed features degrade.

## Updates

The app checks GitHub Releases from its **Settings ▸ Updates** card. When a newer build is
available it downloads the matching asset and replaces the running install in place, with no
installer and no admin prompt.
