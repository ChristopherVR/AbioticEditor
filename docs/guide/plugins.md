# Plugins and language packs

Plugins are community-made add-ons for Abiotic Editor. They can add a one-click save tool, a repair for a game update, a small editor panel, or another language. Think of them as approved lab equipment: useful when you know its source.

::: warning Only install plugins you trust
A plugin has the same access to your computer as the editor. The editor cannot safely contain a malicious add-on. Get plugins from authors you trust, keep a backup of anything important, and read what a plugin says it will do. Plugin saves still create a `.bak` backup.
:::


![Plugin management panel](/screenshots/33-plugins.png)

*Settings > Plugins lists installed add-ons. This example has none installed.*

## Install an add-on

1. Download and unzip the plugin folder. It must contain `plugin.json` alongside its files.
2. Move that whole folder to `%LOCALAPPDATA%\AbioticEditor\plugins\<plugin-folder>\`.
3. Restart Abiotic Editor.

Plugins kept there survive editor updates.

## See your bases in 3D

The **Bases** tab has a **3D view** that draws every object with the game's own models, read from
your installed copy of Abiotic Factor, and can show the rooms around your base with the ceilings
cut away. It comes with the editor: there is nothing extra to download or install, and nothing from
the game is in the download.

Open a world save, go to **Bases** and switch from **Map** to **3D view**. Pick a base in **Base to
show** and the view jumps to it.

The editor must be able to read your game files (the same setting that shows item icons). The
editor starts reading an area's models as soon as you open its save, so by the time you open the 3D
view they are usually ready; after the first time they are kept on disk and it is quick.

Opening a save also prepares the level around your bases and the rest of the area in the
background, so **Show in 3D** on a door or button elsewhere is quick too. The level appears in parts
as it arrives.

The Doors, Buttons, Breakable Objects, Elevators, Trams, World Teleporters, Resource Nodes, Power
Sockets and Containers lists show a picture of each kind of thing, drawn from the game's own models
and included with the editor, with its current state on it and a small picture of where it stands.
NPC spawn points show the creature they spawn.

On the **Map**, the bases sit on a picture of the level seen from straight above, drawn from your game
files. Pick a base and the map zooms to it.

If you installed the separate Game Models plugin with an earlier version, you can delete its
`GameModels3D` folder from **Settings ▸ Plugins ▸ OPEN PLUGINS FOLDER**; the editor uses the newer
copy it comes with either way.

The view fills the window below the Map / 3D switch and the base picker, so nothing needs scrolling.
The level around what you are looking at is drawn by default and loads in the background, a part at
a time, following the view as you move. Its buttons sit on top of it (frame everything, frame the selection,
walk, labels, doors, characters and the surrounding level), and a box in the corner shows what is
still loading: models, their textures and the level. A model appears once its textures are in, so
nothing shows up blank first. The panel beside the view shows what you clicked at the top, with
**Objects** (search and the full list), **Filters** and **Display** (game models and the level) as
tabs underneath.

Scroll to zoom towards whatever is under the pointer, and double-click a spot to swing the view
round it and move in close. **Full screen** gives the view the whole window; press Escape or the
button again to leave. Whatever the pointer is over gets a thin blue outline, so you can see what a
click will pick. With **Edit mode** ticked on the view, press and drag any piece you built to
move it across the floor; a selected group moves together. It moves in 10 cm steps (hold Shift to move
freely, Alt to raise or lower it). R turns the selection 45 degrees (Shift+R 15), Delete removes it and
Ctrl+D copies it beside itself. Furniture and walls that
belong to the level itself show a card saying so: the game rebuilds those from its own files, so
they cannot be moved. Leaving the 3D view and coming back keeps it exactly as you left it.
Triangles mark items lying on the ground and squares mark buttons, breakable walls, resource nodes,
spawn points and the like; click one to change its settings right there. Clicking a wall socket's plug
opens it, so you can plug a device in.

Round markers show the area's doors, coloured by state: green is open, grey is closed, blue is a
closed security door and orange is destroyed or smashed. Click one to open or close it (either
way round for a swinging door); the change waits for **SAVE** like any other edit. Press **Walk** to look around at eye
height: drag to look, use W A S D or the arrow keys to move, Shift to go faster, and Escape to stop.
You bump into walls while walking; turn off **Stay on the floor** to fly through them. E and Q take
you up and down: let go and you land on the floor below, so you can reach another storey.
Diamonds mark story characters, traders and pets; click one to see who it is: their portrait, their
story and, for a trader, what they take and sell. Turn on **Edit mode** on the view, then **Add object** puts a fresh copy of
something you have already built where the view is looking. **Also list things built in my other
worlds** adds everything you have built in your other worlds on this computer, so you can place a
kind this world has never had.

The panel beside the view always shows what you clicked at the top: its picture and name, its
contents (change them right there), then **Remove** and **Copy**. Pressing either turns **Edit mode**
on. The numbers behind it are folded under **Details**. The object list, filters and display settings
are tabs underneath. With **Edit mode** on, **Add object** on the view places a new piece. Things
that came with the level (a fridge, a locker) can't be removed, because the game puts them back from
its own files. **Copy as my own piece** places one of your own beside it instead, made from one you
built in any of your worlds. Wall sockets built into the level show as
markers too: click one to plug a device into it.

**Show in 3D** also works from the story save: a trader's card offers one for each place they stand,
and each containment cell has one. It opens the right area's save and takes you there; holograms
have one as well. The view stays covered until the place has loaded, so it never shows half-drawn.

Most other tabs have a **Show in 3D** button next to what you pick: a door, a container, a chemistry
bench, an item on the ground, a vehicle, a story character, a pet, a button, a breakable wall, a
resource node and the other lists of world objects. It opens the 3D view and takes you there. A
piece you built is selected; a door or a character has its marker picked; anything else gets an
orange pin that shows through walls, with the level around it switched on.

A new object can be wired up straight away, before you save: click it and use its **POWER** card,
or pick its outlet from another device's card. To run a cable the way the game does, tick **Route
the cable through cable reroutes** before you press **PLUG**. The editor places cable reroutes along
the way (every 4 m, or on the points you add where the view is looking) and plugs them one into the
next.

Pieces you move, copy or place are checked against the level: the view warns when one cuts into a
wall or floor, overlaps another piece, or has nothing holding it up, and **Stand it on the floor**
drops it onto the floor below. The browser version of the editor has the 3D view too, with every
piece drawn as a box (the game's models need the desktop app and this plugin).

## Use and manage plugins

Open **Settings ▸ Plugins ▸ Manage Plugins**. You can see each plugin's name, author, source, and whether it loaded. From there you can enable or disable it, run its save operation against the open save, or open a panel it provides. Plugin menu actions also appear in the top-level **Plugins** menu.

Use **disable** if an add-on causes trouble, then restart the editor. A disabled plugin remains listed but does not run.

## Change the editor language

Language packs install exactly like other plugins. Put the whole pack folder in the plugins folder, restart the editor, then select it in **Settings ▸ LANGUAGE**. A pack can translate all or part of the editor. Missing text stays in English.

## Command-line tools

Most players do not need this section. If you use the command line, these commands let you inspect or safely preview plugin operations:

```console
abioticeditor plugins list
abioticeditor plugins info <id>
abioticeditor plugins run <operation> <save> --dry-run
abioticeditor plugins run <operation> <save>
```

`--dry-run` previews an operation without saving. A real operation creates a `.bak` backup first.

Want to make a plugin or translation? The technical [plugin guides](/reference/plugin-system) and [localization reference](/reference/localization) are the right starting point.
