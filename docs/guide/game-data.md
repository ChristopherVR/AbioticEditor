# Game data: names, pictures and 3D models

The editor carries its own list of item names, recipes, skills, story flags and other game information, so you can edit a save even when Abiotic Factor is not installed.

When the game is installed, the desktop app also reads it directly. That gives you:

- names and item pictures that match your game version, in your game's language;
- the game's own models in the [3D view](./3d-view), and the top-down level picture on the Bases map;
- trader stock, pet foods, garden crops, coatings and sandbox options from the game's own tables.

Nothing from the game is in the editor download: all of this is read from your copy, on your computer. The browser editor uses the list packed into its release, and draws boxes in the 3D view.

![Installed game data settings](/screenshots/32-game-data.png)

*Settings ▸ Game data shows which game folder is in use.*

## If the editor cannot find your game

It normally finds Steam and Game Pass installs by itself. If it does not, open **Settings ▸ Game data**, choose **SET GAME FOLDER** and select the Abiotic Factor installation folder (not your saves folder). **USE AUTO-DETECT** goes back to finding it automatically.

Without the game, missing pictures use a plain fallback and the 3D view is not offered. Your saves still open and edit normally.

## After a game update

Update the editor too. A new game patch can add things an older editor does not know yet. The start page will also offer to [get the 3D view ready](./3d-view#get-the-3d-view-ready) again, because the game's files have changed.

## The usmap (matching the game build)

This is an advanced step most players never need. If names or pictures still look incomplete after updating the editor, you can give it a `Mappings.usmap` file that matches your game build.

Make one with [Dumper-7](https://github.com/Encryqed/Dumper-7) or [FModel](https://fmodel.app/), then either:

1. Choose **Settings ▸ Game data ▸ IMPORT DATA FILE**, or
2. Copy it to `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`.

It takes effect straight away. When a later editor update brings a newer file of its own, the newer one is used, so an old import never holds you back.

::: tip Unknown entry?
Do not guess at unfamiliar values from a newer patch. Update the editor first, then review the value before saving.
:::
