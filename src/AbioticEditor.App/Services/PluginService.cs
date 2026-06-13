using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;
using UeSaveGame;

namespace AbioticEditor.App.Services;

/// <summary>
/// The GUI's front door to the plugin system, kept static to match the app's other ambient
/// services (Theme/Spoiler). It loads plugins once at startup and exposes the capabilities
/// the GUI cares about - save operations (run against the open save through the same
/// backup/write path the CLI uses) and editor tools (views hosted in the Plugins panel).
///
/// <para>
/// All plugin work is wrapped so a misbehaving plugin can never take the app down: load
/// failures are recorded on the descriptor, and running an operation surfaces errors as a
/// result, never an unhandled exception.
/// </para>
/// </summary>
public static class PluginService
{
    private static bool _initialized;

    /// <summary>
    /// Installs the app UI bridge so plugins (including JavaScript ones via <c>abiotic.ui</c>)
    /// can drive the app. Call before <see cref="Initialize"/> so it is in place when plugins
    /// load. Safe to call again to update the bound view-model.
    /// </summary>
    public static void InstallHostUi(ViewModels.MainViewModel vm, Func<Task> rebuildHost)
        => PluginHostEnvironment.HostUi = new AppHostUi(vm, rebuildHost);

    /// <summary>
    /// Loads and activates plugins. Idempotent; call once during app startup (after the
    /// editor log preference is known so plugin logs are captured). Never throws.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        try
        {
            PluginManager.Shared.EnsureLoaded("gui");
            var loaded = PluginManager.Shared.Descriptors.Count(d => d.State == PluginLoadState.Loaded);
            EditorLog.Info("Plugins", $"GUI plugin load complete: {loaded} active plugin(s).");
            // Let event-handler plugins run any startup logic.
            PluginManager.Shared.RaiseEvent(PluginEvents.AppStarted);
        }
        catch (Exception ex)
        {
            EditorLog.Error("Plugins", "GUI plugin initialization failed", ex);
        }
    }

    /// <summary>Every discovered plugin, including disabled/failed (for the management list).</summary>
    public static IReadOnlyList<PluginDescriptor> Descriptors => PluginManager.Shared.Descriptors;

    /// <summary>All save operations from loaded plugins.</summary>
    public static IReadOnlyList<PluginCapability<ISaveOperation>> SaveOperations =>
        PluginManager.Shared.SaveOperations;

    /// <summary>All editor (UI) tools from loaded plugins.</summary>
    public static IReadOnlyList<PluginCapability<IEditorTool>> EditorTools =>
        PluginManager.Shared.EditorTools;

    /// <summary>All web (HTML/React) tools from loaded plugins.</summary>
    public static IReadOnlyList<PluginCapability<IWebTool>> WebTools =>
        PluginManager.Shared.WebTools;

    /// <summary>All menu actions from loaded plugins.</summary>
    public static IReadOnlyList<PluginCapability<IMenuAction>> MenuActions =>
        PluginManager.Shared.MenuActions;

    /// <summary>True if any plugin loaded and registered a GUI-surfaced capability.</summary>
    public static bool HasAnyCapabilities =>
        SaveOperations.Count > 0 || EditorTools.Count > 0 || MenuActions.Count > 0;

    /// <summary>
    /// Builds a context for running a menu action, bound to that action's plugin host.
    /// <paramref name="notify"/> is how the action shows a message to the user (the caller
    /// supplies a dialog-based implementation).
    /// </summary>
    public static IMenuActionContext CreateMenuActionContext(
        PluginCapability<IMenuAction> capability, string? activeSavePath, Func<string, Task> notify)
        => new MenuActionContext(capability.Plugin.Host!, activeSavePath, notify);

    /// <summary>
    /// Runs a save operation against a file on disk through the host's backup/write path.
    /// Returns the full outcome (including whether the file was actually written).
    /// </summary>
    public static Task<SaveOperationRunner.RunOutcome> RunOperationAsync(
        PluginCapability<ISaveOperation> capability,
        string filePath,
        IReadOnlyDictionary<string, string>? parameters,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var log = capability.Plugin.Host?.Log ?? new NullLog();
        return SaveOperationRunner.RunAsync(capability.Value, filePath, parameters, log, dryRun, cancellationToken);
    }

    /// <summary>
    /// Builds an <see cref="IEditorToolContext"/> for a tool view. The active save is loaded
    /// lazily from <paramref name="activeSavePath"/> the first time a tool reads it, so
    /// opening a tool with a 100+ MB world save selected does not pay the parse cost unless
    /// the tool actually inspects the save.
    /// </summary>
    public static EditorToolContext CreateToolContext(PluginCapability<IEditorTool> capability, string? activeSavePath)
        => new(capability.Plugin.Host!, activeSavePath);

    /// <summary>
    /// Builds a context for a web (HTML/React) tool. The path provider is read live on each
    /// request so the page sees the current save (after edits or a save switch).
    /// </summary>
    public static WebToolContext CreateWebToolContext(PluginCapability<IWebTool> capability, Func<string?> activeSavePathProvider)
        => new(capability.Plugin.Host!, activeSavePathProvider);

    private sealed class NullLog : IPluginLog
    {
        public void Info(string message) => EditorLog.Info("Plugin", message);
        public void Warn(string message) => EditorLog.Warn("Plugin", message);
        public void Error(string message, Exception? exception = null) => EditorLog.Error("Plugin", message, exception);
    }
}

/// <summary>
/// GUI-side <see cref="IEditorToolContext"/>. Loads the active save on demand and lets the
/// host push a new active save (raising <see cref="ActiveSaveChanged"/>) when the user
/// switches files, so live tools refresh.
/// </summary>
public sealed class EditorToolContext : IEditorToolContext, IDisposable
{
    private string? _path;
    private SaveGame? _loaded;
    private bool _attempted;

    internal EditorToolContext(IPluginHost host, string? activeSavePath)
    {
        Host = host;
        _path = activeSavePath;
    }

    public IPluginHost Host { get; }

    public string? ActiveSavePath => _path;

    public SaveKind? ActiveSaveKind => _path is null ? null : SaveKindDetector.Detect(_path);

    public SaveGame? ActiveSave
    {
        get
        {
            if (_path is null)
            {
                return null;
            }
            if (!_attempted)
            {
                _attempted = true;
                try
                {
                    Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
                    using var fs = File.OpenRead(_path);
                    _loaded = SaveGame.LoadFrom(fs);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    Host.Log.Warn($"tool could not load active save '{_path}': {ex.Message}");
                    _loaded = null;
                }
            }
            return _loaded;
        }
    }

    public event EventHandler? ActiveSaveChanged;

    /// <summary>Points the context at a different save and notifies the tool to refresh.</summary>
    public void SetActiveSave(string? path)
    {
        _path = path;
        _loaded = null;
        _attempted = false;
        ActiveSaveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Severs the context↔tool link and frees the parsed save. The host disposes this when the
    /// tool's hosting page closes, so a tool view-model that subscribed to
    /// <see cref="ActiveSaveChanged"/> (the documented pattern) cannot keep the context - and a
    /// large parsed world save - alive after its panel is gone.
    /// </summary>
    public void Dispose()
    {
        ActiveSaveChanged = null;
        _loaded = null;
        _attempted = false;
    }
}

/// <summary>
/// GUI-side <see cref="IWebToolContext"/>. The bound save path is resolved live from the
/// view-model on each access, and <see cref="ActiveSave"/> is re-read from disk per request -
/// so a web dashboard that polls after an edit (or after the user switches saves) always sees
/// current data. Re-reading is cheap for player saves; a web tool over giant world saves
/// should request deliberately rather than in a tight loop.
/// </summary>
public sealed class WebToolContext : IWebToolContext
{
    private readonly Func<string?> _pathProvider;

    // A web dashboard often polls ActiveSave on a timer, so we memoize the parsed save keyed on
    // (path, last-write-time). An unchanged file (the common case between edits) is served from
    // the cache instead of re-parsing a potentially large world save on every bridge request;
    // any write - including the host's own save/upgrade path - bumps the write time and
    // invalidates it automatically, so the page still sees fresh data after an edit.
    private string? _cachedPath;
    private DateTime _cachedWriteTimeUtc;
    private SaveGame? _cachedSave;

    internal WebToolContext(IPluginHost host, Func<string?> pathProvider)
    {
        Host = host;
        _pathProvider = pathProvider;
    }

    public IPluginHost Host { get; }

    public string? ActiveSavePath => _pathProvider();

    public SaveKind? ActiveSaveKind => ActiveSavePath is { } p ? SaveKindDetector.Detect(p) : null;

    public SaveGame? ActiveSave
    {
        get
        {
            if (ActiveSavePath is not { } path)
            {
                _cachedPath = null;
                _cachedSave = null;
                return null;
            }
            try
            {
                var writeTimeUtc = File.GetLastWriteTimeUtc(path);
                if (_cachedSave is not null
                    && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase)
                    && _cachedWriteTimeUtc == writeTimeUtc)
                {
                    return _cachedSave;
                }

                Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
                using var fs = File.OpenRead(path);
                var loaded = SaveGame.LoadFrom(fs);
                _cachedPath = path;
                _cachedWriteTimeUtc = writeTimeUtc;
                _cachedSave = loaded;
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Host.Log.Warn($"web tool could not load active save '{path}': {ex.Message}");
                _cachedPath = null;
                _cachedSave = null;
                return null;
            }
        }
    }
}

/// <summary>GUI-side <see cref="IMenuActionContext"/>; routes NotifyAsync to a supplied dialog.</summary>
internal sealed class MenuActionContext : IMenuActionContext
{
    private readonly Func<string, Task> _notify;

    public MenuActionContext(IPluginHost host, string? activeSavePath, Func<string, Task> notify)
    {
        Host = host;
        ActiveSavePath = activeSavePath;
        _notify = notify;
    }

    public IPluginHost Host { get; }

    public string? ActiveSavePath { get; }

    public SaveKind? ActiveSaveKind => ActiveSavePath is null ? null : SaveKindDetector.Detect(ActiveSavePath);

    public Task NotifyAsync(string message) => _notify(message);
}
