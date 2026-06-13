namespace AbioticEditor.Plugins;

/// <summary>
/// Services the host (GUI, CLI, or a test harness) offers to a loaded plugin. Passed to
/// <see cref="IAbioticPlugin.Configure"/> and reachable from every capability context.
/// Intentionally narrow: a plugin should reach the outside world only through here, which
/// keeps plugins portable across the three host surfaces and gives the host one choke
/// point to log, sandbox-by-convention, or stub in tests.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Version of <c>AbioticEditor.Plugins.Abstractions</c> the host was built against.
    /// A plugin can compare this with the SDK it compiled against to refuse to run against
    /// an incompatible host at <see cref="IAbioticPlugin.Configure"/> time.
    /// </summary>
    Version SdkVersion { get; }

    /// <summary>Version of the host application/CLI that loaded the plugin.</summary>
    Version HostVersion { get; }

    /// <summary>
    /// Identifies which surface loaded the plugin (<c>"gui"</c>, <c>"cli"</c>, or a test
    /// harness name). Lets a plugin tailor behaviour - e.g. an editor tool no-ops its
    /// CreateView path under the CLI - without taking a framework dependency.
    /// </summary>
    string HostKind { get; }

    /// <summary>Structured logger scoped to this plugin's id (writes to the editor log).</summary>
    IPluginLog Log { get; }

    /// <summary>
    /// The host application's UI surface - dialogs, toasts, running operations, navigation -
    /// so a plugin can interact with the app. In hosts without a UI (the CLI, tests) this is
    /// <see cref="NullHostUi"/> and the calls no-op, so plugin code is portable.
    /// </summary>
    IHostUi Ui { get; }

    /// <summary>
    /// A writable directory reserved for this plugin (created on first access). Use it for
    /// settings, caches, or downloaded data. Path is stable across runs and namespaced by
    /// plugin id so two plugins never collide.
    /// </summary>
    string DataDirectory { get; }
}
