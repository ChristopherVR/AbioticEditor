using System.Reflection;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Discovers, loads, and aggregates plugins. The flow is two-phase by design:
/// <list type="number">
/// <item><b>Discover</b> reads every <c>plugin.json</c> under the plugin roots WITHOUT
///   loading any code, so listing/validating/version-checking is cheap and safe.</item>
/// <item><b>Load</b> activates the enabled, compatible plugins: loads each into its own
///   isolated context, runs its entry point, and records the capabilities it registers.</item>
/// </list>
/// Both the CLI and the GUI share one <see cref="Shared"/> instance so a save operation
/// behaves the same everywhere; tests new up their own instance against a temp root.
/// </summary>
public sealed class PluginManager
{
    private readonly List<PluginDescriptor> _descriptors = new();
    private readonly object _sync = new();
    private bool _loaded;

    // The set of loaded capabilities is immutable once EnsureLoaded completes, so we snapshot
    // each aggregate array once (under the lock, at the end of EnsureLoaded) instead of
    // rebuilding it with a SelectMany/ToArray on every property access - these are read on
    // GUI hot paths (e.g. abiotic.ui.runSaveOperation, menu builds).
    private PluginCapability<ISaveOperation>[] _saveOperations = Array.Empty<PluginCapability<ISaveOperation>>();
    private PluginCapability<IConsoleCommand>[] _consoleCommands = Array.Empty<PluginCapability<IConsoleCommand>>();
    private PluginCapability<IEditorTool>[] _editorTools = Array.Empty<PluginCapability<IEditorTool>>();
    private PluginCapability<IWebTool>[] _webTools = Array.Empty<PluginCapability<IWebTool>>();
    private PluginCapability<ISaveUpgrader>[] _saveUpgraders = Array.Empty<PluginCapability<ISaveUpgrader>>();
    private PluginCapability<IMenuAction>[] _menuActions = Array.Empty<PluginCapability<IMenuAction>>();

    /// <summary>Process-wide instance the GUI and CLI use.</summary>
    public static PluginManager Shared { get; } = new();

    /// <summary>Every descriptor discovered, including disabled and failed ones.</summary>
    public IReadOnlyList<PluginDescriptor> Descriptors
    {
        get { lock (_sync) { return _descriptors.ToArray(); } }
    }

    /// <summary>All save operations from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<ISaveOperation>> SaveOperations
    {
        get { lock (_sync) { return _saveOperations; } }
    }

    /// <summary>All console commands from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<IConsoleCommand>> ConsoleCommands
    {
        get { lock (_sync) { return _consoleCommands; } }
    }

    /// <summary>All editor (UI) tools from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<IEditorTool>> EditorTools
    {
        get { lock (_sync) { return _editorTools; } }
    }

    /// <summary>All web (HTML/React) tools from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<IWebTool>> WebTools
    {
        get { lock (_sync) { return _webTools; } }
    }

    /// <summary>All save upgraders from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<ISaveUpgrader>> SaveUpgraders
    {
        get { lock (_sync) { return _saveUpgraders; } }
    }

    /// <summary>All menu actions from successfully-loaded plugins.</summary>
    public IReadOnlyList<PluginCapability<IMenuAction>> MenuActions
    {
        get { lock (_sync) { return _menuActions; } }
    }

    /// <summary>
    /// Ensures plugins have been discovered and loaded exactly once for this process. Safe
    /// to call from multiple hosts/threads; subsequent calls are no-ops. Never throws - a
    /// broken plugin is recorded as failed, it does not take the host down.
    /// </summary>
    /// <param name="hostKind">Identifies the surface (<c>"gui"</c>/<c>"cli"</c>).</param>
    /// <param name="shouldLoad">
    /// Optional gate consulted per manifest BEFORE loading code, so a host can skip plugins
    /// it can't use (e.g. the CLI skipping UI-only plugins). Null loads every enabled plugin.
    /// </param>
    public void EnsureLoaded(string hostKind, Func<PluginManifest, bool>? shouldLoad = null)
    {
        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            DiscoverInto(_descriptors);
            foreach (var descriptor in _descriptors)
            {
                LoadOne(descriptor, hostKind, shouldLoad);
            }
            RebuildCapabilityCaches();
        }
    }

    /// <summary>
    /// Snapshots the aggregate capability arrays from the loaded descriptors. Called once under
    /// the lock at the end of <see cref="EnsureLoaded"/>; the result is immutable thereafter.
    /// </summary>
    private void RebuildCapabilityCaches()
    {
        var loaded = _descriptors.Where(d => d.State == PluginLoadState.Loaded).ToArray();
        _saveOperations = Flatten(loaded, d => d.SaveOperations);
        _consoleCommands = Flatten(loaded, d => d.ConsoleCommands);
        _editorTools = Flatten(loaded, d => d.EditorTools);
        _webTools = Flatten(loaded, d => d.WebTools);
        _saveUpgraders = Flatten(loaded, d => d.SaveUpgraders);
        _menuActions = Flatten(loaded, d => d.MenuActions);
    }

    private static PluginCapability<T>[] Flatten<T>(
        PluginDescriptor[] loaded, Func<PluginDescriptor, IReadOnlyList<T>> select)
        => loaded.SelectMany(d => select(d).Select(c => new PluginCapability<T>(d, c))).ToArray();

    /// <summary>
    /// Dispatches <paramref name="pluginEvent"/> to every loaded plugin that subscribed to
    /// that event name. Handlers are snapshotted under the lock and invoked outside it (so a
    /// handler may safely raise further events), and each is isolated in try/catch - one
    /// throwing handler can't break the others or the host action that fired the event.
    /// </summary>
    public void RaiseEvent(PluginEvent pluginEvent)
    {
        ArgumentNullException.ThrowIfNull(pluginEvent);

        List<(string PluginId, Action<PluginEvent> Handler)> handlers;
        lock (_sync)
        {
            handlers = _descriptors
                .Where(d => d.State == PluginLoadState.Loaded)
                .SelectMany(d => d.EventHandlers
                    .Where(s => string.Equals(s.EventName, pluginEvent.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(s => (d.Id, s.Handler)))
                .ToList();
        }

        foreach (var (pluginId, handler) in handlers)
        {
            try
            {
                handler(pluginEvent);
            }
            catch (Exception ex)
            {
                EditorLog.Error("Plugins", $"event handler in '{pluginId}' for '{pluginEvent.Name}' threw", ex);
            }
        }
    }

    /// <summary>Convenience overload that builds the <see cref="PluginEvent"/> from a name + data.</summary>
    public void RaiseEvent(string eventName, IReadOnlyDictionary<string, object?>? data = null)
        => RaiseEvent(new PluginEvent(eventName, data));

    /// <summary>
    /// Reads and validates every manifest under the plugin roots without loading code.
    /// Exposed for the GUI's "what's installed" list and for tests. Idempotent.
    /// </summary>
    public static IReadOnlyList<PluginManifest> DiscoverManifests()
    {
        var found = new List<PluginManifest>();
        var byId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in PluginPaths.Roots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var folder in SafeEnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(folder, PluginManifestIo.FileName);
                var manifest = PluginManifestIo.TryRead(manifestPath);
                if (manifest is null || !byId.Add(manifest.Id))
                {
                    continue;
                }
                found.Add(manifest);
            }
        }
        return found;
    }

    // ---------- internals ----------

    private static void DiscoverInto(List<PluginDescriptor> sink)
    {
        var byId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in PluginPaths.Roots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var folder in SafeEnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(folder, PluginManifestIo.FileName);
                var manifest = PluginManifestIo.TryRead(manifestPath);
                if (manifest is null)
                {
                    continue;
                }
                if (!byId.Add(manifest.Id))
                {
                    // A user-root copy shadows a bundled one with the same id (roots are
                    // user-first). Skip the second sighting.
                    EditorLog.Info("Plugins", $"Ignoring duplicate plugin id '{manifest.Id}' at {folder}.");
                    continue;
                }
                sink.Add(new PluginDescriptor(manifest, folder, manifestPath));
            }
        }
    }

    private static void LoadOne(PluginDescriptor descriptor, string hostKind, Func<PluginManifest, bool>? shouldLoad)
    {
        var manifest = descriptor.Manifest;

        if (!manifest.Enabled)
        {
            descriptor.State = PluginLoadState.Disabled;
            return;
        }

        // Compatibility gate: refuse a plugin built for a newer SDK than this host has, with
        // a clear message instead of a runtime MissingMethod crash later.
        var required = Version.Parse(PluginManifestIo.NormalizeVersion(manifest.MinHostVersion));
        var sdk = typeof(IAbioticPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        if (required > sdk)
        {
            descriptor.State = PluginLoadState.Failed;
            descriptor.LoadError = $"requires plugin SDK {required} but host provides {sdk}.";
            EditorLog.Warn("Plugins", $"{manifest.Id}: {descriptor.LoadError}");
            return;
        }

        if (shouldLoad is not null && !shouldLoad(manifest))
        {
            // The host can't use anything this plugin offers; leave it discovered-but-unloaded
            // rather than spinning up a load context for nothing.
            descriptor.State = PluginLoadState.Discovered;
            return;
        }

        // A localization pack is pure data: no assembly/script, no Configure pass. Read its
        // resx/json files and merge their strings, then we are done.
        if (string.Equals(manifest.Runtime, PluginRuntimes.Localization, StringComparison.OrdinalIgnoreCase))
        {
            LoadLocalizationPlugin(descriptor);
            return;
        }

        try
        {
            var host = new PluginHost(manifest.Id, hostKind);
            var registry = new PluginRegistry(manifest.Id, host);

            // Build the entry point from whichever runtime the manifest declares, then run
            // its single Configure pass. The registry capture afterwards is identical for
            // managed and JavaScript plugins.
            var plugin = CreatePlugin(descriptor);
            plugin.Configure(registry, host);

            descriptor.Host = host;
            descriptor.SaveOperations = registry.SaveOperations;
            descriptor.ConsoleCommands = registry.ConsoleCommands;
            descriptor.EditorTools = registry.EditorTools;
            descriptor.WebTools = registry.WebTools;
            descriptor.SaveUpgraders = registry.SaveUpgraders;
            descriptor.MenuActions = registry.MenuActions;
            descriptor.EventHandlers = registry.EventHandlers;
            // A code/JS plugin can contribute translations both at runtime (AddLocalization) and
            // by shipping files declared in its manifest. Merge both, then publish them.
            descriptor.Localizations = registry.Localizations
                .Concat(LoadManifestLocalizations(descriptor))
                .ToArray();
            ApplyLocalizations(descriptor);
            descriptor.State = PluginLoadState.Loaded;

            EditorLog.Info("Plugins",
                $"Loaded '{manifest.Id}' v{manifest.Version} ({descriptor.CapabilitySummary()}).");
        }
        catch (Exception ex)
        {
            descriptor.State = PluginLoadState.Failed;
            descriptor.LoadError = ex.Message;
            EditorLog.Error("Plugins", $"Failed to load plugin '{manifest.Id}'", ex);
        }
    }

    /// <summary>
    /// Loads a resource-only localization pack: reads each file named in the manifest's
    /// <c>localizations</c> map and merges its strings. No assembly or script is loaded.
    /// </summary>
    private static void LoadLocalizationPlugin(PluginDescriptor descriptor)
    {
        var manifest = descriptor.Manifest;
        descriptor.Localizations = LoadManifestLocalizations(descriptor);

        if (descriptor.Localizations.Count == 0)
        {
            descriptor.State = PluginLoadState.Failed;
            descriptor.LoadError = "no usable translation files (all missing, empty, or unreadable).";
            EditorLog.Warn("Plugins", $"{manifest.Id}: {descriptor.LoadError}");
            return;
        }

        ApplyLocalizations(descriptor);
        descriptor.State = PluginLoadState.Loaded;
        EditorLog.Info("Plugins",
            $"Loaded localization pack '{manifest.Id}' v{manifest.Version} ({descriptor.CapabilitySummary()}).");
    }

    /// <summary>Reads the manifest's <c>localizations</c> files (resx/json) from the plugin folder.</summary>
    private static IReadOnlyList<PluginLocalization> LoadManifestLocalizations(PluginDescriptor descriptor)
    {
        if (descriptor.Manifest.Localizations.Count == 0)
        {
            return Array.Empty<PluginLocalization>();
        }

        var result = new List<PluginLocalization>();
        foreach (var (culture, file) in descriptor.Manifest.Localizations)
        {
            var filePath = Path.Combine(descriptor.Folder, file);
            var strings = LocalizationResourceLoader.Load(filePath);
            if (strings is null || strings.Count == 0)
            {
                EditorLog.Warn("Plugins", $"{descriptor.Id}: no strings loaded for '{culture}' from {file}.");
                continue;
            }
            result.Add(new PluginLocalization(culture, strings));
        }
        return result;
    }

    /// <summary>Publishes a descriptor's contributed strings into the process-wide table.</summary>
    private static void ApplyLocalizations(PluginDescriptor descriptor)
    {
        foreach (var localization in descriptor.Localizations)
        {
            PluginLocalizations.Add(localization.Culture, localization.Strings);
        }
    }

    /// <summary>
    /// Constructs the <see cref="IAbioticPlugin"/> entry point for a descriptor, dispatching
    /// on the manifest's runtime: a managed assembly loaded in an isolated context, or a
    /// JavaScript file wrapped by <see cref="Scripting.JavaScriptPlugin"/>.
    /// </summary>
    private static IAbioticPlugin CreatePlugin(PluginDescriptor descriptor)
    {
        var manifest = descriptor.Manifest;

        if (string.Equals(manifest.Runtime, PluginRuntimes.JavaScript, StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = Path.Combine(descriptor.Folder, manifest.EntryScript);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"entry script not found: {scriptPath}");
            }
            return new Scripting.JavaScriptPlugin(scriptPath);
        }

        var entryPath = Path.Combine(descriptor.Folder, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException($"entry assembly not found: {entryPath}");
        }

        var context = new PluginLoadContext(manifest.Id, entryPath);
        var assembly = context.LoadFromAssemblyPath(entryPath);
        var pluginType = FindEntryType(assembly)
            ?? throw new TypeLoadException(
                $"no public parameterless type implementing {nameof(IAbioticPlugin)} in {manifest.EntryAssembly}.");
        return (IAbioticPlugin)Activator.CreateInstance(pluginType)!;
    }

    /// <summary>
    /// Finds the single entry type: a public, non-abstract, parameterless-constructible
    /// implementor of <see cref="IAbioticPlugin"/>. Tolerates partial type-load failures
    /// (a plugin DLL that references something unavailable in this host) by inspecting only
    /// the types that did load.
    /// </summary>
    private static Type? FindEntryType(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types
            .Where(t => t is not null)
            .FirstOrDefault(t =>
                typeof(IAbioticPlugin).IsAssignableFrom(t!)
                && t is { IsAbstract: false, IsInterface: false }
                && t.GetConstructor(Type.EmptyTypes) is not null);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Warn("Plugins", $"Could not enumerate plugin root {root}: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// Pairs a capability with the plugin that provided it, so hosts can show provenance
/// ("from Max Skills v1.0") and namespace ids by plugin when needed.
/// </summary>
public sealed record PluginCapability<T>(PluginDescriptor Plugin, T Value);
