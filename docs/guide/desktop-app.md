# Desktop app

The desktop editor is a .NET MAUI app that runs unpackaged on Windows (and on macOS via
Mac Catalyst). It's a thin front-end over the shared Core engine, so every edit it writes
is byte-identical to what the CLI produces.

## Workflow

1. Start the app and click **OPEN FOLDER**, then point it at a save directory:
   - **Client saves:** `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`
   - **Dedicated server:** the folder containing `Worlds\<WorldName>`. The app also finds
     `Admin.ini` and each world's `SandboxSettings.ini` and lists them under **CONFIG FILES**
     in the sidebar.
2. Pick a file from the sidebar:
   - **Player saves** open the player editor — vitals, inventory, equipment, skills, traits,
     recipes, codex, transmog, spawn point, achievements, and SteamID change.
   - **World saves** open the world editor — containers, quest flags, doors, dropped items,
     NPCs, bases, and story progression.
   - **The metadata save** edits story state.
3. Edits are **staged until you press SAVE**. Every write keeps a `.bak` of the previous
   file next to it.
4. **Quest flags are gated by story prerequisites** — the editor offers to set missing
   prerequisite flags rather than letting you create an inconsistent story state.
5. **JSON export/import** gives you the full save as editable text for anything the UI does
   not cover.

Diagnostic logging is opt-in (toggle in the status bar). When enabled it traces every
staged change and records any save content this build does not recognize.

## Game data and icons

Item, recipe, skill, flag, fish, and trait catalogs (and item icons) come from the
installed game's pak archives, read through a bundled type-mappings file. **Everything
degrades gracefully when the game isn't present** — catalogs come back empty and icons
no-op, but the editor still opens and edits saves.

## Keeping the game build in sync (usmap)

Reading the game's data tables needs a `Mappings.usmap` matching the installed game build.
A validated one is bundled; when the game updates, dump a fresh usmap (with
[Dumper-7](https://github.com/Encryqed/Dumper-7) or [FModel](https://fmodel.app/)) and
install it via the status bar **IMPORT USMAP** button, or copy it to
`%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`. The user-installed file always wins
over the bundled one. Without a matching usmap the editor still opens and edits saves; only
asset-backed features degrade.

## Updates

The app checks GitHub Releases from its **Settings ▸ Updates** card. When a newer build is
available it downloads the matching asset and replaces the running install in place — no
installer, no admin prompt.
