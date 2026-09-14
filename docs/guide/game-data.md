# Keeping game data current

The shared editor loads a **bundled game-data registry** for item names and other catalog metadata.
It works without an installed copy of the game. The desktop can also read installed game assets;
its item icons are extracted lazily from the paks. The browser uses pre-extracted, bundled icons.

A `Mappings.usmap` tells the desktop asset reader how to interpret the game's data tables. Updating
it helps with installed-game extraction; it does not regenerate the browser's bundled registry or
icons. Those are shipped with editor releases and the Pages build.

::: tip Missing assets do not prevent save editing
Bundled metadata remains available when the game is absent. Missing images use a fallback. A newer
game can introduce entries the bundled data does not yet know; use an updated editor and matching
mappings, and review unknown values before editing them.
:::

## The game install

The editor auto-detects your Abiotic Factor install (Steam or Game Pass). If it can't find yours, set
it under **Settings ▸ Game Data ▸ Set game folder**. The Game Data card always shows the install path
currently in use.

## The usmap (matching the game build)

`Mappings.usmap` is a type-mappings file the editor needs to read the game's data tables for a given
game version. A validated one is **bundled**, so this normally just works out of the box.

When the game gets an update, the catalogs can start to look stale or incomplete. To refresh them,
install a usmap that matches the new build:

1. Dump a fresh usmap for the installed game version with
   [Dumper-7](https://github.com/Encryqed/Dumper-7) or [FModel](https://fmodel.app/).
2. Import it from **Settings ▸ Import usmap**, or copy it to
   `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`.

A user-installed usmap always wins over the bundled one, so your imported file takes effect
immediately.

::: tip For maintainers
Regenerating the **bundled** usmap and the data registry that ships with the editor is a separate,
maintainer-only task. See [Maintainer commands](/reference/maintainer-commands).
:::
