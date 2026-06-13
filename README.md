# Abiotic Editor

A save-game editor for [Abiotic Factor](https://store.steampowered.com/app/427410/Abiotic_Factor/).
It reads and writes the game's GVAS save files byte-for-byte, understands the game's own
data tables (items, recipes, skills, quest flags, story progression) by mounting the
installed game's pak archives, and ships both a desktop app and a command-line tool.

## Layout

| Path | Contents |
|---|---|
| `src/AbioticEditor.App` | .NET MAUI desktop editor. |
| `src/AbioticEditor.Cli` | Headless CLI (`abioticeditor`) for scripting and server admin. |
| `src/AbioticEditor.Core` | All parsing and editing logic. The app and CLI are thin front-ends over this, so new tooling can reuse it directly. |
| `src/AbioticEditor.Plugins.Abstractions` | The public plugin SDK (host-agnostic contracts). |
| `plugins/` | Sample plugins (save operations, a CLI command, and UI tools). |
| `tests/AbioticEditor.Tests` | Assertion tests over real save fixtures. |
| `tests/AbioticEditor.Probes` | Research probes that dump game data structures. Not part of the normal test run. |
| `assets/Mappings.usmap` | Bundled type mappings for the validated game build. |
| `submodules/` | Pinned source builds of UeSaveGame and CUE4Parse (see below). |
| `docs/` | Save-format research notes and the working progress log. |

## Building

```console
git clone --recursive <this repo>
dotnet build src/AbioticEditor.App -f net10.0-windows10.0.19041.0   # desktop editor (Windows)
dotnet build src/AbioticEditor.Cli                                   # CLI
dotnet test tests/AbioticEditor.Tests                                # tests
```

Requires the .NET 10 SDK. The app project also targets Android, iOS and Mac Catalyst;
building those needs the matching MAUI workloads (`dotnet workload install maui`).
Package versions are managed centrally in `Directory.Packages.props`.

## Using the app

1. Start the app and click **OPEN FOLDER**. Point it at a save directory:
   - Client saves: `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>`
   - Dedicated server: the folder containing `Worlds\<WorldName>` (the app also finds
     `Admin.ini` and each world's `SandboxSettings.ini` and lists them under
     CONFIG FILES in the sidebar)
2. Pick a file from the sidebar. Player saves open the player editor (vitals, inventory,
   equipment, skills, traits, recipes, codex, transmog, spawn point, achievements,
   SteamID change). World saves open the world editor (containers, quest flags, doors,
   dropped items, NPCs, bases, story progression). The metadata save edits story state.
3. Edits are staged until you press **SAVE**. Every write keeps a `.bak` of the previous
   file next to it.
4. Quest flags are gated by story prerequisites; the editor offers to set missing
   prerequisite flags rather than letting you create an inconsistent story state.
5. JSON export/import gives you the full save as editable text for anything the UI does
   not cover.

Diagnostic logging is opt-in (toggle in the status bar). When enabled it traces every
staged change and records any save content this build does not recognize.

## Using the CLI

The CLI wraps the same Core library, so anything it writes is byte-identical to what the
app would write. Run `abioticeditor --help` for the full surface. Examples:

```console
abioticeditor scan <folder>                      # list saves with kind and version
abioticeditor info <save.sav>                    # key facts about one save
abioticeditor export-json <save.sav> -o out.json # lossless JSON dump
abioticeditor import-json <save.sav> in.json     # rebuild the save from JSON
abioticeditor flags list <world.sav>             # quest flags (--filter to narrow)
abioticeditor flags set <world.sav> <flag>       # set a flag (--clear, --force)
abioticeditor steamid <player.sav> <newid64>     # reassign the owning Steam account
abioticeditor ini list <file.ini>                # sections of an ini file
abioticeditor ini get/set <file.ini> ...         # read or edit ini values
abioticeditor version                            # tool + supported save versions
```

Exit codes: 0 success, 1 usage or data error, 2 unexpected failure. `--json` switches
`scan`/`info`/`flags list` to machine-readable output.

## Plugins

The editor is extensible through **plugins** — standalone units that add functionality
without being part of this source tree. A plugin can be a compiled **.NET assembly** (`.dll`)
or a **JavaScript file** (`.js`, run on a bundled engine — no build step), and either kind can
contribute any mix of:

| Capability | What it does | Surfaced in |
|---|---|---|
| **Save operation** | Read/modify a save (cheats, bulk edits, fixes) | CLI `plugins run`, GUI Plugins panel |
| **Console command** | A whole new CLI verb | CLI (`abioticeditor <verb>`) |
| **Editor tool** | A UI panel (MAUI view, incl. XAML/MVVM) | GUI Plugins panel |
| **Web tool** | An **HTML/React** UI rendered in a web view, with a JS↔host bridge | GUI Plugins panel |
| **Menu action** | A click-to-run menu item | GUI **Plugins** menu + Plugins panel |
| **Event handler** | Runs when something happens (save opened/written, app started) | everywhere |

> **Trust:** plugins run with full trust (there is no sandbox). The system makes loading
> deliberate and visible — only install plugins you trust.

### Where plugins live

Each plugin is a subfolder containing a `plugin.json` plus its `.dll` or `.js`:

```
%LOCALAPPDATA%\AbioticEditor\plugins\        (user plugins; survive updates)
<exe dir>\plugins\                            (plugins shipped next to the app/CLI)
  max-skills\
    plugin.json
    MaxSkills.dll
  hello-script\
    plugin.json
    plugin.js
```

Point the editor at a different folder for development with the `ABIOTIC_PLUGINS_DIR`
environment variable; set `ABIOTIC_NO_PLUGINS=1` to disable plugin loading entirely.

### Using plugins — CLI

```console
abioticeditor plugins list                       # installed plugins + load state + capabilities
abioticeditor plugins info <id>                  # one plugin's details (operations, commands, ...)
abioticeditor plugins run <operation> <save>     # run a save operation (keeps a .bak)
abioticeditor plugins run <operation> <save> --param name=value --dry-run
abioticeditor <plugin-command> ...               # plugin console commands are top-level verbs
```

### Using plugins — app

- **Settings** lists every installed plugin with an **enable/disable** switch, and a
  **Manage Plugins** button opening a panel that runs save operations against the open save,
  opens UI tools, and runs menu actions.
- Plugin **menu actions** also appear in a top-level **Plugins** menu.
- **Enable/disable** is persisted to the plugin's `plugin.json`; restart to apply.

### Bundled samples (under `plugins/`)

| Sample | Kind | Capability |
|---|---|---|
| `MaxSkills` | .NET | save operation (`max-skills`) |
| `SaveStats` | .NET | console command (`save-stats`) |
| `PlaytimeDashboard` | .NET / MAUI | editor tool (C#-built view) |
| `SaveInspector` | .NET / MAUI | editor tool (compiled **XAML + MVVM**) |
| `HelloScript` | **JavaScript** | save op + command + menu action + event handler |
| `ReactDashboard` | **JavaScript** | web tool — a **React** UI (React from a CDN) over the host bridge |
| `ReactAppDashboard` | **JavaScript** | web tool — a **full Vite + React app** that also drives the app UI |
| `WebStats` | **JavaScript** | web tool — an **offline HTML** UI served from a bundled folder |

Build the .NET samples with `dotnet build plugins/<name>` (the MAUI ones need a `-f <tfm>`);
JavaScript samples need no build — copy the folder into a plugins directory.

**HTML/React UIs.** A JavaScript plugin can render its UI as a web page (`abiotic.registerWebTool`)
— the host shows it in a web view and wires a bridge so page JavaScript can call back into the
plugin: `abiotic.request({...})` returns a Promise the plugin's `handleMessage` resolves (e.g.
with save data), and `abiotic.log` / `abiotic.onEvent` are available too. The page can be inline
HTML (pull React/Vue/etc. from a CDN — see `ReactDashboard`) or a bundled folder of static assets
— either an offline page (`WebStats`) or a **full Vite + React build** (`ReactAppDashboard`, a real
`npm`/Vite project under `app/`). For shipping plugins, prefer bundling assets or pinning
Subresource Integrity hashes over CDN loads.

JavaScript plugins can also **drive the app** through `abiotic.ui`: show alerts/toasts, run a
registered save operation against the open save, reload it, or open app screens — so a web UI
(e.g. a React button) can trigger real editor actions, not just read data.

### Writing a plugin (quick taste)

A JavaScript plugin is just a `.js` file that registers capabilities on the `abiotic` host
object:

```javascript
abiotic.registerCommand({
    name: "js-greet",
    description: "Print a greeting from JavaScript.",
    invoke: function (ctx) { ctx.print("Hello from a plugin!"); return 0; }
});
abiotic.on("save.written", function (e) { abiotic.log.info("saw " + e.name + " " + e.savePath); });
```

with a `plugin.json` declaring `"runtime": "javascript"` and `"entryScript": "plugin.js"`.

See [`docs/plugins.md`](docs/plugins.md) for the architecture, rationale, and security model,
and [`docs/plugin-authoring.md`](docs/plugin-authoring.md) for the full authoring guide
(managed and JavaScript, all five capabilities, host events, and the data directory).

## Why submodules?

`submodules/UeSaveGame` does the GVAS (de)serialization and `submodules/CUE4Parse` reads
the game's pak archives. Both are pinned as source submodules instead of NuGet packages
because the editor depends on exact serialization behavior: a save must round-trip
byte-identical, and we need to debug into both libraries when the game updates its formats.
Pinned commits mean a checkout always builds the combination that was tested. Clone with
`--recursive`, or run `git submodule update --init` after a plain clone.

There is no good NuGet alternative for either, which is the other half of the decision:

- **CUE4Parse** *does* publish a NuGet package, but it is tagged rarely — the latest
  (`1.2.2`, Feb 2025) sits ~840 commits / over a year behind the `master` we track, and the
  editor relies on current-`master` behavior (e.g. the texture-decode path in
  `GameAssetProvider`). Pinning to that package would be a large, breaking regression. We
  follow `master` directly and bump the pinned commit deliberately.
- **UeSaveGame** is not published to NuGet at all, so a submodule is the only way to consume it.

Both submodules currently track their upstream default branch exactly (no local patches);
update them with `git submodule update --remote` and commit the new pinned commit once
re-validated against the test fixtures.

The repo root's `Directory.Build.props` and `Directory.Packages.props` are shielded from
the submodules (`submodules/Directory.*.props`) so our analyzer and package settings
never alter how the upstream code builds.

## Updating to a newer game version (usmap)

Reading the game's data tables requires a `Mappings.usmap` matching the installed game
build. The bundled one (`assets/Mappings.usmap`) matches the build this editor was
validated against. When the game updates:

1. Dump a fresh usmap from the running game with
   [Dumper-7](https://github.com/Encryqed/Dumper-7) or
   [FModel](https://fmodel.app/) (FModel: Settings, then enable the usmap export).
2. Install it either way:
   - In the app: status bar, **IMPORT USMAP**, pick the file. It is validated and copied
     into place; restart the editor to load it.
   - Manually: copy it to `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap`.
3. The user-installed file always wins over the bundled one. Delete it to fall back.

Without a matching usmap the editor still opens and edits saves; only asset-backed
features (item catalog, icons, recipe browser, skill milestones) degrade.

## Version compatibility

Save headers carry an `ABF_SAVE_VERSION`. The versions this build was validated against
live in one place, `SaveVersionRegistry` (`src/AbioticEditor.Core/Compatibility`), shown
by `abioticeditor version`. When you open a save the editor produces a compatibility
report with one of these severities:

| Severity | Meaning | Behavior |
|---|---|---|
| `Exact` | Known version, nothing unrecognized. | No warning. |
| `NewerMinor` | Known version but the save carries content this build has no model for (new quest flags, properties, enum values). | Warning lists what was found. Unknown content is preserved untouched on save; it is just not editable. |
| `NewerVersion` | Save version above what this build knows. | Prominent warning. Editing still works for readable structures; a `.bak` is always kept. |
| `Unknown` | Save class not recognized at all. | Editing not recommended. |

The same tolerance applies inside the data: unknown quest flags round-trip byte-identical
and are never blocked by prerequisite gating, unknown skills/fish/recipes are preserved
and labeled, unknown enum values (equip slots, liquids, door states) display as numbered
placeholders instead of breaking, and new backpacks or keypad-hacker tiers are picked up
from the game's own data tables at runtime. After a game update, the expected workflow
is: import a fresh usmap, check `abioticeditor version` (or the in-app warning), and
bump `SaveVersionRegistry` once the new format is validated.
