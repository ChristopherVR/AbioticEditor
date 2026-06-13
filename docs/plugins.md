# Plugin system — architecture & rationale

The editor supports **plugins**: independently-built units that extend the app, the CLI, and
the core without being part of the product's own source tree. A plugin is either a compiled
**.NET assembly** (`.dll`) or a **JavaScript file** (`.js`, run on the bundled Jint engine).
Either kind can add:

- **Save operations** — scripted read/modify passes over a save (cheats, bulk edits, or
  *fixes* for fields a game patch changed). Runnable from the CLI (`plugins run`) and the
  GUI (the Plugins panel).
- **Console commands** — whole new CLI verbs (`abioticeditor <your-command>`), indistinguishable
  from built-ins.
- **Editor tools** — UI panels (dynamic MAUI component loading) hosted inside the GUI.
- **Web tools** — UI panels rendered as **HTML in a web view**, so a plugin can ship a
  **React** (or any web) front-end with a JavaScript↔host bridge.
- **Menu actions** — click-to-run menu items in the GUI (a top-level **Plugins** menu and the
  Plugins panel).
- **Event handlers** — code that runs when something happens (a save is opened or written, the
  app starts). This is the "trigger when X occurs" extension point.

One plugin may provide any mix of these.

> **Trust model up front:** .NET has no in-process sandbox. A loaded plugin runs with the
> same privileges as the editor. The system is built to make loading *deliberate and
> visible* (manifest gate, enable/disable, provenance in every list), **not** to contain
> malicious code. Only install plugins you trust. See [Security](#security--trust).

---

## Why this shape

The editor is three deliverables over one core (`AbioticEditor.Core`): a MAUI app, a CLI,
and the library itself. Three forces shaped the design:

1. **One contract, every host.** A plugin author should write a save operation *once* and
   have it work in the GUI and the CLI. That means the capability contracts cannot depend on
   MAUI or on `System.CommandLine`. They live in a tiny, host-agnostic SDK assembly
   (`AbioticEditor.Plugins.Abstractions`) that the app, the CLI, and plugins all reference.

2. **The dangerous part stays in the host.** Writing save bytes, keeping `.bak` backups, and
   honouring `--dry-run` are done by the host (`SaveOperationRunner`), never by plugin code.
   A plugin only *mutates an in-memory model and says "I changed something"*; the host
   decides whether and how to persist. Every operation — first-party or third-party — gets
   the same backup guarantee.

3. **Discovery must be cheap and safe.** Listing, validating, version-checking, and
   enabling/disabling plugins all happen by reading a `plugin.json` **without loading any
   code**. Untrusted assemblies are only loaded when a plugin is actually activated, and even
   then into an isolated context.

## Assembly layout

```
AbioticEditor.Plugins.Abstractions   (net10.0, no MAUI / no System.CommandLine)
    │   The SDK. Plugin authors reference this (+ UeSaveGame for raw save access).
    │   IAbioticPlugin, IPluginHost/Log/Registry, PluginManifest,
    │   ISaveOperation, IConsoleCommand, IEditorTool + their contexts.
    ▼
AbioticEditor.Core/Plugins/          (the host: discovery + loading + execution)
    PluginManager      two-phase discover → load, aggregates capabilities
    PluginManifestIo   parse/validate/persist plugin.json (never loads code)
    PluginLoadContext  isolated, collectible AssemblyLoadContext per plugin
    PluginHost/Log     IPluginHost impl (logging, data dir, versions)
    SaveOperationRunner  load → kind-check → execute → backup+write / dry-run
    SaveKindDetector   header-only save classification
    ▲                    ▲
    │                    │
AbioticEditor.Cli        AbioticEditor.App
  plugins list/info/run    PluginService (startup load)
  PluginCliBridge          PluginsPage (manage, run ops, open tools)
  (adapts IConsoleCommand    EditorToolContext (active-save window for tools)
   to System.CommandLine)
```

The sample plugins under `plugins/` reference the SDK and Core but ship **neither** — see
[the shared-assembly rule](#the-shared-assembly-rule).

## How a plugin is found and loaded

Two **roots** are scanned, user-first:

| Root | Path | For |
|------|------|-----|
| User | `%LOCALAPPDATA%\AbioticEditor\plugins` | plugins a user installs; survives app updates |
| Bundled | `<exe dir>\plugins` | plugins shipped next to the app/CLI |

Override both with the `ABIOTIC_PLUGINS_DIR` environment variable (path-separator-joined),
which is how tests and portable installs point at a custom location.

Each plugin is a **subfolder** containing a `plugin.json` and its DLL(s):

```
%LOCALAPPDATA%\AbioticEditor\plugins\
  max-skills\
    plugin.json
    MaxSkills.dll
```

Loading is **two-phase**:

1. **Discover** — read every `plugin.json`, validate it, dedupe by `id` (first root wins).
   No assemblies are loaded. This is what powers `plugins list` and the GUI list, and it is
   safe to run against an untrusted folder.
2. **Load** (only for enabled, version-compatible plugins) — create an isolated
   `PluginLoadContext`, load the entry assembly, find the single `IAbioticPlugin`, call
   `Configure(registry, host)`, and record the capabilities it registers. A plugin that
   throws here is marked **Failed** with its error; it never takes the host down.

The CLI additionally **skips loading** plugins whose manifest declares *only* an editor tool
(it can't host UI), and disables the whole step under `ABIOTIC_NO_PLUGINS=1`.

## Isolation & the shared-assembly rule

Each plugin loads into its own collectible `AssemblyLoadContext`, so two plugins can carry
different versions of the same helper library without colliding.

The one thing that must **not** be isolated is the shared contract surface. If a plugin
loaded its *own* copy of the SDK, its `ISaveOperation` would be a different `Type` than the
host's and every cast across the boundary would fail with a confusing
`InvalidCastException`. `PluginLoadContext` prevents this by resolving from the **host** (the
default load context) for:

- the editor contracts (`AbioticEditor.Plugins.Abstractions`, `AbioticEditor.Core`,
  `UeSaveGame`), and
- **anything the host already has loaded** (CUE4Parse, MAUI, the BCL, …).

Only genuinely plugin-private libraries load from the plugin's own folder. The practical
consequence for authors is the **shared-assembly rule**:

> Reference the SDK / Core / MAUI to compile, but do not ship them. Use
> `Private="false" ExcludeAssets="runtime"` on those references so your output is just *your*
> DLL + `plugin.json`. The host provides the shared assemblies at runtime and unifies the
> types.

All four sample `.csproj`s follow this rule and are the canonical examples.

## The manifest (`plugin.json`)

```json
{
  "id": "com.example.max-skills",   // globally-unique, stable; names the data dir
  "name": "Max Skills",
  "version": "1.0.0",
  "author": "You",
  "description": "Levels every skill on a player save.",
  "entryAssembly": "MaxSkills.dll", // bare file name in this folder (no paths)
  "minHostVersion": "1.0.0",        // refuse to load against an older SDK
  "capabilities": ["saveOperation"],// advisory hint for listing / CLI skip
  "enabled": true                   // user-togglable; persisted here
}
```

Validation is strict on the two fields the loader trusts (`id`, `entryAssembly` — which must
be a bare `.dll` file name, never a path, so a manifest can't aim the loader at an arbitrary
DLL) and lenient on the descriptive ones. `minHostVersion` turns "built against a newer SDK"
into an early, clear *"requires plugin SDK x but host provides y"* instead of a runtime
`MissingMethodException`.

## Capabilities

### Save operation (`ISaveOperation`)
`AppliesTo` a `SaveKind` (Player/World/Metadata/Customization/Any); declares optional
`Parameters`; implements `ExecuteAsync(context)`. The context exposes the loaded `SaveGame`
to mutate, resolved parameters, an `IsDryRun` flag, and `MarkChanged()`. The host runs it via
`SaveOperationRunner`, which loads the save, enforces the kind match and required parameters,
executes, and **only if the operation marked a change and it isn't a dry run** backs up and
rewrites the file. Identical behaviour in the CLI and the GUI.

### Console command (`IConsoleCommand`)
Declares `Name`, `Description`, framework-neutral `Arguments`/`Options`, and
`InvokeAsync(context)` returning an exit code. The CLI's `PluginCliBridge` turns this into a
real `System.CommandLine` subcommand (with help and the standard 0/1/2 exit codes). A name
that collides with a built-in (or another plugin) is **skipped with a warning** — a plugin
can never shadow a shipped command.

### Editor tool (`IEditorTool`)
A UI panel for the GUI. `CreateView(context)` returns a `Microsoft.Maui.Controls.View` typed
as `object` — **that single seam is why the SDK has no MAUI dependency**: the plugin (not the
SDK) takes the MAUI reference, and the GUI casts the result back. The context gives a
read-only window onto the open save (`ActiveSave`, lazily loaded) and an `ActiveSaveChanged`
event so a tool refreshes when the user switches files. When the panel closes the host
disposes the context (severing `ActiveSaveChanged` subscribers) and the view/its
`BindingContext` if `IDisposable`, so a subscribed view-model can't outlive its panel; a
view-model that subscribes should implement `IDisposable` and unsubscribe. The CLI ignores
editor tools.

### Web tool (`IWebTool`) — HTML/React UIs
A UI whose front-end is **HTML** rendered in a MAUI `WebView`, rather than native controls. This
is how a plugin ships a **React** (or Vue/Svelte/plain-HTML) interface, and it pairs especially
well with JavaScript plugins. `CreateContent(ctx)` returns either inline HTML
(`WebToolContent.FromHtml`, e.g. a page that pulls React from a CDN) or a directory of static
assets (`WebToolContent.FromDirectory`, e.g. a production SPA build; a relative path resolves
against the plugin's own folder). The SDK never references a web-view type — content moves out as
HTML, messages move across the bridge as strings — so the contract stays host-agnostic.

**The bridge.** The host (`WebToolHostPage`) injects a small script giving the page
`abiotic.request(obj)` (returns a Promise), `abiotic.log(msg)`, and `abiotic.onEvent(fn)`. It uses
custom-scheme navigation, which needs no platform-specific message channel: page JS sets
`location.href` to an `abiotic://request?...` URL; the host intercepts it in the web view's
`Navigating` event (cancelling the real navigation), routes the payload to
`IWebTool.HandleMessageAsync`, and resolves the page's Promise by evaluating
`window.abiotic.__resolve(id, reply)`. So a React component can `await abiotic.request({type:'…'})`
and the plugin answers from C#/JS (e.g. reading the open save). Editing should still go through an
`ISaveOperation` (the page asks the host to run one) so writes keep the backup path. JavaScript
plugins register web tools with `abiotic.registerWebTool({ id, title, html | rootDirectory,
handleMessage })`; the message handler gets a `ctx` with `activeSavePath`, `activeSaveKind`, and a
ready-made `ctx.playerSummaryJson()`. A directory `rootDirectory` may be relative — it resolves
against the plugin's folder, so a plugin can ship a built SPA (e.g. a Vite + React `dist/`) and
point at it. The `ReactAppDashboard` sample is exactly that: a real `npm`/Vite project built to a
single self-contained `index.html` (via `vite-plugin-singlefile` + `base: "./"`, so it works from a
`file://` URL).

### Driving the app — the host-UI bridge (`IHostUi` / `abiotic.ui`)
Plugins can interact with the app, not just read it. `IPluginHost.Ui` (an `IHostUi`) exposes
`ShowAlertAsync`/`ConfirmAsync`/`ToastAsync`, `RunSaveOperationAsync(id)` (runs a registered
operation against the open save through the backup/write path, then reloads), `ReloadOpenSaveAsync`,
and `OpenSettingsAsync`/`OpenPluginsPanelAsync`. The GUI implements it (`AppHostUi`, marshalling
each call onto the UI thread); the CLI and tests get `NullHostUi`, which no-ops, so plugin code
stays portable. JavaScript plugins reach it as `abiotic.ui` (e.g. `abiotic.ui.toast("hi")`,
`abiotic.ui.runSaveOperation("max-skills")`) — fire-and-forget, since the Jint engine is
synchronous. A web tool's React UI can therefore trigger real editor actions by asking its
`handleMessage` to call `abiotic.ui.*`.

### Menu action (`IMenuAction`)
A single click-to-run command surfaced in the GUI as a menu item (a top-level **Plugins** menu
built in `MainPage`) and as a button in the Plugins panel. `InvokeAsync(context)` gets the open
save's path/kind and a `NotifyAsync` to show the user a message. No MAUI dependency, so managed
and JavaScript plugins register them the same way. The CLI ignores menu actions.

### Event handler (events)
`registry.AddEventHandler(eventName, handler)` subscribes to a host event. The host raises
named events through `PluginManager.RaiseEvent`, which snapshots the matching handlers and
invokes each in isolation (a throwing handler is logged, never fatal, and can't block the
others or the action that fired the event). Well-known events (`PluginEvents`):

| Event | Raised by | Payload |
|---|---|---|
| `app.started` | GUI startup, after plugins load | — |
| `save.opened` | GUI, after a save parses | `savePath`, `saveKind` |
| `save.closed` | GUI, when the editor clears | — |
| `save.written` | CLI/GUI, after a plugin save operation writes | `savePath`, `saveKind`, `operationId` |

The hosts choose where to raise; Core supplies the dispatch. A plugin event handler is the
mechanism behind "do X automatically whenever Y happens."

## JavaScript plugins

A plugin whose manifest sets `"runtime": "javascript"` and `"entryScript": "plugin.js"` is run
on **Jint**, a pure-managed ECMAScript interpreter bundled in Core. Pure-managed matters: it
works on every target (desktop, mobile, CLI) with no native dependency, unlike V8-based
engines. A JavaScript plugin needs **no build step** — it is just a `.js` file next to a
`plugin.json`.

`JavaScriptPlugin` adapts a script into the same `IAbioticPlugin` the managed path produces, so
both kinds load, list, enable/disable, and run identically. The script runs once at load and
registers capabilities through the injected `abiotic` host object:

```javascript
abiotic.registerSaveOperation({ id, displayName, appliesTo, parameters, execute(ctx) { ... } });
abiotic.registerCommand({ name, description, options, invoke(ctx) { ... } });
abiotic.registerMenuAction({ id, title, glyph, invoke(ctx) { ... } });
abiotic.on(eventName, function (event) { ... });
abiotic.log.info("..."); abiotic.log.warn("..."); abiotic.log.error("...");
```

Each registered JS callback is wrapped by a CLR adapter (`JsSaveOperation`/`JsConsoleCommand`/
`JsMenuAction`/event delegate) that builds a JS-friendly context facade and invokes the
function. Member access is case-insensitive, so scripts use natural camelCase (`ctx.markChanged()`
maps to the CLR `MarkChanged`). Save operations get `ctx.player` — a focused facade
(`money`, `setAllSkillLevels(n)`, …) that edits through the host writer and marks the context
changed so the runner persists it.

**Constraints & safety.** The engine is bounded (recursion depth, a wall-clock timeout, a
statement cap) and CLR reflection is not exposed, so a runaway or malformed script is contained
and reported as a failed plugin. The Jint engine is single-threaded, so all callback invocations
funnel through one lock (`JsRuntime`). As with managed plugins, JS still runs in-process and is
**not** a security boundary.

## Security & trust

- **No sandbox.** Loaded plugins run fully trusted. The defences are *procedural*: a manifest
  must exist and validate before any code loads; plugins are listed with provenance and a
  load state; each can be disabled; the entry assembly can't be a path. None of this contains
  hostile code — it makes loading deliberate.
- **Surface the trust decision.** Both the CLI (`plugins list`) and the GUI (Settings →
  Manage Plugins) state that plugins run with full trust and show where they came from.
- **Writes are still safe-by-construction.** Even a buggy operation can't lose data silently:
  the host keeps a `.bak` before every write, and a dry run never touches the file.
- **Failures are contained.** A plugin that throws on load or during an operation is reported,
  not fatal; the editor and its built-in features keep working.

## Authoring

See [`plugin-authoring.md`](plugin-authoring.md) for a step-by-step guide and the four
worked samples under `plugins/`:

| Sample | Runtime | Capability | Notes |
|--------|---------|-----------|-------|
| `MaxSkills` | .NET | save operation | headless; player saves; `--param level` |
| `SaveStats` | .NET | console command | headless; adds `save-stats <save> [--json]` |
| `PlaytimeDashboard` | .NET / MAUI | editor tool | MAUI view built in C# |
| `SaveInspector` | .NET / MAUI | editor tool | full MVVM: compiled **XAML** + view-model |
| `HelloScript` | **JavaScript** | save op + command + menu action + event handler | no build step |
| `ReactDashboard` | **JavaScript** | web tool | **React** UI (CDN) over the host bridge |
| `ReactAppDashboard` | **JavaScript** | web tool + save op | **full Vite + React app** that drives the app via `abiotic.ui` |
| `WebStats` | **JavaScript** | web tool | **offline HTML** served from a bundled `web/` folder |
