using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// One row in the host's plugin inventory: the manifest, where it lives, whether it loaded,
/// and (once loaded) the capabilities it registered. A descriptor exists even for plugins
/// that are disabled or failed to load, so the GUI can list them with a status and the user
/// can re-enable or read the error.
/// </summary>
public sealed class PluginDescriptor
{
    internal PluginDescriptor(PluginManifest manifest, string folder, string manifestPath)
    {
        Manifest = manifest;
        Folder = folder;
        ManifestPath = manifestPath;
    }

    /// <summary>The validated manifest (kept mutable-by-replacement via <see cref="SetEnabled"/>).</summary>
    public PluginManifest Manifest { get; private set; }

    /// <summary>The plugin's folder (contains the manifest and DLLs).</summary>
    public string Folder { get; }

    /// <summary>Full path of the <c>plugin.json</c> this descriptor came from.</summary>
    public string ManifestPath { get; }

    /// <summary>Convenience accessor for the manifest id.</summary>
    public string Id => Manifest.Id;

    /// <summary>Load outcome. <see cref="PluginLoadState.Loaded"/> once capabilities are live.</summary>
    public PluginLoadState State { get; internal set; } = PluginLoadState.Discovered;

    /// <summary>Populated when <see cref="State"/> is <see cref="PluginLoadState.Failed"/>.</summary>
    public string? LoadError { get; internal set; }

    /// <summary>Save operations the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<ISaveOperation> SaveOperations { get; internal set; } = Array.Empty<ISaveOperation>();

    /// <summary>Console commands the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<IConsoleCommand> ConsoleCommands { get; internal set; } = Array.Empty<IConsoleCommand>();

    /// <summary>Editor (UI) tools the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<IEditorTool> EditorTools { get; internal set; } = Array.Empty<IEditorTool>();

    /// <summary>Web (HTML/React) tools the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<IWebTool> WebTools { get; internal set; } = Array.Empty<IWebTool>();

    /// <summary>Save upgraders the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<ISaveUpgrader> SaveUpgraders { get; internal set; } = Array.Empty<ISaveUpgrader>();

    /// <summary>Menu actions the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<IMenuAction> MenuActions { get; internal set; } = Array.Empty<IMenuAction>();

    /// <summary>Event subscriptions the plugin registered (empty until loaded).</summary>
    public IReadOnlyList<PluginEventSubscription> EventHandlers { get; internal set; } = Array.Empty<PluginEventSubscription>();

    /// <summary>
    /// The plugin's host services, available once loaded. Hosts use it to build capability
    /// contexts (the CLI for console commands, the GUI for editor tools) and to reach the
    /// plugin's logger and data directory.
    /// </summary>
    public IPluginHost? Host { get; internal set; }

    /// <summary>True if the plugin loaded and exposed at least one capability.</summary>
    public bool HasCapabilities =>
        SaveOperations.Count > 0 || ConsoleCommands.Count > 0 || EditorTools.Count > 0
        || WebTools.Count > 0 || MenuActions.Count > 0 || EventHandlers.Count > 0 || SaveUpgraders.Count > 0;

    /// <summary>A short, human description of what the plugin provides, for list UIs.</summary>
    public string CapabilitySummary()
    {
        if (State != PluginLoadState.Loaded)
        {
            return State == PluginLoadState.Disabled ? "disabled"
                : State == PluginLoadState.Failed ? $"failed: {LoadError}"
                : "not loaded";
        }
        var parts = new List<string>();
        if (SaveOperations.Count > 0) parts.Add($"{SaveOperations.Count} operation(s)");
        if (ConsoleCommands.Count > 0) parts.Add($"{ConsoleCommands.Count} command(s)");
        if (EditorTools.Count > 0) parts.Add($"{EditorTools.Count} tool(s)");
        if (WebTools.Count > 0) parts.Add($"{WebTools.Count} web tool(s)");
        if (SaveUpgraders.Count > 0) parts.Add($"{SaveUpgraders.Count} upgrader(s)");
        if (MenuActions.Count > 0) parts.Add($"{MenuActions.Count} menu action(s)");
        if (EventHandlers.Count > 0) parts.Add($"{EventHandlers.Count} event handler(s)");
        return parts.Count == 0 ? "no capabilities" : string.Join(", ", parts);
    }

    /// <summary>
    /// Flips the enabled flag and persists it to the manifest on disk. Takes effect for the
    /// loading decision on the next discovery/restart - already-loaded code stays in memory
    /// for this session (managed plugins can't be safely yanked mid-run), which the GUI
    /// communicates as "restart to apply".
    /// </summary>
    public bool SetEnabled(bool enabled)
    {
        if (Manifest.Enabled == enabled)
        {
            return true;
        }
        var updated = Manifest with { Enabled = enabled };
        if (!PluginManifestIo.TryWrite(ManifestPath, updated))
        {
            return false;
        }
        Manifest = updated;
        if (!enabled && State == PluginLoadState.Discovered)
        {
            State = PluginLoadState.Disabled;
        }
        return true;
    }
}

/// <summary>Lifecycle states a <see cref="PluginDescriptor"/> moves through.</summary>
public enum PluginLoadState
{
    /// <summary>Manifest read and validated; assembly not yet loaded.</summary>
    Discovered,

    /// <summary>Manifest marks the plugin disabled; it will not be loaded.</summary>
    Disabled,

    /// <summary>Assembly loaded, entry point ran, capabilities registered.</summary>
    Loaded,

    /// <summary>Loading or configuring threw; see <see cref="PluginDescriptor.LoadError"/>.</summary>
    Failed,
}
