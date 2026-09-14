# Keeping the item catalogue current

The editor carries a field guide of item names, recipes, skills, flags, and other game information. You can edit saves even when Abiotic Factor is not installed. The desktop app can also read your installed game files for the freshest available names and icons; the browser edition uses the catalogue packed into its release.

## If the editor cannot find your game

It normally finds Steam and Game Pass installs by itself. If it does not, open **Settings ▸ Game Data ▸ Set game folder** and select the Abiotic Factor installation folder. The Game Data card shows the folder currently in use.

Missing icons will use a fallback image. They do not stop you editing a save.

## The usmap (matching the game build)

Usually, install the latest editor first. A very new game patch can add things the bundled catalogue does not yet know. If names or icons look incomplete after updating the editor, you can install a matching `Mappings.usmap` file.

This is an advanced compatibility file, not something most players need. Get one that matches your installed game build with [Dumper-7](https://github.com/Encryqed/Dumper-7) or [FModel](https://fmodel.app/), then either:

1. Choose **Settings ▸ Import usmap**, or
2. Copy it to `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`.

Your imported file takes effect immediately and overrides the one that came with the editor. It helps the desktop app read your installed game data. It does not change the bundled browser catalogue or its icons.

::: tip Unknown entry?
Do not guess at unfamiliar values from a newer patch. Update the editor and game data first, then review the value before saving.
:::
