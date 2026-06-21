# Sample plugins

The repository ships eleven worked sample plugins under [`plugins/`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins).
Each is small, self-contained, and demonstrates one slice of the system. Each also has its own
`README.md` in its folder with build and run details.

Use them as starting points: copy the one closest to what you want, change the id and the logic, and
rebuild. For the design behind these, see [Plugin system](./plugin-system.md); for a step-by-step
walkthrough, see the [Authoring guide](./plugin-authoring.md).

## At a glance

| Plugin | Runtime | Capability | What it shows |
|---|---|---|---|
| `MaxSkills` | .NET | save operation | the smallest managed save operation, with a parameter |
| `RepairNeeds` | .NET | save operation | a no-parameter operation; handling delta-serialized needs |
| `GrantFlag` | .NET | save operation | a forward-compatible fix-up: add a raw world flag |
| `SaveStats` | .NET | console command | a new CLI verb that behaves like a built-in |
| `VersionShim` | .NET | save upgrader | recovering a save with an unsupported version |
| `PlaytimeDashboard` | .NET / MAUI | editor tool | a UI panel whose view is built in C# |
| `SaveInspector` | .NET / MAUI | editor tool | full MVVM: compiled XAML plus a view-model |
| `HelloScript` | JavaScript | save op + command + menu action + event handler | four capabilities in one script, no build |
| `WebStats` | JavaScript | web tool | an offline HTML UI served from a bundled folder |
| `ReactDashboard` | JavaScript | web tool | a React UI (React from a CDN), no build step |
| `ReactAppDashboard` | JavaScript | web tool + save op | a full Vite + React app that also drives the editor |

## Save operations (.NET)

**`MaxSkills`** registers a `max-skills` operation for player saves. It reads the typed model with
`PlayerSaveReader`, raises every skill below a target level (a `level` parameter, default 10, max
20), and calls `MarkChanged()`. It never lowers an over-cap skill, and reports a no-op when nothing
needs raising. This is the canonical first plugin.

**`RepairNeeds`** registers a parameter-free `repair-needs` operation that tops hunger, thirst,
sanity, fatigue, and continence back to 100. It also fixes the case where a need reads as 0 because
it sat at its blueprint default and was therefore omitted from the file (the game delta-serializes,
so an absent property is a real value, not missing data).

**`GrantFlag`** registers a `grant-flag` operation for world saves that appends a named entry to the
`WorldFlags` array (a required `flag` parameter). It works at the raw property-tree level, so it can
grant a flag this build of the editor has no model for. That makes it a forward-compatible fix-up;
see [Plugin fix-ups](./plugin-fixups.md).

## Console command (.NET)

**`SaveStats`** registers a `save-stats` CLI verb that prints a one-glance summary of a player or
world save (money and skills for players; flag, container, door, NPC counts and minutes played for
worlds), with a `--json` switch. It writes to the abstract `ctx.Out`/`ctx.Error` writers (so it is
unit-testable) and returns the standard 0/1/2 exit codes. It is read-only. The CLI exposes it as a
top-level verb in `abioticeditor --help`; GUI hosts ignore console commands.

## Save upgrader (.NET)

**`VersionShim`** registers a save **upgrader** (not an operation). An operation edits an
already-loaded save; an upgrader runs precisely when a load *fails*, so it works on raw bytes. When
the host cannot parse a save, it offers the file to each upgrader in turn. `VersionShim` claims a
file whose `SaveGameVersion` is unknown and rewrites that 4-byte field (at offset 4, past the `GVAS`
magic) to the modern supported value, leaving the rest intact. The host then loads the corrected
bytes and can persist them after a `.preupgrade.bak`. It is the conservative reference for the
`ISaveUpgrader` hook.

## Editor tools (.NET / MAUI)

These need the MAUI workload and a target framework moniker to build.

**`PlaytimeDashboard`** registers a read-only "Dashboard" panel whose MAUI view is built entirely in
C# (no XAML). It shows headline numbers for the open save and subscribes to `ActiveSaveChanged` to
refresh on a file switch. The simplest UI-tool example.

**`SaveInspector`** is the full-MVVM reference: a compiled `InspectorView.xaml` (with `x:DataType`
for compiled bindings) bound to an `InspectorViewModel` (`INotifyPropertyChanged`, a `Command`, an
`ObservableCollection`, and `IDisposable`). It lists the open player save's skills and stats. The
`IDisposable` matters: the view-model unsubscribes from `ActiveSaveChanged` when the panel closes,
so it cannot outlive its panel.

## JavaScript plugins (no build step)

**`HelloScript`** registers four capabilities from one `plugin.js`: a `rich-player` save operation
(uses the `ctx.player` facade to set money and skills), a `js-greet` console command, a `say-hi`
menu action, and a `save.written` event handler. The broadest single-file tour of the `abiotic`
host API.

**`WebStats`** registers a web tool that serves an offline `web/index.html` from a bundled folder
(no CDN, no build). The page reads the open save through the host bridge
(`abiotic.request(...)` resolved by the plugin's `handleMessage`, which can call
`ctx.playerSummaryJson()`).

**`ReactDashboard`** registers a web tool whose UI is a React app rendered from an inline HTML string
with React and Babel from a CDN (no build step). It reads the open player save through the bridge
and refreshes on host events. A CDN needs internet at runtime; a shipping plugin should bundle
assets or pin Subresource Integrity hashes.

**`ReactAppDashboard`** is a real Vite + React project under `app/`, built to a single self-contained
`index.html` (via `vite-plugin-singlefile` and `base: "./"`, so it works from a `file://` WebView).
Beyond reading the save, it **drives the editor**: a button asks the plugin to call
`abiotic.ui.runSaveOperation("react-max-skills")`, so a click runs a real, backed-up edit and
reloads the editor. The production-grade web-tool example. Build it once with
`cd app && npm install && npm run build`.

## Building and installing

See [Building and installing plugins](./plugin-building.md) for the full commands. In short: .NET
samples build with `dotnet build plugins/<name>` (the MAUI ones add `-f <tfm>`), JavaScript samples
need no build, and you install a plugin by copying its folder into a plugin root or by pointing
`ABIOTIC_PLUGINS_DIR` at it.
