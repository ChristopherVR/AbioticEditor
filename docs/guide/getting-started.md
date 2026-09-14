# Getting started

Abiotic Editor edits Abiotic Factor saves in a browser, a Windows/Linux desktop app, or a CLI.
The desktop app also supports [live editing](./live-editing), which has different save behavior.

## Your first save edit

1. Close the game or stop the dedicated server. Copy the world folder somewhere separate as a backup.
2. [Open the browser editor](/app/) or install the desktop app below.
3. Choose your save folder. Opening the SteamID account folder also includes character appearance,
   which lives beside `Worlds/`. The desktop app can discover local worlds automatically.
4. Select a player or world save and make an edit. Review it, then press **SAVE**.
5. If working from a zip or without browser write permission, press **EXPORT** after SAVE and
   copy the exported files back to the game's save folder before playing.

Direct file saves keep a `.bak` of the previous file. It is a rolling backup, not an unlimited
history. See [Browser editing](./browser-editor) for folder permissions and export details.

![The editor with a save folder loaded](/screenshots/01-loaded.png)

## Install

Grab the latest build for your platform from the
[**Releases page**](https://github.com/ChristopherVR/AbioticEditor/releases/latest):

The Windows and Linux / Steam Deck (Proton saves) editor packages are also available as separate
files on the [Nexus Mods downloads page](https://www.nexusmods.com/abioticfactor/mods/244?tab=files).

Each zip's name carries the release version (e.g. `AbioticEditor-desktop-win-x64-v1.2.0.zip`).

| Download | What it is |
|---|---|
| `AbioticEditor-desktop-win-x64-v<version>.zip` | Desktop editor (Windows). Extract and run `AbioticEditor.Web.exe`; no .NET install is needed. |
| `AbioticEditor-desktop-linux-x64-v<version>.zip` | Desktop editor (Linux, Steam Deck, and Proton saves). Extract, then double-click `launch-linux.desktop` and trust it. |
| `AbioticEditor-cli-win-x64-v<version>.zip` | Command-line tool (Windows) |
| `AbioticEditor-cli-linux-x64-v<version>.zip` | Command-line tool (Linux, including Steam Deck - finds saves inside the Proton prefix automatically) |
| `AbioticEditor-cli-osx-x64-v<version>.zip` / `-osx-arm64-…` | Command-line tool (macOS) |

Unzip and run. The app and CLI both self-update from GitHub Releases: the app from its
**Settings ▸ Updates** card, the CLI via `abioticeditor update`. Each release also ships a
`SHA256SUMS.txt` so you can verify a download.

### Windows: "unknown publisher" / SmartScreen

The Windows builds are **not code-signed** (a certificate that clears those warnings costs
money this free, fan-made tool doesn't spend), so Windows reports an unknown publisher and
SmartScreen or your antivirus may warn on first run. Check that you downloaded the release from this project before choosing to run it.

::: tip Install with Scoop
A command-line install via [Scoop](https://scoop.sh/) provides one-command upgrades. Scoop verifies each download against the SHA-256 pinned in
the manifest before extracting it.

```console
scoop bucket add abiotic-editor https://github.com/ChristopherVR/AbioticEditor
scoop install abiotic-editor          # desktop app
scoop install abiotic-editor-cli      # command-line tool
scoop update  abiotic-editor          # later, to upgrade
```
:::

If you'd rather run the zip download directly: right-click the downloaded `.zip` ▸
**Properties** ▸ tick **Unblock** ▸ **OK**, then unzip and run `AbioticEditor.Web.exe`. If
SmartScreen still shows "Windows protected your PC", click **More info ▸ Run anyway**.

::: tip macOS downloads are CLI-only
Use the browser editor for the graphical interface on macOS. The downloadable macOS command-line
builds are unsigned; review the download source if Gatekeeper flags the executable.
:::

## Build from source

Requires the **.NET 10 SDK**. Clone with submodules, since the build depends on the pinned
`submodules/` source projects (UeSaveGame and CUE4Parse).

```console
git clone --recursive https://github.com/ChristopherVR/AbioticEditor.git
cd AbioticEditor

dotnet build src/AbioticEditor.Web                                  # local desktop editor host
dotnet build src/AbioticEditor.Cli                                   # CLI
dotnet test  tests/AbioticEditor.Tests -f net10.0                     # tests
```

The Razor desktop app runs on Windows and Linux with the standard .NET SDK. Package versions are
managed centrally in `Directory.Packages.props`.

> The `CUE4Parse-Natives … 'cmake' is not recognized` line during a build is **benign**.
> The native texture decoder is optional and managed parsing still works.

## Where saves live

- **Client saves (Windows):** `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`
- **Client saves (Linux / Steam Deck, Proton):**
  `<Steam library>/steamapps/compatdata/427410/pfx/drive_c/users/steamuser/AppData/Local/AbioticFactor/Saved/SaveGames/<steamid>/Worlds/<WorldName>`
- **Dedicated server:** the folder containing `Worlds\<WorldName>` (the editor also finds
  `Admin.ini` and each world's `SandboxSettings.ini`).

::: tip Linux / Steam Deck (Proton)
The self-contained Linux editor provides the full point-and-click UI in its own desktop window.
Extract `AbioticEditor-desktop-linux-x64-v<version>.zip` in Desktop Mode, then double-click
`launch-linux.desktop` and choose Trust and Launch - no terminal needed.
The editor and CLI scan every Steam library, including internal storage, SD cards, Flatpak, and
Snap installs, and find saves inside Proton prefixes without requiring a manual `compatdata` path.
:::

Save kinds you'll see: `Player_<steamid64>.sav`, `WorldSave_<Region>.sav`, and
`WorldSave_MetaData.sav` (story/metadata).

## Next steps

- **[Documentation directory](/guide/)**: all player guides and reference links.
- **[Live editing](./live-editing)**: connect to a running game.
- **[Desktop app](/guide/desktop-app)**: the point-and-click editor.
- **[Command-line tool](/guide/cli)**: scripting and server admin.
- **[Plugins & language packs](/guide/plugins)**: install community tools and translations.
- **Something not working?** See [Reporting a bug](/guide/desktop-app#reporting-a-bug).
