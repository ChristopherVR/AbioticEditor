# Getting started

Abiotic Editor opens your **Abiotic Factor** saves and shows what is inside as plain controls: sliders for needs, a grid for your inventory, a checklist for recipes, a 3D view of your base. Change what you want, press **SAVE**, and load the game.

## First rule: secure the specimen

1. Close Abiotic Factor, or stop the dedicated server.
2. Copy your whole world folder somewhere safe. Give the copy a name such as `Cascade before edits`.
3. Open the editor and choose **Open offline editor**.
4. Pick your world, make one small change, then choose **SAVE**.
5. Start the game and check that the change is where you expected it.

The editor keeps a `.bak` copy beside every file it writes. That is handy for an immediate undo, but it is one rolling backup. Keep your own copy of the whole world for real experiments.

![Choose offline or live editing](/screenshots/00-editing-modes.png)

*Open offline editor works on save files. Live editing changes a running game.*

## Download the desktop app

Get the latest version from [GitHub Releases](https://github.com/ChristopherVR/AbioticEditor/releases/latest) or the [Nexus Mods files tab](https://www.nexusmods.com/abioticfactor/mods/244?tab=files). Nothing else needs to be installed, and nothing from the game is in the download: the editor reads names, pictures and 3D models from your own copy of the game.

| Download | Use it when... |
| --- | --- |
| `AbioticEditor-desktop-win-x64-...zip` | You play on Windows. Extract it and run `AbioticEditor.Web.exe`. |
| `AbioticEditor-desktop-linux-x64-...zip` | You play on Linux or Steam Deck. See [Linux and Steam Deck](./linux-local-host). |
| `AbioticEditor-desktop-osx-arm64-...zip` | You use a Mac with Apple silicon (M1 or later). |
| `AbioticEditor-desktop-osx-x64-...zip` | You use an older Intel Mac. |
| `AbioticEditor-cli-...zip` | You use command-line tools or run a server. Most players can skip this. |

The 3D view and its game models come with the desktop app. On the first start, the start page offers to **get the 3D view ready**: see [Bases and the 3D view](./3d-view#get-the-3d-view-ready).

### Windows first-run warning

This free fan tool is not code-signed, so Windows may call it an unknown publisher the first time. Check that the zip came from this project's Releases or Nexus page. If Windows blocks the zip, right-click it, choose **Properties**, tick **Unblock**, choose **OK**, then extract it. If SmartScreen appears, choose **More info**, then **Run anyway**.

Prefer to skip the warning? Install with [Scoop](https://scoop.sh/), a Windows package manager:

```console
scoop bucket add abiotic-editor https://github.com/ChristopherVR/AbioticEditor
scoop install abiotic-editor
```

### macOS

Extract the zip, open **Terminal** in that folder and run `bash launch-mac.sh`. The download is not notarized by Apple, so the launcher clears the download flag that would otherwise stop macOS from opening it. Live editing is not available on macOS; everything else works.

## Find your saves

The desktop app looks for your saves and lists them under **Your worlds**. Choose **OPEN** beside the one you want. If it is not listed, use **OPEN FOLDER**, or drag the folder onto the window.

| Where you play | Usual save location |
| --- | --- |
| Windows (Steam) | `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<your SteamID>\Worlds\<WorldName>` |
| Linux or Steam Deck (Proton) | `<Steam library>/steamapps/compatdata/427410/pfx/drive_c/users/steamuser/AppData/Local/AbioticFactor/Saved/SaveGames/<your SteamID>/Worlds/<WorldName>` |
| Game Pass | Found for you. See [Game Pass saves](./game-pass). |
| Dedicated server | The folder that contains `Worlds\<WorldName>` |

On Windows, paste `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames` into the File Explorer address bar and press Enter.

When you can, open the **account folder** one level above `Worlds`. It holds your worlds and your saved character looks together, so the appearance editor works too.

## Staying up to date

The version you have is shown at the top of the window. To update, download the newest release and replace the old files. Your settings and plugins live elsewhere and are kept. Scoop users run `scoop update abiotic-editor`.

After an update the editor shows what changed. You can see it again any time from **Settings ▸ General ▸ SHOW RELEASE NOTES**.

## Your next job

- Take the [desktop app tour](./desktop-app) to see every player and world tool.
- See your base in 3D: [Bases and the 3D view](./3d-view).
- Want to work in a browser? Read [Edit in your browser](./browser-editor).
- Want to shift loot between saves? Read [Transfer items](./transfer-items).
- Something misbehaved? The [desktop guide](./desktop-app#reporting-a-bug) explains how to send a useful report.
