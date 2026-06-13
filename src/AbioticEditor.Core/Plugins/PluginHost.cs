using System.Reflection;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Plugins;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Host-side implementation of <see cref="IPluginHost"/>, one per loaded plugin so logging
/// and the data directory are scoped to that plugin's id. Versions are shared across all
/// plugins (they describe the host, not the plugin).
/// </summary>
internal sealed class PluginHost : IPluginHost
{
    public PluginHost(string pluginId, string hostKind)
    {
        Log = new PluginLog(pluginId);
        DataDirectory = PluginPaths.DataDirectoryFor(pluginId);
        HostKind = hostKind;
    }

    /// <summary>
    /// The SDK version the host links against - read from the actual loaded
    /// <c>AbioticEditor.Plugins.Abstractions</c> assembly so it never drifts from reality.
    /// </summary>
    public Version SdkVersion { get; } =
        typeof(IAbioticPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    /// <summary>The host application's version (Core assembly version stands in for it).</summary>
    public Version HostVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(PluginHost).Assembly.GetName().Version
        ?? new Version(1, 0, 0, 0);

    public string HostKind { get; }

    public IPluginLog Log { get; }

    /// <summary>
    /// The app UI bridge. Pulled from <see cref="PluginHostEnvironment.HostUi"/> on each access
    /// so the GUI can install the real implementation after plugins have already loaded.
    /// </summary>
    public IHostUi Ui => PluginHostEnvironment.HostUi;

    public string DataDirectory { get; }
}

/// <summary>
/// Process-wide host environment for plugins. The GUI sets <see cref="HostUi"/> at startup so
/// every plugin's <see cref="IPluginHost.Ui"/> can drive the app; the CLI and tests leave the
/// default <see cref="NullHostUi"/> in place.
/// </summary>
public static class PluginHostEnvironment
{
    private static IHostUi _hostUi = NullHostUi.Instance;

    /// <summary>The UI bridge handed to plugins. Never null; defaults to a no-op.</summary>
    public static IHostUi HostUi
    {
        get => _hostUi;
        set => _hostUi = value ?? NullHostUi.Instance;
    }
}

/// <summary>
/// Routes a plugin's log calls to the shared <see cref="EditorLog"/> under the plugin's id,
/// so plugin output is interleaved with (and distinguishable from) the host's own log.
/// </summary>
internal sealed class PluginLog : IPluginLog
{
    private readonly string _area;

    public PluginLog(string pluginId) => _area = $"Plugin:{pluginId}";

    public void Info(string message) => EditorLog.Info(_area, message);

    public void Warn(string message) => EditorLog.Warn(_area, message);

    public void Error(string message, Exception? exception = null) => EditorLog.Error(_area, message, exception);
}
