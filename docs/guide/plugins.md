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

The **Game Models for 3D View** plugin adds a **3D view** to the **Bases** tab. It draws every
object with the game's own models, read from your installed copy of Abiotic Factor, and can show
the rooms around your base with the ceilings cut away. Nothing from the game is in the download.

1. Download `AbioticEditor-plugin-game-models-3d-v<version>.zip` from the same release as your
   editor ([GitHub releases](https://github.com/ChristopherVR/AbioticEditor/releases)).
2. In the editor, open **Settings ▸ Plugins** and press **OPEN PLUGINS FOLDER**.
3. Extract the zip there, so the folder holds a `GameModels3D` folder, and restart the editor.
4. Open a world save, go to **Bases** and switch from **Map** to **3D view**. Pick a base in
   **Base to show** and the view jumps to it.

The editor must be able to read your game files (the same setting that shows item icons). The
first look at a new area takes a little while as the level is read; after that it is quick.

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
