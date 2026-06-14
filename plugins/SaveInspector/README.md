# SaveInspector - sample plugin

A managed (.NET / MAUI) **editor tool** built the full MVVM way: a compiled XAML view bound to a
view-model. Where [`PlaytimeDashboard`](../PlaytimeDashboard/) builds its UI in C#, this sample is
the reference for a properly structured UI plugin, including lifetime cleanup.

- **Runtime:** .NET / MAUI (`SaveInspector.dll`)
- **Capability:** editor tool `save-inspector`
- **Surfaced in:** the app's Plugins panel only

## What it does

Adds an "Inspector" panel that lists the open player save's skills (name, level, XP) and a few
headline stats, with a Refresh command and an empty state when no player save is open. It is
read-only.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.save-inspector",
  "name": "Save Inspector",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "A full-MVVM UI tool (XAML + view-model) that lists the open player save's skills and headline stats.",
  "entryAssembly": "SaveInspector.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["editorTool"],
  "enabled": true
}
```

## Layout

```
SaveInspector/
  plugin.json
  SaveInspectorPlugin.cs   # IAbioticPlugin: registers InspectorTool
  InspectorView.xaml       # compiled ContentView (x:DataType for compiled bindings)
  InspectorView.xaml.cs    # code-behind
  InspectorViewModel.cs    # INotifyPropertyChanged + IDisposable + a Command + ObservableCollection
```

## How it works

`SaveInspectorPlugin.Configure` registers an `InspectorTool : IEditorTool` whose `CreateView(ctx)`
returns `new InspectorView(new InspectorViewModel(ctx))`. The view-model:

- exposes `Skills` (an `ObservableCollection`), `FileName`, `Summary`, and empty-state flags;
- has a `RefreshCommand` that re-reads the active save and rebuilds the displayed state;
- subscribes to `ctx.ActiveSaveChanged` for live updates and, crucially, implements `IDisposable`
  to unsubscribe.

The lifetime detail matters: when the panel closes the host disposes the context (severing every
`ActiveSaveChanged` subscriber) and disposes the view and its `BindingContext` if they implement
`IDisposable`. A subscribing view-model that did not implement `IDisposable` could outlive its
panel and leak the save it parsed.

## Build

MAUI plugin: needs the MAUI workload and a target framework moniker.

```console
dotnet build plugins/SaveInspector -c Release -f net10.0-windows10.0.19041.0
```

The XAML is compiled at build time, so binding and handler errors surface as build errors.

## Try it

Install it, then in the app: Settings, Manage Plugins, TOOLS, open **Inspector**. Open a player
save and use Refresh; switch files to see it update.
