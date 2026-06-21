# Keeping game data current

The editor shows real item names, icons, recipes, skills, quest text, fish, traits, and trader data
by reading them straight from your **installed copy of the game**. Two pieces make that work: the
game install it reads from, and a `Mappings.usmap` file that tells it how to interpret the game's
data tables. This page covers keeping both in sync.

::: tip Everything degrades gracefully
If the game isn't installed, or the usmap doesn't match, the editor **still opens and edits saves**.
You just lose the asset-backed niceties (icons, catalog names) until it's sorted. You can't break a
save by having stale game data.
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
