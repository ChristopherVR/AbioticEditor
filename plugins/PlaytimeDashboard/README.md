# PlaytimeDashboard - sample plugin

A managed (.NET / MAUI) **editor tool**: a read-only UI panel that shows headline numbers for the
open save and refreshes when you switch files. It builds its MAUI view entirely in C# (no XAML),
which makes it the simplest UI-tool example.

- **Runtime:** .NET / MAUI (`PlaytimeDashboard.dll`)
- **Capability:** editor tool `playtime-dashboard`
- **Surfaced in:** the app's Plugins panel only (the CLI ignores editor tools)

## What it does

Adds a "Dashboard" panel that displays, for the open save: file name, save kind, save class, and
property count, and for player saves also money, skill count, recipe count, and trait count. It is
read-only - it never edits the save - and it live-updates when you open a different file.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.playtime-dashboard",
  "name": "Playtime Dashboard",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Adds a 'Dashboard' UI tool to the Plugins panel that shows headline numbers for the open save.",
  "entryAssembly": "PlaytimeDashboard.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["editorTool"],
  "enabled": true
}
```

## How it works

`PlaytimeDashboardPlugin.Configure` registers a `DashboardTool : IEditorTool` (id
`playtime-dashboard`, title "Dashboard", glyph 📊, placement `Panel`). Its `CreateView(ctx)` builds
a `ScrollView` of labels in code and returns it as `object` (the GUI casts it back to a
`Microsoft.Maui.Controls.View`). That single seam is why the SDK itself takes no MAUI dependency:
the plugin references MAUI, the SDK does not.

The view reads `ctx.ActiveSave` (loaded lazily, so there is no parse cost until you read it) and
subscribes to `ctx.ActiveSaveChanged` to refresh on a file switch. When the panel closes the host
disposes the context, which severs that subscription.

> An editor tool is read-only by contract. To edit from a panel, register an `ISaveOperation` and
> run it through the host's backup/write path rather than mutating `ActiveSave` directly.

## Build

This is a MAUI plugin, so it needs the MAUI workload (`dotnet workload install maui`) and a target
framework moniker for the head you run on:

```console
dotnet build plugins/PlaytimeDashboard -c Release -f net10.0-windows10.0.19041.0
```

## Try it

Install it, then open the app: Settings, Manage Plugins, TOOLS, and open **Dashboard**. Switch
between saves in the sidebar to see it update.
