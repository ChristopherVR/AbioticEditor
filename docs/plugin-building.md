# Building and installing plugins

This page collects the practical mechanics: how to build a plugin, where to put it, and how to
confirm the host loaded it. For writing the code, see the [Authoring guide](./plugin-authoring.md);
for the design, see [Plugin system](./plugins.md).

## Where plugins live

A plugin is a **subfolder** that contains a `plugin.json` manifest next to its `.dll` or `.js`:

```
<plugin-root>/
  max-skills/
    plugin.json
    MaxSkills.dll
  hello-script/
    plugin.json
    plugin.js
```

Two roots are scanned, **user first** (a user-installed plugin shadows a bundled one with the same
id):

| Root | Path | For |
|---|---|---|
| User | `%LOCALAPPDATA%\AbioticEditor\plugins` | plugins a user installs; survive app updates |
| Bundled | `<exe dir>\plugins` | plugins shipped next to the app/CLI |

Override both with the `ABIOTIC_PLUGINS_DIR` environment variable (one or more paths, joined by the
platform path separator). This is how tests, development, and portable installs point at a custom
location. Set `ABIOTIC_NO_PLUGINS=1` to disable plugin loading entirely.

Each plugin also gets a private, writable **data directory** at
`%LOCALAPPDATA%\AbioticEditor\plugin-data\<id>` (reachable as `host.DataDirectory`), kept separate
from the install folder so user data survives plugin updates.

## Building a .NET plugin

A managed plugin is a normal .NET class library. Build it in Release:

```console
dotnet build plugins/MaxSkills -c Release
```

The output you ship is just **your DLL plus `plugin.json`**. The SDK, Core, and MAUI are referenced
to compile but not copied, because the host provides them at runtime and unifies the types (the
[shared-assembly rule](./plugins.md#isolation--the-shared-assembly-rule)). In the project file:

```xml
<ProjectReference Include="...\AbioticEditor.Plugins.Abstractions.csproj"
                  Private="false" ExcludeAssets="runtime" />
```

If you ship your own copy of a shared assembly, casts across the host boundary fail with a confusing
`InvalidCastException`, so the rule is not optional.

### MAUI (UI) plugins

An editor tool takes a MAUI dependency (the SDK does not). These projects multi-target the app's
heads, so you must pass a target framework moniker for the head you run on, and you need the MAUI
workload:

```console
dotnet workload install maui
dotnet build plugins/SaveInspector -c Release -f net10.0-windows10.0.19041.0
```

Reference `Microsoft.Maui.Controls` with `ExcludeAssets="runtime"` as well. XAML is compiled at
build time, so binding and handler errors surface as build errors.

## Building a JavaScript plugin

There is no build step. A JavaScript plugin is a `.js` file next to a `plugin.json` that sets
`"runtime": "javascript"` and `"entryScript": "plugin.js"`. Copy the folder and you are done.

The one exception is a plugin that bundles a built web UI, like `ReactAppDashboard`: its React app
is compiled once with its own toolchain, but the plugin itself still needs no .NET build.

```console
cd plugins/ReactAppDashboard/app
npm install
npm run build      # produces app/dist/index.html, which the plugin serves
```

## The manifest

```json
{
  "id": "com.example.max-skills",
  "name": "Max Skills",
  "version": "1.0.0",
  "author": "You",
  "description": "Levels every skill on a player save.",
  "runtime": "dotnet",
  "entryAssembly": "MaxSkills.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation"],
  "enabled": true
}
```

| Field | Notes |
|---|---|
| `id` | Globally unique and stable. Names your data directory and is how every host refers to you. |
| `runtime` | `"dotnet"` (default, a managed `.dll`) or `"javascript"` (a `.js` on the bundled Jint engine). |
| `entryAssembly` | A bare `.dll` file name in this folder (never a path). Required for `dotnet`. |
| `entryScript` | A `.js` file name in this folder. Required for `javascript`. |
| `minHostVersion` | Minimum plugin-SDK version. The host refuses to load if its SDK is older, with a clear message instead of a runtime `MissingMethodException`. |
| `capabilities` | An advisory list for the listing UI and for letting a host skip a plugin it cannot use. The real capabilities come from what the plugin registers. |
| `enabled` | Defaults to `true`. Disabled plugins are listed but never loaded. The enable/disable toggle is persisted here. |

The loader is strict on `id` and `entryAssembly` (no path traversal) and lenient on the descriptive
fields. The whole manifest is read **without loading any code**, so listing and version-checking are
cheap and safe even for an untrusted folder.

## Installing

Copy the build output's plugin folder (its `plugin.json` plus its `.dll`/`.js` and any bundled
assets) into a plugin root:

```
%LOCALAPPDATA%\AbioticEditor\plugins\<your-plugin>\
```

For development, skip the copy and point the editor straight at your build output:

```powershell
$env:ABIOTIC_PLUGINS_DIR = "D:\path\to\plugins\MaxSkills\bin\Release\net10.0"
abioticeditor plugins list
```

## Confirming it loaded

```console
abioticeditor plugins list          # installed plugins, load state, and capabilities
abioticeditor plugins info <id>     # one plugin's operations, commands, tools, and any load error
```

A plugin that fails to load is listed as **Failed** with its error; it never takes the host down. In
the app, the same information is under Settings, Manage Plugins, where each plugin can be enabled or
disabled (persisted to `plugin.json`; restart to apply).

## Running

```console
abioticeditor plugins run <operation> <save>                       # runs a save operation, keeps a .bak
abioticeditor plugins run <operation> <save> --param k=v --dry-run # preview without writing
abioticeditor <plugin-command> ...                                 # console commands are top-level verbs
```

In the app, run save operations and open UI/web tools from Settings, Manage Plugins; menu actions
also appear in the top-level **Plugins** menu.

## Checklist

- [ ] One public `IAbioticPlugin` with a parameterless constructor.
- [ ] .NET references use `Private="false" ExcludeAssets="runtime"`; output is your DLL plus `plugin.json`.
- [ ] `plugin.json` has a unique `id` and the correct `entryAssembly` or `entryScript`.
- [ ] Save operations mutate in place and call `MarkChanged()`; they never write files themselves.
- [ ] Console command names do not collide with built-ins (a collision is skipped with a warning).
- [ ] UI tools return a `Microsoft.Maui.Controls.View`; heavy reads are lazy and subscribers are disposed.
