# Building Plugins

This page walks through writing and building a plugin. For installing and running one, see
[Adding Plugins](Adding-Plugins); for the full type-by-type reference, see
[Plugin API Reference](Plugin-API-Reference).

A plugin is either:

- a **.NET assembly** that references the SDK (`AbioticEditor.Plugins.Abstractions`) and ships as a
  `.dll`, or
- a **JavaScript file** run on the bundled Jint engine, with no build step.

Both load, list, enable/disable, and run identically.

## The entry point

Every .NET plugin exposes exactly one public, parameterless type implementing `IAbioticPlugin`. The
host instantiates it and calls `Configure` once at load time, where you register capabilities:

```csharp
using AbioticEditor.Plugins;

public sealed class HelloPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("hello from a plugin");
        registry.AddSaveOperation(new MaxSkillsOperation());
        // registry also has: AddConsoleCommand, AddEditorTool, AddWebTool,
        //                     AddMenuAction, AddSaveUpgrader, AddEventHandler
    }
}
```

Keep `Configure` cheap: no heavy work and no UI. One plugin may register any mix of capabilities.

## The project file: the shared-assembly rule

Reference the SDK (and Core/MAUI if you use them) to **compile**, but do **not** ship them. The host
provides them at runtime and unifies the types. If you ship your own copy of a shared assembly,
casts across the host boundary fail with a confusing `InvalidCastException`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- Private=false + ExcludeAssets=runtime: compile against it, do not copy it. -->
    <ProjectReference Include="...\AbioticEditor.Plugins.Abstractions.csproj"
                      Private="false" ExcludeAssets="runtime" />
    <!-- Optional: Core gives you the typed catalogs and readers/writers. -->
    <ProjectReference Include="...\AbioticEditor.Core.csproj"
                      Private="false" ExcludeAssets="runtime" />
  </ItemGroup>

  <ItemGroup>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Your output is then just your DLL plus `plugin.json`.

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

`id` must be globally unique and stable (it names your data directory). `entryAssembly` is a bare
file name in the folder (never a path). `runtime` is `"dotnet"` or `"javascript"`; a JavaScript
plugin uses `entryScript` instead of `entryAssembly`. `capabilities` is an advisory hint;
`minHostVersion` turns "built against a newer SDK" into a clear up-front error.

## A save operation

The most common capability: read and modify one save. Mutate the loaded save in place, then call
`MarkChanged()`. The host owns persistence, the `.bak` backup, and the dry-run gate; you never write
the file yourself.

```csharp
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

        var data = PlayerSaveReader.ReadFrom(ctx.Save);   // data.Raw IS ctx.Save
        var updated = data.Skills.Select(s => s.Xp < targetXp ? s with { Xp = targetXp } : s).ToList();
        var changed = updated.Zip(data.Skills).Count(p => p.First.Xp != p.Second.Xp);
        if (changed == 0)
            return Task.FromResult(SaveOperationResult.NoChange("already maxed."));

        PlayerSaveWriter.ApplySkills(data, updated);
        ctx.MarkChanged();                                // without this, the host writes nothing
        return Task.FromResult(SaveOperationResult.Ok($"raised {changed} skills", changed));
    }
}
```

Rules of the road:

- **Mutate `ctx.Save` in place, then call `ctx.MarkChanged()`.** No mark, no write (and no backup),
  which keeps a "nothing to do" run clean.
- **Never write the file yourself.** Honour `ctx.IsDryRun` by doing your full computation regardless,
  so the reported `ChangeCount` is honest, but let the host decide whether to persist.
- You can skip Core and edit `ctx.Save.Properties` (raw `FPropertyTag`s) directly if you only want a
  `UeSaveGame` dependency. That is how a forward-compatible fix-up edits fields the editor has no
  model for (see the `GrantFlag` sample).

## Other capabilities (briefly)

- **Console command** (`IConsoleCommand`): a CLI verb. Declare `Name`, framework-neutral
  `Arguments`/`Options`, and `InvokeAsync` returning an exit code (0/1/2). Write to `ctx.Out`. A name
  that collides with a built-in is skipped with a warning.
- **Editor tool** (`IEditorTool`): a native MAUI panel. `CreateView(ctx)` returns a
  `Microsoft.Maui.Controls.View` as `object`. Read `ctx.ActiveSave` (loaded lazily) and subscribe to
  `ctx.ActiveSaveChanged`. A subscribing view-model should implement `IDisposable` so it does not
  outlive its panel.
- **Web tool** (`IWebTool`): an HTML/React UI in a web view. Return `WebToolContent.FromHtml(...)` or
  `WebToolContent.FromDirectory(...)`. The page calls `abiotic.request(obj)` (a Promise) and your
  `HandleMessageAsync` answers.
- **Menu action** (`IMenuAction`): a click-to-run item. `InvokeAsync(ctx)` with `ctx.NotifyAsync`.
- **Event handler**: `registry.AddEventHandler(PluginEvents.SaveWritten, e => ...)`. Events:
  `app.started`, `save.opened`, `save.closed`, `save.written`.
- **Save upgrader** (`ISaveUpgrader`): recover a save the host cannot load. `CanUpgrade(probe)` is a
  cheap header-only claim; `UpgradeAsync` returns corrected bytes.

To **edit from a UI or web tool**, register an `ISaveOperation` and run it through the host's
backup/write path rather than mutating the save from the panel.

## A JavaScript plugin (no build step)

Set `"runtime": "javascript"` and `"entryScript": "plugin.js"`, and register capabilities on the
injected `abiotic` host object:

```javascript
abiotic.registerSaveOperation({
    id: "rich-player",
    displayName: "Rich",
    appliesTo: "Player",
    parameters: [ { name: "money", default: "9999" } ],
    execute: function (ctx) {
        ctx.player.money = parseInt(ctx.getParameter("money", "9999"), 10);
        ctx.player.setAllSkillLevels(12);
        return { message: "done", changeCount: 1 };   // or a string, or nothing
    }
});

abiotic.registerCommand({
    name: "js-greet",
    invoke: function (ctx) { ctx.print("Hi, " + ctx.getOption("name", "you")); return 0; }
});

abiotic.registerMenuAction({ id: "hi", title: "Say hi (JS)", invoke: function (ctx) { ctx.notify("hi"); } });
abiotic.on("save.written", function (e) { abiotic.log.info("saw " + e.name + " " + e.savePath); });
```

For player save operations, `ctx.player` is a focused facade: `money` (get/set),
`setAllSkillLevels(n)`, `skillCount`, `recipeCount`. The engine is single-threaded and bounded
(recursion depth, a wall-clock timeout, a statement cap), and member access is case-insensitive, so
natural camelCase works. JavaScript plugins can also drive the app through `abiotic.ui`
(`toast`, `runSaveOperation`, `reloadSave`, and so on), and register web tools with
`abiotic.registerWebTool({ id, title, html | rootDirectory, handleMessage })`.

## Building

```console
# headless .NET plugin
dotnet build plugins/MaxSkills -c Release

# MAUI (UI) plugin: needs the MAUI workload and a target framework moniker
dotnet workload install maui
dotnet build plugins/SaveInspector -c Release -f net10.0-windows10.0.19041.0
```

JavaScript plugins need no build. A plugin that bundles a built web UI (like `ReactAppDashboard`)
builds that UI with its own toolchain (`npm run build`); the plugin itself still needs no .NET build.

## Installing and verifying

Copy your output folder (DLL/JS plus `plugin.json` and any assets) into
`%LOCALAPPDATA%\AbioticEditor\plugins\<your-plugin>\`, or point `ABIOTIC_PLUGINS_DIR` at your build
output during development:

```powershell
$env:ABIOTIC_PLUGINS_DIR = "D:\path\to\plugins\MaxSkills\bin\Release\net10.0"
abioticeditor plugins list
abioticeditor plugins info com.example.max-skills
```

`plugins list` / `plugins info` show whether your plugin loaded and what it registered; a load
failure appears there with its error.

## Host services

Reachable from `Configure` and every capability context (`host` / `ctx.Host`):

| Member | Use |
|---|---|
| `Log` | `Info`/`Warn`/`Error`, routed to the editor log and tagged with your id |
| `DataDirectory` | a writable, per-plugin folder for settings and caches |
| `HostKind` | `"gui"` or `"cli"`, to tailor behavior without a framework reference |
| `Ui` | the host-UI bridge (alerts, toasts, run an operation); a no-op in the CLI and tests |
| `HostVersion` / `SdkVersion` | version checks at runtime |

## Checklist

- [ ] One public `IAbioticPlugin` with a parameterless constructor.
- [ ] References use `Private="false" ExcludeAssets="runtime"`; output is your DLL plus `plugin.json`.
- [ ] `plugin.json` has a unique `id` and the correct `entryAssembly` or `entryScript`.
- [ ] Save operations mutate in place and call `MarkChanged()`; they never write files.
- [ ] Console command names do not collide with built-ins.
- [ ] UI tools return a `Microsoft.Maui.Controls.View`; heavy reads are lazy and subscribers are disposed.
