namespace AbioticEditor.Plugins;

/// <summary>
/// The single entry point a plugin assembly exposes. The host scans a plugin's entry
/// assembly for exactly one public, parameterless-constructible type implementing this
/// interface, creates it, and calls <see cref="Configure"/> once to let the plugin
/// register its capabilities (save operations, console commands, editor tools).
///
/// <para>
/// Why one explicit entry point rather than scanning for every capability type: it keeps
/// activation deterministic (the plugin decides what is exposed and in what order), lets a
/// single DLL contribute across all three host surfaces, and gives the plugin a hook
/// (<see cref="Configure"/>) that runs with the <see cref="IPluginHost"/> in hand - the
/// only place it can read host services, its data directory, or the SDK version.
/// </para>
///
/// <para>
/// Threading: <see cref="Configure"/> is called once during load, before any capability
/// runs. Do only cheap, synchronous wiring here; defer real work to a capability's
/// Execute/Create method. Throwing from <see cref="Configure"/> fails just this plugin -
/// the host logs it and continues loading the others.
/// </para>
/// </summary>
public interface IAbioticPlugin
{
    /// <summary>
    /// Called once after the plugin is instantiated. Register capabilities on
    /// <paramref name="registry"/> and capture <paramref name="host"/> (or
    /// <see cref="IPluginRegistry.Host"/>) for later use.
    /// </summary>
    /// <param name="registry">Sink for the plugin's capabilities; also exposes the host.</param>
    /// <param name="host">Host services: logging, SDK/host versions, per-plugin data dir.</param>
    void Configure(IPluginRegistry registry, IPluginHost host);
}
