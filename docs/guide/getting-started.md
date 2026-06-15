# Getting started

Abiotic Editor is a save-game editor for [Abiotic Factor](https://store.steampowered.com/app/427410/Abiotic_Factor/).
It reads and writes the game's GVAS save files, understands the game's own data tables
(items, recipes, skills, quest flags, story progression) by mounting the installed game's
pak archives, and ships both a desktop app and a command-line tool.

In plain terms: it opens the `.sav` files Abiotic Factor writes to disk and shows you what's
inside as clickable controls (sliders for your needs, a grid for your inventory, a checklist
for recipes), so you can change your run (or repair it) without a hex editor. Edits stage
until you press **SAVE**, and every write keeps a `.bak` backup.

![The editor with a save folder loaded](/screenshots/01-loaded.png)

## Install

Grab the latest build for your platform from the
[**Releases page**](https://github.com/ChristopherVR/AbioticEditor/releases/latest):

Each zip's name carries the release version (e.g. `AbioticEditor-app-win-x64-v1.2.0.zip`).

| Download | What it is |
|---|---|
| `AbioticEditor-app-win-x64-v<version>.zip` | Desktop app (Windows) - a single self-contained `.exe` (no .NET install needed) plus a `Mappings.usmap` data file |
| `AbioticEditor-app-osx-x64-v<version>.zip` / `-osx-arm64-…` | Desktop app (macOS, unsigned) |
| `AbioticEditor-cli-win-x64-v<version>.zip` | Command-line tool (Windows) |
| `AbioticEditor-cli-linux-x64-v<version>.zip` | Command-line tool (Linux) |
| `AbioticEditor-cli-osx-x64-v<version>.zip` / `-osx-arm64-…` | Command-line tool (macOS) |

Unzip and run. The app and CLI both self-update from GitHub Releases: the app from its
**Settings ▸ Updates** card, the CLI via `abioticeditor update`. Each release also ships a
`SHA256SUMS.txt` so you can verify a download.

### Windows: "unknown publisher" / SmartScreen

The Windows builds are **not code-signed** (a certificate that clears those warnings costs
money this free, fan-made tool doesn't spend), so Windows reports an unknown publisher and
SmartScreen or your antivirus may warn on first run. The download is safe; the warning is just
the absence of a paid signature. Two ways around it:

::: tip Install with Scoop (no warning)
A command-line install via [Scoop](https://scoop.sh/) skips the SmartScreen prompt entirely
and gives you one-command upgrades. Scoop verifies each download against the SHA-256 pinned in
the manifest before extracting it.

```console
scoop bucket add abiotic-editor https://github.com/ChristopherVR/AbioticEditor
scoop install abiotic-editor          # desktop app
scoop install abiotic-editor-cli      # command-line tool
scoop update  abiotic-editor          # later, to upgrade
```
:::

If you'd rather run the zip download directly: right-click the downloaded `.zip` ▸
**Properties** ▸ tick **Unblock** ▸ **OK**, then unzip and run `AbioticEditor.App.exe`. If
SmartScreen still shows "Windows protected your PC", click **More info ▸ Run anyway**.

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
