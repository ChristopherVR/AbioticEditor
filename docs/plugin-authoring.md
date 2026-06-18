# Writing a plugin

A plugin is a normal .NET class library that references **`AbioticEditor.Plugins.Abstractions`**
(the SDK) and ships as a `.dll` next to a `plugin.json`. This guide builds each of the three
capability kinds. For the design and the *why*, see [`plugins.md`](plugins.md).

## 1. The smallest plugin

```csharp
using AbioticEditor.Plugins;

public sealed class HelloPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("hello from a plugin");
        // register capabilities here
    }
}
```

The host scans your entry assembly for the **single** public, parameterless type implementing
`IAbioticPlugin`, instantiates it, and calls `Configure` once. Register your capabilities on
`registry`; keep `Configure` cheap (no heavy work, no UI).

## 2. The project file: the shared-assembly rule

Reference the SDK (and Core/MAUI if you use them) to **compile**, but don't **ship** them:
the host provides them at runtime and unifies the types (see
[isolation](plugins.md#isolation--the-shared-assembly-rule)). Output stays just your DLL +
`plugin.json`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- Private=false + ExcludeAssets=runtime: compile against it, don't copy it. -->
    <ProjectReference Include="...\AbioticEditor.Plugins.Abstractions.csproj"
                      Private="false" ExcludeAssets="runtime" />
    <!-- Optional: Core gives you the typed catalogs + readers/writers. -->
    <ProjectReference Include="...\AbioticEditor.Core.csproj"
                      Private="false" ExcludeAssets="runtime" />
  </ItemGroup>

  <ItemGroup>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

> Outside this repo you'd reference NuGet packages of the SDK/Core instead of project paths,
> but the `Private="false" ExcludeAssets="runtime"` rule is the same.

## 3. The manifest

```json
{
  "id": "com.example.hello",
  "name": "Hello",
  "version": "1.0.0",
  "author": "You",
  "description": "What it does, in one line.",
  "entryAssembly": "Hello.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation"],
  "enabled": true
}
```

`id` must be globally unique and stable (it names your plugin's data directory and is how
every host refers to you). `entryAssembly` is a bare file name in the plugin folder.
`capabilities` is an advisory hint (`saveOperation`, `consoleCommand`, `editorTool`).

## 4. A save operation (edit a save)

```csharp
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

public sealed class MaxSkillsOperation : ISaveOperation
{
    public string Id => "max-skills";
    public string DisplayName => "Max Skills";
    public string Description => "Raise every skill to a target level.";
    public SaveKind AppliesTo => SaveKind.Player;

    public IReadOnlyList<SaveOperationParameter> Parameters { get; } = new[]
    {
        new SaveOperationParameter("level", "Target level 1-20", DefaultValue: "10"),
    };

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext ctx, CancellationToken ct = default)
    {
        var level = Math.Clamp(int.Parse(ctx.GetParameter("level", "10")), 1, SkillCatalog.MaxLevel);
        var targetXp = SkillCatalog.XpForLevel(level);

        // data.Raw IS ctx.Save, so the writer mutates the very instance the host persists.
        var data = PlayerSaveReader.ReadFrom(ctx.Save);
        var updated = data.Skills.Select(s => s.Xp < targetXp ? s with { Xp = targetXp } : s).ToList();
        var changed = updated.Zip(data.Skills).Count(p => p.First.Xp != p.Second.Xp);
        if (changed == 0)
            return Task.FromResult(SaveOperationResult.NoChange("already maxed."));

        PlayerSaveWriter.ApplySkills(data, updated);
        ctx.MarkChanged();                     // <-- without this the host writes nothing
        return Task.FromResult(SaveOperationResult.Ok($"raised {changed} skills", changed));
    }
}
```

Rules of the road:

- **Mutate `ctx.Save` in place**, then call `ctx.MarkChanged()`. If you don't mark a change,
  the host skips the write *and* the backup, which is how a "nothing to do" run stays clean.
- **Never write the file yourself.** The host owns persistence, `.bak` backups, and the
  dry-run gate (`ctx.IsDryRun`). Do your full computation regardless so the reported
  `ChangeCount` is honest.
- **Don't lower over-cap values** unless that's the point; respect what's already there.
- You can drop Core entirely and manipulate `ctx.Save.Properties` (raw `FPropertyTag`s) if you
  only want a `UeSaveGame` dependency; Core just gives you the typed catalogs and readers.

Run it:

```
abioticeditor plugins run max-skills path\to\Player_x.sav --param level=15
abioticeditor plugins run max-skills path\to\Player_x.sav --dry-run
```

…or from the GUI: **Settings → Manage Plugins → SAVE OPERATIONS → RUN** (against the open
save; the editor reloads afterward).

## 5. A console command (new CLI verb)

```csharp
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;

public sealed class SaveStatsCommand : IConsoleCommand
{
    public string Name => "save-stats";
    public string Description => "Print a quick summary of a save.";
    public IReadOnlyList<PluginCommandArgument> Arguments { get; } = new[]
        { new PluginCommandArgument("save", "Path to the .sav") };
    public IReadOnlyList<PluginCommandOption> Options { get; } = new[]
        { new PluginCommandOption("json", "Emit JSON", IsFlag: true) };

    public Task<int> InvokeAsync(IConsoleCommandContext ctx, CancellationToken ct = default)
    {
        var path = ctx.RequireArgument("save");
        ctx.Out.WriteLine(ctx.GetFlag("json") ? "{ ... }" : "summary ...");
        return Task.FromResult(0);            // 0 ok, 1 user error, 2 unexpected
    }
}
```

Register it with `registry.AddConsoleCommand(new SaveStatsCommand())`. It then appears in
`abioticeditor --help` and runs as `abioticeditor save-stats <save> [--json]`. Write to
`ctx.Out`/`ctx.Error` (abstract writers, so the command is unit-testable with a `StringWriter`).

## 6. An editor tool (UI panel)

A UI plugin takes a MAUI dependency (the SDK does not). Multi-target the MAUI heads the app
targets and reference `Microsoft.Maui.Controls` with `ExcludeAssets="runtime"`.

```csharp
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;
using Microsoft.Maui.Controls;

public sealed class DashboardTool : IEditorTool
{
    public string Id => "dashboard";
    public string Title => "Dashboard";
    public string Glyph => "📊";

    public object CreateView(IEditorToolContext ctx)   // returns a MAUI View as object
    {
        var label = new Label();
        void Refresh() => label.Text = ctx.ActiveSave is null
            ? "No save open."
            : $"{ctx.ActiveSaveKind}: {ctx.ActiveSave.Properties?.Count} properties";
        ctx.ActiveSaveChanged += (_, _) => Refresh();
        Refresh();
        return new ScrollView { Content = label };
    }
}
```

Register with `registry.AddEditorTool(new DashboardTool())`. The GUI lists it under **Manage
Plugins → TOOLS** and hosts the view you return. `ctx.ActiveSave` is loaded lazily (no parse
cost until you read it) and `ActiveSaveChanged` fires when the user switches files.

**Lifetime / cleanup.** When the tool panel closes the host disposes the context (which
severs every `ActiveSaveChanged` subscription) and disposes the view and its `BindingContext`
if they implement `IDisposable`. So if your view-model subscribes to `ActiveSaveChanged`,
implement `IDisposable` and unsubscribe in `Dispose()`. This keeps the view-model (and any
save it parsed) from outliving its panel:

```csharp
public sealed class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorToolContext _ctx;
    public DashboardViewModel(IEditorToolContext ctx) { _ctx = ctx; _ctx.ActiveSaveChanged += OnChanged; }
    private void OnChanged(object? s, EventArgs e) => Refresh();
    public void Dispose() => _ctx.ActiveSaveChanged -= OnChanged;
}
```

**Full MVVM with XAML** is supported; see the `SaveInspector` sample: a compiled
`ContentView` (`InspectorView.xaml`, `x:DataType` for compiled bindings) bound to an
`InspectorViewModel` (`INotifyPropertyChanged`, `IDisposable`, a `Command`, an
`ObservableCollection`). The tool just returns `new InspectorView(new InspectorViewModel(ctx))`.

> A UI tool is read-only by contract. To *edit* from a panel, register an `ISaveOperation`
> and run it through the host's backup/write path rather than mutating `ActiveSave` directly.

## 7. A menu action (click-to-run menu item)

```csharp
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;

public sealed class HelloAction : IMenuAction
{
    public string Id => "say-hi";
    public string Title => "Say hello";
    public string? Glyph => "👋";

    public async Task InvokeAsync(IMenuActionContext ctx, CancellationToken ct = default)
        => await ctx.NotifyAsync($"Hello! Open save: {ctx.ActiveSavePath ?? "(none)"}");
}
```

Register with `registry.AddMenuAction(new HelloAction())`. It appears in the GUI's **Plugins**
menu and the Plugins panel. `NotifyAsync` shows the user a message without a UI dependency.

## 8. An event handler (trigger when something happens)

```csharp
using AbioticEditor.Plugins.Events;

public void Configure(IPluginRegistry registry, IPluginHost host)
{
    registry.AddEventHandler(PluginEvents.SaveWritten, e =>
        host.Log.Info($"a plugin wrote {e.SavePath} ({e.Name})"));
}
```

Handlers run when the host raises that event (see the table in [`plugins.md`](plugins.md#event-handler-events)
for the names and payloads). Keep them quick and non-throwing: the host logs and isolates
failures, but a slow handler stalls the action that fired it.

## 8b. A localization (translate the UI)

A plugin can add a language or override individual UI strings. Contribute them in `Configure`:

```csharp
registry.AddLocalization("it", new Dictionary<string, string>
{
    ["Common_Save"] = "SALVA",
    ["Header_OpenFolder"] = "APRI CARTELLA",
});
```

Or ship a **pure-data pack** with no code at all: set `"runtime": "localization"` and point the
manifest's `localizations` map at a `.resx`/`.json` file per culture. JavaScript plugins use
`abiotic.addLocalization(culture, { Key: "text" })`. Full guide, key conventions, and the
resolution order: [Localization](localization.md).

## 9. A JavaScript plugin (no build step)

Set `"runtime": "javascript"` and `"entryScript": "plugin.js"` in the manifest, and write a
`.js` file that registers capabilities on the `abiotic` host object. No project, no compile,
just two files in a folder.

```javascript
// plugin.js
abiotic.registerSaveOperation({
    id: "rich-player",
    displayName: "Rich",
    appliesTo: "Player",
    parameters: [ { name: "money", default: "9999" } ],
    execute: function (ctx) {
        ctx.player.money = parseInt(ctx.getParameter("money", "9999"), 10);
        return { message: "done", changeCount: 1 };  // or a string, or nothing
    }
});

abiotic.registerCommand({
    name: "js-greet",
    description: "Greet from JS",
    options: [ { name: "name", description: "who", isFlag: false } ],
    invoke: function (ctx) { ctx.print("Hi, " + ctx.getOption("name", "you")); return 0; }
});

abiotic.registerMenuAction({ id: "hi", title: "Say hi (JS)", invoke: function (ctx) { ctx.notify("hi"); } });

abiotic.on("save.written", function (e) { abiotic.log.info("saw " + e.name + " " + e.savePath); });
```

The `abiotic` API: `log.info/warn/error`, `registerSaveOperation`, `registerCommand`,
`registerMenuAction`, `on(eventName, handler)`, plus `hostKind` and `dataDirectory`. Member
access is case-insensitive, so use natural camelCase. Save operations get `ctx.player` for
player saves (`money` get/set, `setAllSkillLevels(n)`, `skillCount`, `recipeCount`); call
`ctx.markChanged()` (or let `ctx.player` do it) so the host persists. `console.log/warn/error`
are also available. The engine is sandboxed-by-constraint (no CLR reflection, bounded
recursion/time) but, like managed plugins, is not a hard security boundary. See the
`HelloScript` sample.

## 9b. A web tool (HTML / React UI)

A plugin can render its UI as a web page in a `WebView` instead of native controls, which is
ideal for shipping a **React** (or any web) front-end. The page talks to the plugin over a bridge.

JavaScript (inline HTML pulling React from a CDN, no build step):

```javascript
var HTML = `<!doctype html><html><head>
  <script crossorigin="anonymous" src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
  <script crossorigin="anonymous" src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
  <script src="https://unpkg.com/@babel/standalone/babel.min.js"></script>
</head><body><div id="root"></div>
<script type="text/babel">
  function App() {
    const [d, setD] = React.useState(null);
    React.useEffect(() => { abiotic.request({ type: "playerSummary" }).then(setD); }, []);
    return <pre>{JSON.stringify(d, null, 2)}</pre>;
  }
  ReactDOM.createRoot(document.getElementById("root")).render(<App/>);
</script></body></html>`;

abiotic.registerWebTool({
    id: "dash", title: "Dashboard", glyph: "⚛️",
    html: HTML,
    handleMessage: function (message, ctx) {
        var req = JSON.parse(message);
        if (req.type === "playerSummary") return ctx.playerSummaryJson();
        return "{}";
    }
});
```

The page gets, from the host-injected bridge:
- `abiotic.request(obj)` → a Promise resolved by your `handleMessage(message, ctx)` (the message
  is the JSON of `obj`); return a string (usually JSON).
- `abiotic.log(msg)` and `abiotic.onEvent(fn)`.

`ctx` in `handleMessage` exposes `activeSavePath`, `activeSaveKind`, and `playerSummaryJson()`.
To **edit** from a web tool, have the page request an action and run an `ISaveOperation` so the
write keeps its backup; don't mutate the save from the page.

**Offline / bundled apps.** Instead of inline HTML, serve a folder (a production SPA build or a
plain offline page): set `rootDirectory` (relative paths resolve against the plugin folder) and
`entryFile`. See the `WebStats` sample (`web/index.html`, no CDN). For a managed plugin implement
`IWebTool` and return `WebToolContent.FromHtml(...)` or `WebToolContent.FromDirectory(dir)`.

> CDN loads need internet at runtime and, for shipping plugins, should bundle the assets or pin
> Subresource Integrity hashes.

**A full Vite + React app.** The `ReactAppDashboard` sample is a real `npm`/Vite project (under
`app/`) with `package.json`, JSX, and components, built to a single self-contained file the
plugin serves. Two Vite settings make it work inside the editor's `file://` WebView:
`base: "./"` (relative asset URLs) and `vite-plugin-singlefile` (inline all JS/CSS, so there are no
ES-module `<script>` requests the `file://` origin would block). Build with
`cd app && npm install && npm run build`; the plugin's `rootDirectory: "app/dist"` then serves it.

## 9c. Driving the app from a plugin (`abiotic.ui` / `IHostUi`)

Plugins can interact with the app, not just read saves. Every host gives `host.Ui` (an `IHostUi`);
JavaScript plugins reach it as `abiotic.ui`:

```javascript
abiotic.ui.showAlert("Title", "Message");
abiotic.ui.toast("Saved!");
abiotic.ui.runSaveOperation("max-skills");   // runs a registered op on the open save, then reloads
abiotic.ui.reloadSave();
abiotic.ui.openSettings();
abiotic.ui.openPlugins();
```

These are fire-and-forget from JavaScript (the Jint engine is synchronous; the GUI marshals each
onto the UI thread). In the CLI and tests `host.Ui` is a no-op, so the same plugin runs everywhere.
A **web tool's React UI** drives the app by having its `handleMessage` call `abiotic.ui.*`. For
example, the React "Max skills" button does `abiotic.request({type:"runOperation"})`, and the plugin's
handler calls `abiotic.ui.runSaveOperation(...)`, so a click in the web UI performs a real,
backed-up edit. See `ReactAppDashboard`.

## 10. Build, install, test

```bash
# headless plugin
dotnet build plugins/MaxSkills -c Release

# UI plugin (needs the MAUI workload; build the head you run on)
dotnet build plugins/SaveInspector -c Release -f net10.0-windows10.0.19041.0
```

Install by copying the build output's **DLL + plugin.json** into a folder under
`%LOCALAPPDATA%\AbioticEditor\plugins\<your-plugin>\`. For development, point the editor at
your build output directly:

```bash
# Windows PowerShell
$env:ABIOTIC_PLUGINS_DIR = "D:\path\to\plugins\MaxSkills\bin\Debug\net10.0"
abioticeditor plugins list
```

`abioticeditor plugins list` / `plugins info <id>` show whether your plugin loaded and what it
registered; a load failure appears there with its error. `ABIOTIC_NO_PLUGINS=1` disables all
plugin loading.

## Host services (`IPluginHost`)

Reachable from `Configure` and every capability context:

| Member | Use |
|--------|-----|
| `Log` | `Info/Warn/Error` → the editor log, tagged with your id |
| `DataDirectory` | a writable, per-plugin folder for settings/caches |
| `HostKind` | `"gui"` or `"cli"`, to tailor behaviour without a framework reference |
| `HostVersion` / `SdkVersion` | version checks at runtime |

## Checklist

- [ ] One public `IAbioticPlugin` with a parameterless constructor.
- [ ] References use `Private="false" ExcludeAssets="runtime"`; output is DLL + `plugin.json`.
- [ ] `plugin.json` has a unique `id` and the correct `entryAssembly` file name.
- [ ] Save operations mutate in place and call `MarkChanged()`; they never write files.
- [ ] Console command names don't collide with built-ins (they'd be skipped).
- [ ] UI tools return a `Microsoft.Maui.Controls.View`; heavy reads are lazy.
