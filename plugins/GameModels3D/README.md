# Game Models for 3D View - optional plugin

Draws the editor's **3D base view** with the game's own models and textures instead of coloured
boxes, and can show the level around your base (floors, walls, rocks, pipes) so you can see where
things stand before you move them. Everything is read from **your installed copy of Abiotic
Factor**; nothing from the game is shipped with the plugin or the editor.

- **Runtime:** .NET (`GameModels3D.dll`)
- **Capability:** 3D model provider (`sceneModels`)
- **Surfaced in:** the desktop editor's **Bases** tab, as a Map / 3D view switch (the CLI and the
  browser build ignore it)
- **Not bundled:** without it there is no 3D view at all and the Bases tab shows its map as before;
  install it only if you want it.

## Install

**From a release:** download `AbioticEditor-plugin-game-models-3d-v<version>.zip` from the same
release as your editor, open **Settings, Plugins, OPEN PLUGINS FOLDER** in the editor, extract the
zip there (it holds one `GameModels3D` folder) and restart the editor.

**From source** (needs the .NET 10 SDK and a clone with submodules):

1. Build it:
   ```console
   dotnet build plugins/GameModels3D -c Release
   ```
2. Make a folder `%LOCALAPPDATA%\AbioticEditor\plugins\GameModels3D` (on Linux,
   `~/.local/share/AbioticEditor/plugins/GameModels3D`) and copy these two files into it from
   `plugins/GameModels3D/bin/Release/net10.0/`:
   - `GameModels3D.dll`
   - `plugin.json`
3. Start the editor. **Settings, Plugins** lists "Game Models for 3D View".

Then open a world save, go to the **Bases** tab and switch from **Map** to **3D view**.

The editor must be able to read your game files (the same setting that gives you item icons).

## Using it

The 3D view shows every object in the region with its game model. Turn on **Experimental: move
objects** to move, turn, delete and copy player-built pieces; nothing is written until SAVE, and a
`.bak` is kept. The **Game models** card in the side panel has:

- **Show game models**: on by default once the plugin is installed. Objects the game has no
  model for stay as boxes.
- **Show the level around the view**: loads the level around the centre of the view. Pick a
  **distance**, and **cut away above the floor** to hide ceilings and upper floors so you can see
  into rooms (measured from the floor of the base you are looking at). Move the view and press **Load level here** to look somewhere else. Clicking a level
  piece names it (and the level file it comes from); level pieces cannot be selected or edited.

Everything else works as without the plugin: click to select, the inspector, the move/rotate
gizmo, delete, duplicate and group edits, and SAVE. Selection outlines fit the real models.

## How it works

- **Placed objects.** For each object's class, the plugin rebuilds the game's own construction
  script: every parent class's components, with each class's overrides applied, following the
  parent chain rather than a list of names, so objects added in future game updates work too.
  Attached parts (lids, taps, plug sockets) sit where the game puts them.
- **Materials.** Each material is reduced to its base colour texture and tint, plus glass,
  cut-out and glowing flags. It is a preview, not the game's lighting, so see-through glows
  (fake light beams under lamps, glow cards) are left out.
- **Level.** The plugin reads the level's static meshes. Streamed areas are placed where the game
  places them, and only drawn where the game's own streaming volumes would load them, so dream
  sequences and other set pieces that share the same space stay hidden. Sky domes, far-distance
  stand-ins, landscape terrain and bendable pipes and cables are left out.

## Cache

The first time you open a region, reading its level files takes a few seconds per file (the view
shows progress). Models, textures and level indexes are then cached in
`%LOCALAPPDATA%\AbioticEditor\plugin-data\com.abioticeditor.game-models-3d\cache`, in a folder
tied to your game version, so they load instantly afterwards and a game update starts fresh. You
can delete that folder at any time.

## Removing it

Delete the `plugins\GameModels3D` folder (and the cache folder above if you like). The 3D view
goes back to boxes.

## For plugin authors

The editor side is a small, generic capability: `ISceneModelProvider` in the plugin SDK
(`AbioticEditor.Plugins.Scene`), registered with `registry.AddSceneModelProvider(...)`. A provider
describes classes and level slices in the 3D view's space and serves meshes in the documented
`SceneMeshFormat` layout plus PNG textures. This plugin is one implementation; another could
supply hand-made or simplified models the same way.
