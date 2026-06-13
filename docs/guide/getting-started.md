# Getting started

Abiotic Editor is a save-game editor for [Abiotic Factor](https://store.steampowered.com/app/427410/Abiotic_Factor/).
It reads and writes the game's GVAS save files, understands the game's own data tables
(items, recipes, skills, quest flags, story progression) by mounting the installed game's
pak archives, and ships both a desktop app and a command-line tool.

## Install

Grab the latest build for your platform from the
[**Releases page**](https://github.com/ChristopherVR/AbioticEditor/releases/latest):

| Download | What it is |
|---|---|
| `AbioticEditor-app-win-x64.zip` | Desktop app (Windows) |
| `AbioticEditor-app-osx-x64.zip` / `-osx-arm64.zip` | Desktop app (macOS, unsigned) |
| `AbioticEditor-cli-win-x64.zip` | Command-line tool (Windows) |
| `AbioticEditor-cli-linux-x64.zip` | Command-line tool (Linux) |
| `AbioticEditor-cli-osx-x64.zip` / `-osx-arm64.zip` | Command-line tool (macOS) |

Unzip and run. The app and CLI both self-update from GitHub Releases: the app from its
**Settings ▸ Updates** card, the CLI via `abioticeditor update`.

::: tip macOS is unsigned
The macOS builds are not code-signed, so Gatekeeper will warn on first launch. Right-click
the app and choose **Open**, or clear the quarantine flag, to run it.
:::

## Build from source

Requires the **.NET 10 SDK**. Clone with submodules, since the build depends on the pinned
`submodules/` source projects (UeSaveGame and CUE4Parse).

```console
git clone --recursive https://github.com/ChristopherVR/AbioticEditor.git
cd AbioticEditor

dotnet build src/AbioticEditor.App -f net10.0-windows10.0.19041.0   # desktop editor (Windows)
dotnet build src/AbioticEditor.Cli                                   # CLI
dotnet test  tests/AbioticEditor.Tests                               # tests
```

The app project also targets Android, iOS, and Mac Catalyst; building those needs the
matching MAUI workloads (`dotnet workload install maui`). Package versions are managed
centrally in `Directory.Packages.props`.

> The `CUE4Parse-Natives … 'cmake' is not recognized` line during a build is **benign**.
> The native texture decoder is optional and managed parsing still works.

## Where saves live

- **Client saves:** `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`
- **Dedicated server:** the folder containing `Worlds\<WorldName>` (the editor also finds
  `Admin.ini` and each world's `SandboxSettings.ini`).

Save kinds you'll see: `Player_<steamid64>.sav`, `WorldSave_<Region>.sav`, and
`WorldSave_MetaData.sav` (story/metadata).

## Next steps

- **[Desktop app](/guide/desktop-app)**: the point-and-click editor.
- **[Command-line tool](/guide/cli)**: scripting and server admin.
- **[Plugins](/plugins)**: extend the editor with your own tools.
