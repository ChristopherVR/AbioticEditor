# Plugin API Reference

A quick reference of the plugin SDK (`AbioticEditor.Plugins.Abstractions`) and the JavaScript
`abiotic` host object. For the prose walkthrough, see [Building Plugins](Building-Plugins).

## Entry point and host

```csharp
public interface IAbioticPlugin
{
    void Configure(IPluginRegistry registry, IPluginHost host);
}

public interface IPluginRegistry
{
    IPluginHost Host { get; }
    void AddSaveOperation(ISaveOperation operation);
    void AddConsoleCommand(IConsoleCommand command);
    void AddEditorTool(IEditorTool tool);
    void AddWebTool(IWebTool tool);
    void AddSaveUpgrader(ISaveUpgrader upgrader);
    void AddMenuAction(IMenuAction action);
    void AddEventHandler(string eventName, Action<PluginEvent> handler);
}

public interface IPluginHost
{
    Version SdkVersion { get; }
    Version HostVersion { get; }
    string  HostKind { get; }       // "gui" or "cli"
    IPluginLog Log { get; }
    IHostUi    Ui  { get; }         // NullHostUi (no-op) in CLI and tests
    string DataDirectory { get; }   // writable, per-plugin
}
```

## Save kinds

```csharp
public enum SaveKind { Player, World, Metadata, Customization, Any }
```

An operation's `AppliesTo` is matched against the detected kind; `Any` matches everything.

## Save operations

```csharp
public interface ISaveOperation
{
    string   Id { get; }
    string   DisplayName { get; }
    string   Description { get; }
    SaveKind AppliesTo { get; }
    IReadOnlyList<SaveOperationParameter> Parameters { get; }   // default: empty
    Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken ct = default);
}

public sealed record SaveOperationParameter(
    string Name, string Description, bool Required = false, string? DefaultValue = null);

public interface ISaveOperationContext
{
    string FilePath { get; }                                  // read-only path; do not write to it
    SaveKind Kind { get; }
    SaveGame Save { get; }                                    // mutate this in place
    IReadOnlyDictionary<string, string> Parameters { get; }
    bool IsDryRun { get; }                                    // true => host will not write
    IPluginLog Log { get; }
    void MarkChanged();                                       // call iff you changed Save
    bool HasChanges { get; }
    string GetParameter(string name, string fallback = "");
}

public sealed record SaveOperationResult(bool Success, string Message, int ChangeCount = 0)
{
    public static SaveOperationResult Ok(string message, int changeCount = 0);
    public static SaveOperationResult NoChange(string message);
    public static SaveOperationResult Failed(string message);
}
```

The host runs an operation through a single write path: load, kind-check, resolve and require
parameters, execute, and only if `MarkChanged()` was called and it is not a dry run, keep a `.bak`
and rewrite the file. The result reports to the user; it does not decide persistence.

## Console commands

```csharp
public interface IConsoleCommand
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<PluginCommandArgument> Arguments { get; }   // default: empty
    IReadOnlyList<PluginCommandOption>   Options   { get; }   // default: empty
    Task<int> InvokeAsync(IConsoleCommandContext context, CancellationToken ct = default);
}

public sealed record PluginCommandArgument(string Name, string Description, bool Required = true);
public sealed record PluginCommandOption(
    string Name, string Description, bool IsFlag = false, bool Required = false, string? DefaultValue = null);

public interface IConsoleCommandContext
{
    IReadOnlyDictionary<string, string>  Arguments { get; }
    IReadOnlyDictionary<string, string?> Options { get; }
    TextWriter Out { get; }
    TextWriter Error { get; }
    IPluginHost Host { get; }
    bool   GetFlag(string name);
    string GetOption(string name, string fallback = "");
    string RequireArgument(string name);
}
```

Return `0` success, `1` user error, `2` unexpected failure. CLI only; GUI hosts ignore commands.

## Editor tools (MAUI UI)

```csharp
public interface IEditorTool
{
    string Id { get; }
    string Title { get; }
    string? Glyph { get; }                                    // default: null
    EditorToolPlacement Placement { get; }                    // default: Panel
    object CreateView(IEditorToolContext context);            // returns a Microsoft.Maui.Controls.View
}

public enum EditorToolPlacement { Panel, Tab, Sidebar }

public interface IEditorToolContext
{
    IPluginHost Host { get; }
    SaveGame? ActiveSave { get; }                             // lazily loaded
    SaveKind? ActiveSaveKind { get; }
    string?   ActiveSavePath { get; }
    event EventHandler? ActiveSaveChanged;                    // fires on file switch
}
```

When the panel closes the host disposes the context (severing `ActiveSaveChanged` subscribers) and
the view/`BindingContext` if `IDisposable`. A subscribing view-model should implement `IDisposable`.

## Web tools (HTML/React)

```csharp
public interface IWebTool
{
    string Id { get; }
    string Title { get; }
    string? Glyph { get; }
    WebToolContent CreateContent(IWebToolContext context);
    Task<string?> HandleMessageAsync(string message, IWebToolContext context, CancellationToken ct = default);
}

public sealed record WebToolContent
{
    public string? Html { get; init; }
    public string? RootDirectory { get; init; }              // relative paths resolve against the plugin folder
    public string  EntryFile { get; init; } = "index.html";
    public static WebToolContent FromHtml(string html);
    public static WebToolContent FromDirectory(string rootDirectory, string entryFile = "index.html");
}
```

The page bridge (injected by the host): `abiotic.request(obj)` returns a Promise resolved by
`HandleMessageAsync`; `abiotic.log(msg)` and `abiotic.onEvent(fn)` are also available.

## Menu actions

```csharp
public interface IMenuAction
{
    string Id { get; }
    string Title { get; }
    string? Glyph { get; }
    string? Group { get; }
    Task InvokeAsync(IMenuActionContext context, CancellationToken ct = default);
}

public interface IMenuActionContext
{
    IPluginHost Host { get; }
    string?   ActiveSavePath { get; }
    SaveKind? ActiveSaveKind { get; }
    Task NotifyAsync(string message);
}
```

## Events

```csharp
public static class PluginEvents
{
    public const string AppStarted  = "app.started";   // no payload
    public const string SaveOpened  = "save.opened";   // savePath, saveKind
    public const string SaveClosed  = "save.closed";   // no payload
    public const string SaveWritten = "save.written";  // savePath, saveKind, operationId
}

public sealed class PluginEvent
{
    public string Name { get; }
    public IReadOnlyDictionary<string, object?> Data { get; }
    public string?   SavePath { get; }
    public SaveKind? SaveKind { get; }
    public object? Get(string key);
    public string? GetString(string key);
}
```

Handlers run synchronously on the thread that raised the event. Keep them quick and non-throwing; a
throwing handler is logged and isolated, never fatal.

## Save upgraders

```csharp
public interface ISaveUpgrader
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool CanUpgrade(SaveUpgradeProbe probe);                 // cheap, header-only
    Task<SaveUpgradeResult> UpgradeAsync(ISaveUpgradeContext context, CancellationToken ct = default);
}

public sealed record SaveUpgradeProbe(
    string FilePath, long FileLength, string? SaveClass, int SaveGameVersion,
    uint PackageVersionUE4, uint PackageVersionUE5, int? AbfVersion, string? LoadError);

public interface ISaveUpgradeContext
{
    string  FilePath { get; }
    byte[]  OriginalBytes { get; }
    SaveUpgradeProbe Probe { get; }
    IPluginLog Log { get; }
}

public sealed record SaveUpgradeResult(bool Handled, string Message, byte[]? UpgradedBytes = null)
{
    public static SaveUpgradeResult Ok(byte[] upgradedBytes, string message);
    public static SaveUpgradeResult NotHandled(string message);
}
```

## Manifest model

```csharp
public sealed record PluginManifest
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public string Author { get; init; }
    public string Description { get; init; }
    public string Runtime { get; init; }          // "dotnet" | "javascript"
    public string EntryAssembly { get; init; }
    public string EntryScript { get; init; }
    public string MinHostVersion { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; }
    public bool Enabled { get; init; }
}

public static class PluginRuntimes { public const string DotNet = "dotnet", JavaScript = "javascript"; }
public static class PluginCapabilities
{
    public const string SaveOperation = "saveOperation", ConsoleCommand = "consoleCommand",
        EditorTool = "editorTool", WebTool = "webTool", MenuAction = "menuAction",
        EventHandler = "eventHandler", SaveUpgrader = "saveUpgrader";
}
```

## JavaScript `abiotic` host object

```javascript
// logging
abiotic.log.info(msg);  abiotic.log.warn(msg);  abiotic.log.error(msg);
console.log(...);                                  // shimmed onto abiotic.log

// properties
abiotic.hostKind;        // "gui" | "cli"
abiotic.dataDirectory;   // writable per-plugin folder
abiotic.sdkVersion;

// drive the app (no-op in CLI/tests)
abiotic.ui.showAlert(title, message);
abiotic.ui.toast(message);
abiotic.ui.runSaveOperation(operationId, parameters?);   // runs an op on the open save, then reloads
abiotic.ui.reloadSave();
abiotic.ui.openSettings();
abiotic.ui.openPlugins();

// register capabilities
abiotic.registerSaveOperation({ id, displayName, description, appliesTo, parameters, execute(ctx) {...} });
abiotic.registerCommand({ name, description, arguments, options, invoke(ctx) {...} });
abiotic.registerMenuAction({ id, title, glyph, group, invoke(ctx) {...} });
abiotic.registerWebTool({ id, title, glyph, html | rootDirectory, entryFile, handleMessage(message, ctx) {...} });
abiotic.on(eventName, function (event) { ... });
```

Capability contexts (case-insensitive member access, so camelCase works):

```javascript
// save operation ctx
ctx.filePath; ctx.kind; ctx.isDryRun; ctx.getParameter(name, fallback);
ctx.logInfo(msg); ctx.logWarn(msg); ctx.markChanged();
ctx.player.money;                 // get/set (player saves)
ctx.player.skillCount;            // readonly
ctx.player.recipeCount;           // readonly
ctx.player.setAllSkillLevels(n);  // returns how many changed

// command ctx
ctx.getArgument(name, fallback); ctx.getOption(name, fallback); ctx.getFlag(name);
ctx.print(msg); ctx.printError(msg);

// menu ctx
ctx.activeSavePath; ctx.activeSaveKind; ctx.notify(msg);

// web tool handleMessage ctx
ctx.activeSavePath; ctx.activeSaveKind; ctx.logInfo(msg); ctx.playerSummaryJson();

// event object
event.name; event.savePath; event.saveKind; event.get(key);
```

In the page (web tool), the host injects: `abiotic.request(obj)` (a Promise resolved by your
`handleMessage`), `abiotic.log(msg)`, and `abiotic.onEvent(fn)`.
