# Sample plugins

This folder holds the worked **sample plugins** for Abiotic Editor. They are not part of the
shipped app; each one is a small, self-contained example of one slice of the plugin system, and
each has its own `README.md` with the details.

If you just want to understand the system, start with the docs:

- [Plugin system](../docs/plugins.md) - the architecture, the trust model, and why it is shaped
  this way.
- [Authoring guide](../docs/plugin-authoring.md) - a step-by-step walkthrough of every capability,
  managed and JavaScript.
- [Plugin fix-ups](../docs/plugin-fixups.md) - using plugins to recover saves a game patch broke.

## What a plugin is

A plugin is a standalone unit that extends the editor without living in its source tree. It is
either a compiled **.NET assembly** (`.dll`) or a **JavaScript file** (`.js`, run on the bundled
Jint engine, no build step). Either kind can contribute any mix of:

| Capability | What it adds | Surfaced in |
|---|---|---|
| **Save operation** | a scripted read/modify pass over a save | CLI `plugins run`, GUI Plugins panel |
| **Console command** | a whole new CLI verb | CLI (`abioticeditor <verb>`) |
| **Editor tool** | a native MAUI UI panel | GUI Plugins panel |
| **Web tool** | an HTML/React UI in a web view, with a JS-to-host bridge | GUI Plugins panel |
| **Menu action** | a click-to-run menu item | GUI Plugins menu + panel |
| **Event handler** | code that runs on a host event (save opened/written, app start) | everywhere |
| **Save upgrader** | recovers a save whose version the editor cannot load yet | load path |

Every plugin is a **subfolder** containing a `plugin.json` manifest next to its `.dll` or `.js`.

## The samples

| Plugin | Runtime | Capability | What it shows |
|---|---|---|---|
| [`MaxSkills`](MaxSkills/) | .NET | save operation | the smallest managed save operation, with a parameter |
| [`RepairNeeds`](RepairNeeds/) | .NET | save operation | a no-parameter operation that tops up survival needs |
| [`GrantFlag`](GrantFlag/) | .NET | save operation | a forward-compatible fix-up: add a raw world flag |
| [`SaveStats`](SaveStats/) | .NET | console command | a new CLI verb that behaves like a built-in |
| [`VersionShim`](VersionShim/) | .NET | save upgrader | recovering a save with an unsupported version |
| [`PlaytimeDashboard`](PlaytimeDashboard/) | .NET / MAUI | editor tool | a UI panel whose view is built in C# |
| [`SaveInspector`](SaveInspector/) | .NET / MAUI | editor tool | full MVVM: compiled XAML plus a view-model |
| [`HelloScript`](HelloScript/) | JavaScript | save op + command + menu action + event handler | one script, four capabilities, no build |
| [`WebStats`](WebStats/) | JavaScript | web tool | an offline HTML UI served from a bundled folder |
| [`ReactDashboard`](ReactDashboard/) | JavaScript | web tool | a React UI (React from a CDN), no build step |
| [`ReactAppDashboard`](ReactAppDashboard/) | JavaScript | web tool + save op | a full Vite + React app that also drives the editor |

## Building the samples

**.NET samples** build with `dotnet build`. The two MAUI ones (`PlaytimeDashboard`,
`SaveInspector`) need the MAUI workload and a target framework moniker:

```console
dotnet build plugins/MaxSkills -c Release
dotnet build plugins/SaveInspector -c Release -f net10.0-windows10.0.19041.0
```

The output of a .NET sample is just **its own DLL plus `plugin.json`**: the samples reference the
SDK and Core with `Private="false" ExcludeAssets="runtime"`, so the shared assemblies are not
copied (the host provides them at runtime). This is the [shared-assembly
rule](../docs/plugins.md#isolation--the-shared-assembly-rule).

**JavaScript samples** need no build. Copy the folder as-is. The one exception is
`ReactAppDashboard`, whose React UI is built once with `npm run build` (its own README covers it).

## Installing and running

Copy a built sample's folder (its `plugin.json` plus its `.dll`/`.js` and any assets) into a
plugin root:

```
%LOCALAPPDATA%\AbioticEditor\plugins\<your-plugin>\
```

For development you can skip the copy and point the editor straight at a folder with
`ABIOTIC_PLUGINS_DIR`:

```powershell
$env:ABIOTIC_PLUGINS_DIR = "D:\Development\uesave\plugins\MaxSkills\bin\Debug\net10.0"
abioticeditor plugins list
```

Then:

- **CLI:** `abioticeditor plugins list`, `plugins info <id>`, and
  `plugins run <operation> <save> [--param k=v] [--dry-run]`. Plugin console commands appear as
  top-level verbs in `abioticeditor --help`.
- **App:** Settings, then Manage Plugins: enable/disable each plugin, run save operations against
  the open save, open UI/web tools, and run menu actions.

Set `ABIOTIC_NO_PLUGINS=1` to disable all plugin loading.

> **Trust:** plugins run with full trust. There is no sandbox; a loaded plugin has the same
> privileges as the editor. The system makes loading deliberate and visible (a manifest gate, an
> enable/disable switch, provenance in every list), it does not contain hostile code. Only install
> plugins you trust.
