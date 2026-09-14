<div align="center">

<img src="docs/public/logo.png" alt="Abiotic Editor" width="100" />

# Abiotic Editor

**A save editor and live-editing companion for Abiotic Factor.**

[Open browser editor](https://christophervr.github.io/AbioticEditor/app/) ·
[Download desktop app](https://github.com/ChristopherVR/AbioticEditor/releases/latest) ·
[Documentation](https://christophervr.github.io/AbioticEditor/guide/) ·
[Getting started](https://christophervr.github.io/AbioticEditor/guide/getting-started)

</div>

![Player inventory and item catalog](docs/public/screenshots/11-player-inventory.png)

Edit your character, repair a world, move supplies, or inspect what the game recorded.
The browser editor, Windows/Linux desktop app, and CLI share the same save engine.
Save files stay on your computer. This is a fan-made tool, not part of the game.

## Choose an edition

| Edition | Best for | Details |
| --- | --- | --- |
| [Browser](https://christophervr.github.io/AbioticEditor/app/) | Editing a save without installing anything | Choose a folder or zip. Direct folder saves where supported; otherwise SAVE and EXPORT a zip. |
| [Desktop](https://github.com/ChristopherVR/AbioticEditor/releases/latest) | Local worlds and additional tools | Windows and Linux, including Steam Deck. Discovery, Game Pass, compare, transfers, plugins, and live connections. |
| CLI | Scripts and server administration | Separate Windows, Linux, and macOS downloads. Run `abioticeditor --help`. |

The browser includes bundled game metadata and icons. The desktop can also extract item icons
from an installed copy of the game. Missing assets do not stop save editing.

## Make your first edit

1. Close the game or stop the server and keep a separate copy of your world folder.
2. Open the editor and select your save folder. The desktop can discover worlds automatically.
3. Select a player or world save, make your changes, and press **SAVE**.
4. If the browser cannot write to the folder, or you imported a zip, press **EXPORT** after SAVE.
   Copy the exported files back to the game's save folder before playing.

Direct saves keep a `.bak` of the previous file. This is a rolling backup, not an unlimited
history. Quest tools offer prerequisite planning to help keep story changes consistent, but
save editing cannot guarantee every possible combination will behave correctly in the game.

On Windows, Steam saves are under
`%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`.
Open the account folder if you also want character appearance, which is stored beside `Worlds/`.
See [Getting started](https://christophervr.github.io/AbioticEditor/guide/getting-started)
for download names, installation, and save locations on other platforms.

## What you can edit

- **Player:** vitals, money, inventory and equipment, skills, recipes, background and traits,
  GatePal entries, transmog, appearance, and spawn information. Steam achievement status is read-only.
- **World:** containers, story and quest flags, doors, dropped items, NPCs and pets, bases,
  vehicles, resource nodes, and other saved world features.
- **Tools:** compare saves, transfer items between worlds, change a player's SteamID,
  edit server INI settings, and inspect raw save data.
- **Game Pass:** discover, repair, extract, and convert Xbox containers to or from Steam layouts.
- **Plugins:** add save operations, CLI commands, UI tools, and other extensions in .NET or JavaScript.

The [desktop tour](https://christophervr.github.io/AbioticEditor/guide/desktop-app) explains the
screens. For example, to restore broken office glass, open its region's **Resource Nodes** tab,
find **Glass Pane**, turn **Harvested** off, and save while the game is closed.

## Live editing

Live editing is **experimental and has fewer tools than offline editing**. On Windows, choose
**Live editing > This PC** and let the app set it up. It downloads missing UE4SS support,
installs its bundled agent, and starts the helper. No separate installer or manual mod copying
is needed with a complete Windows release. Close the game for setup; the first download needs
internet access. Existing mod installations are preserved. Remote connections need setup on
the server machine too.

Live changes affect the game immediately. Vitals and skills apply automatically after a short
pause in input; other controls send their own actions. There is no editor `.bak` or universal
undo step, and the game can persist changes in its normal saves. Live coverage differs from file
editing, and bench-upgrade installation is disabled.

Read the [live-editing guide](https://christophervr.github.io/AbioticEditor/guide/live-editing)
for setup, supported areas, and current limitations. The browser edition cannot connect live;
automatic local agent setup is Windows-only.

## Find the right guide

| Task | Documentation |
| --- | --- |
| Browser folders, permissions, and exports | [Browser guide](https://christophervr.github.io/AbioticEditor/guide/browser-editor) |
| Linux or Steam Deck setup | [Linux desktop guide](https://christophervr.github.io/AbioticEditor/guide/linux-local-host) |
| Move items between worlds | [Item transfers](https://christophervr.github.io/AbioticEditor/guide/transfer-items) |
| Xbox cloud sync, repair, and conversion | [Game Pass guide](https://christophervr.github.io/AbioticEditor/guide/game-pass) |
| Steam account and achievement information | [Steam guide](https://christophervr.github.io/AbioticEditor/guide/steam-achievements) |
| Missing or outdated game data | [Game-data guide](https://christophervr.github.io/AbioticEditor/guide/game-data) |
| Install community tools | [Plugins and language packs](https://christophervr.github.io/AbioticEditor/guide/plugins) |
| Write a plugin | [Authoring guide](https://christophervr.github.io/AbioticEditor/reference/plugin-authoring), [samples](plugins/) |
| Script edits or manage servers | [CLI reference](https://christophervr.github.io/AbioticEditor/guide/cli) |

For Game Pass edits, follow the guide's offline routine: close the game and Xbox app, go offline,
edit, then load and save once in-game while still offline before reconnecting. Xbox cloud sync
can otherwise replace the local edit.

## Downloads and help

Download platform archives from [GitHub Releases](https://github.com/ChristopherVR/AbioticEditor/releases/latest)
or the Windows/Linux files on [Nexus Mods](https://www.nexusmods.com/abioticfactor/mods/244?tab=files).
Desktop packages include the .NET runtime. Windows runs `AbioticEditor.Web.exe`; Linux includes
`launch-linux.desktop` and `launch-linux.sh`. macOS downloads are CLI-only.

Windows builds are unsigned. Confirm the download source before allowing it to run. Release
archives include `SHA256SUMS.txt`; Scoop installs are available through this repository's bucket:

```console
scoop bucket add abiotic-editor https://github.com/ChristopherVR/AbioticEditor
scoop install abiotic-editor
scoop install abiotic-editor-cli
```

The desktop checks updates in **Settings > Updates**; the CLI supports `abioticeditor update`.
For a bug report, include the editor version, platform, save source, what happened, and a log.
Enable **Settings > Diagnostics**, reproduce the issue, and use **OPEN LOG FOLDER**. Errors are
logged even with diagnostic tracing off. Report through
[GitHub Issues](https://github.com/ChristopherVR/AbioticEditor/issues/new/choose) or
[Nexus Mods posts](https://www.nexusmods.com/abioticfactor/mods/244?tab=posts).

## Build and contribute

Requires the **.NET 10 SDK** and the pinned source submodules:

```console
git clone --recursive https://github.com/ChristopherVR/AbioticEditor.git
cd AbioticEditor
dotnet build src/AbioticEditor.Web
dotnet build src/AbioticEditor.Cli
dotnet test tests/AbioticEditor.Tests -f net10.0
```

Core owns parsing and editing. `Web.Shared` contains the shared Razor UI; `Web` hosts the local
Photino desktop window, and `Web.Wasm` hosts the browser edition. `Ui.Abstractions` defines
platform contracts. The CLI, plugin SDK, updater, and standalone live agent have separate roles.
See [Architecture and contributing](docs/reference/architecture.md) for the full project map,
save contract, test notes, and Pages workflow.

To work on the documentation:

```console
cd docs
npm ci
npm run docs:dev
npm run docs:build
```

The production build checks internal documentation links. GitHub Pages deploys the docs and the
browser app together; the docs preview alone does not include `/app/`. The
[technical reference](https://christophervr.github.io/AbioticEditor/reference/) includes save
schemas, localization, maintainer commands, and dated research notes. `docs/PROGRESS.md` is the
internal session history, not a current feature specification.

## License

[Apache License 2.0](LICENSE). Redistributions must retain [NOTICE](NOTICE) and identify changes.
See [third-party notices](THIRD-PARTY-NOTICES.txt). Bundled wiki reference images are credited to
[abioticfactor.wiki.gg](https://abioticfactor.wiki.gg) under CC BY-NC-SA.

Not affiliated with or endorsed by the developers of Abiotic Factor.
