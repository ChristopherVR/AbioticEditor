using AbioticEditor.Plugins;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace AbioticEditor.Core.Plugins.Scripting;

/// <summary>
/// Adapts a JavaScript file into an <see cref="IAbioticPlugin"/> so script plugins load
/// through the exact same path as managed ones. On <see cref="Configure"/> it spins up a
/// sandboxed <see cref="Engine">Jint engine</see>, exposes the <c>abiotic</c> host API, and
/// runs the script - which calls <c>abiotic.registerSaveOperation/registerCommand/
/// registerMenuAction/on</c> to register its capabilities, each backed by a JS callback.
///
/// <para>
/// Jint is a pure-managed ECMAScript interpreter, so this works on every target (desktop,
/// mobile, CLI) with no native dependency. The engine is constrained (recursion depth and a
/// wall-clock timeout) and CLR reflection is locked down, so a runaway or hostile script is
/// bounded - though, like managed plugins, JS still runs in-process and is not a security
/// boundary.
/// </para>
/// </summary>
internal sealed class JavaScriptPlugin : IAbioticPlugin
{
    private readonly string _scriptPath;

    public JavaScriptPlugin(string scriptPath) => _scriptPath = scriptPath;

    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        var source = File.ReadAllText(_scriptPath);

        var engine = new Engine(options =>
        {
            options.LimitRecursion(64);
            options.TimeoutInterval(TimeSpan.FromSeconds(10));
            options.MaxStatements(1_000_000);
            // JS authors write camelCase; our CLR facades are PascalCase. A case-insensitive
            // member comparer bridges the two so `ctx.markChanged()` finds `MarkChanged`.
            options.SetTypeResolver(new TypeResolver { MemberNameComparer = StringComparer.OrdinalIgnoreCase });
        });

        var runtime = new JsRuntime(engine, host);
        var api = new AbioticApi(registry, host, runtime);
        engine.SetValue("abiotic", api);

        // A minimal console shim so scripts can use the familiar console.log/warn/error.
        engine.Execute("""
            var console = {
                log:   function () { abiotic.log.info(Array.prototype.join.call(arguments, ' ')); },
                info:  function () { abiotic.log.info(Array.prototype.join.call(arguments, ' ')); },
                warn:  function () { abiotic.log.warn(Array.prototype.join.call(arguments, ' ')); },
                error: function () { abiotic.log.error(Array.prototype.join.call(arguments, ' ')); }
            };
            """);

        // Run the plugin script; registration happens as a side effect of this call. Any
        // error here propagates to PluginManager, which records the plugin as Failed.
        engine.Execute(source);
    }
}

/// <summary>
/// Wraps a Jint <see cref="Engine"/> with the single lock every call must take. Jint engines
/// are not thread-safe and a save operation can run on a background thread, so all JS entry
/// points (invoking a registered callback) funnel through <see cref="Invoke"/>.
/// </summary>
internal sealed class JsRuntime
{
    private readonly Engine _engine;
    private readonly object _lock = new();

    public JsRuntime(Engine engine, IPluginHost host)
    {
        _engine = engine;
        Host = host;
    }

    public IPluginHost Host { get; }

    /// <summary>Invokes a JS function value with CLR arguments, serialized against the lock.</summary>
    public JsValue Invoke(JsValue function, params object?[] arguments)
    {
        lock (_lock)
        {
            return _engine.Invoke(function, arguments!);
        }
    }
}

/// <summary>
/// The <c>abiotic</c> object exposed to scripts. Its public methods are what a plugin script
/// calls to register capabilities and log. Object-literal specs arrive as <see cref="JsValue"/>
/// (e.g. <c>{ id: 'x', execute: function (ctx) { ... } }</c>) and are unpacked here.
/// </summary>
public sealed class AbioticApi
{
    private readonly IPluginRegistry _registry;
    private readonly JsRuntime _runtime;

    internal AbioticApi(IPluginRegistry registry, IPluginHost host, JsRuntime runtime)
    {
        _registry = registry;
        _runtime = runtime;
        Log = new JsLog(host.Log);
        Ui = new JsHostUi(host);
        HostKind = host.HostKind;
        DataDirectory = host.DataDirectory;
        SdkVersion = host.SdkVersion.ToString();
    }

    /// <summary><c>abiotic.log.info/warn/error(message)</c>.</summary>
    public JsLog Log { get; }

    /// <summary><c>abiotic.ui</c> - drive the app UI (alerts, toasts, run operations, navigate).</summary>
    public JsHostUi Ui { get; }

    /// <summary><c>"gui"</c> or <c>"cli"</c>.</summary>
    public string HostKind { get; }

    /// <summary>The plugin's writable data directory.</summary>
    public string DataDirectory { get; }

    /// <summary>The host's plugin SDK version string.</summary>
    public string SdkVersion { get; }

    /// <summary><c>abiotic.registerSaveOperation({ id, displayName, description, appliesTo, parameters, execute })</c>.</summary>
    public void RegisterSaveOperation(JsValue spec)
        => _registry.AddSaveOperation(JsSaveOperation.FromSpec(spec, _runtime));

    /// <summary><c>abiotic.registerCommand({ name, description, arguments, options, invoke })</c>.</summary>
    public void RegisterCommand(JsValue spec)
        => _registry.AddConsoleCommand(JsConsoleCommand.FromSpec(spec, _runtime));

    /// <summary><c>abiotic.registerMenuAction({ id, title, glyph, group, invoke })</c>.</summary>
    public void RegisterMenuAction(JsValue spec)
        => _registry.AddMenuAction(JsMenuAction.FromSpec(spec, _runtime));

    /// <summary>
    /// <c>abiotic.registerWebTool({ id, title, glyph, html | rootDirectory+entryFile, handleMessage })</c>
    /// - an HTML/React UI rendered in the GUI's web view, optionally backed by a JS
    /// <c>handleMessage(message, ctx)</c> bridge handler.
    /// </summary>
    public void RegisterWebTool(JsValue spec)
        => _registry.AddWebTool(JsWebTool.FromSpec(spec, _runtime));

    /// <summary><c>abiotic.on(eventName, function (event) { ... })</c>.</summary>
    public void On(string eventName, JsValue handler)
        => _registry.AddEventHandler(eventName, evt => _runtime.Invoke(handler, new JsEventView(evt)));

    /// <summary>
    /// <c>abiotic.addLocalization("de", { Common_Save: "Speichern", ... })</c> - contribute UI
    /// translations for a culture. The object's string properties become resource key -> text
    /// pairs; non-string values are ignored. A pure-data alternative is a "localization" runtime
    /// plugin that ships resx/json files (no code).
    /// </summary>
    public void AddLocalization(string culture, JsValue strings)
    {
        if (string.IsNullOrWhiteSpace(culture) || strings is null || !strings.IsObject())
        {
            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.AsObject().GetOwnProperties())
        {
            var value = property.Value.Value;
            if (value is not null && value.IsString())
            {
                map[property.Key.ToString()] = value.AsString();
            }
        }
        _registry.AddLocalization(culture, map);
    }
}

/// <summary>
/// The <c>abiotic.ui</c> facade scripts use to interact with the app. The result-less methods
/// are fire-and-forget (the host marshals them onto the UI thread), since the Jint engine is
/// synchronous; a web page that needs a result should use the <c>handleMessage</c> round-trip.
/// In non-GUI hosts these no-op (the bridge is <see cref="NullHostUi"/>).
/// </summary>
public sealed class JsHostUi
{
    private readonly IPluginHost _host;

    internal JsHostUi(IPluginHost host) => _host = host;

    /// <summary>Path of the open save, or null.</summary>
    public string? OpenSavePath => _host.Ui.OpenSavePath;

    /// <summary>Shows an OK dialog in the app.</summary>
    public void ShowAlert(string title, string message) => _ = _host.Ui.ShowAlertAsync(title ?? string.Empty, message ?? string.Empty);

    /// <summary>Shows a transient toast in the app.</summary>
    public void Toast(string message) => _ = _host.Ui.ToastAsync(message ?? string.Empty);

    /// <summary>Runs a registered save operation against the open save (then reloads it).</summary>
    public void RunSaveOperation(string operationId) => _ = _host.Ui.RunSaveOperationAsync(operationId ?? string.Empty);

    /// <summary>Re-reads the open save and rebuilds its editor.</summary>
    public void ReloadSave() => _ = _host.Ui.ReloadOpenSaveAsync();

    /// <summary>Opens the Settings screen.</summary>
    public void OpenSettings() => _ = _host.Ui.OpenSettingsAsync();

    /// <summary>Opens the Plugins management screen.</summary>
    public void OpenPlugins() => _ = _host.Ui.OpenPluginsPanelAsync();
}

/// <summary>Logging facade exposed to scripts as <c>abiotic.log</c>.</summary>
public sealed class JsLog
{
    private readonly IPluginLog _log;

    internal JsLog(IPluginLog log) => _log = log;

    public void Info(string message) => _log.Info(message ?? string.Empty);

    public void Warn(string message) => _log.Warn(message ?? string.Empty);

    public void Error(string message) => _log.Error(message ?? string.Empty);
}
