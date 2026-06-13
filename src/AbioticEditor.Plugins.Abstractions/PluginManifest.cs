namespace AbioticEditor.Plugins;

/// <summary>
/// The metadata in a plugin's <c>plugin.json</c>, sitting next to its DLL. The host reads
/// this WITHOUT loading the assembly, so it can list, validate, version-check, and
/// enable/disable plugins cheaply and before any third-party code runs - the manifest is
/// the trust gate in front of <see cref="System.Reflection.Assembly"/> loading.
///
/// <para>
/// The record lives in the SDK (not the host) so plugin authors, the host, and tooling all
/// agree on one shape. Parsing/validation lives in the host (AbioticEditor.Core), which is
/// the only side that touches disk.
/// </para>
/// </summary>
public sealed record PluginManifest
{
    /// <summary>
    /// Globally-unique, stable id in reverse-DNS or kebab form, e.g.
    /// <c>com.example.max-skills</c>. Names the data directory and is how every host refers
    /// to the plugin. Required.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable name. Required.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Semantic version of the plugin itself, e.g. <c>1.2.0</c>. Required.</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>Author or organisation. Optional.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>One or two sentences shown in the plugin list. Optional but recommended.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Which runtime loads this plugin: <see cref="PluginRuntimes.DotNet"/> (a managed .dll,
    /// the default) or <see cref="PluginRuntimes.JavaScript"/> (a .js file run on the bundled
    /// Jint engine). Determines whether <see cref="EntryAssembly"/> or
    /// <see cref="EntryScript"/> is used.
    /// </summary>
    public string Runtime { get; init; } = PluginRuntimes.DotNet;

    /// <summary>
    /// File name of the managed assembly to load, relative to the manifest's folder, e.g.
    /// <c>MaxSkills.dll</c>. Required for <see cref="PluginRuntimes.DotNet"/> plugins.
    /// </summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>
    /// File name of the JavaScript entry script, relative to the manifest's folder, e.g.
    /// <c>plugin.js</c>. Required for <see cref="PluginRuntimes.JavaScript"/> plugins.
    /// </summary>
    public string EntryScript { get; init; } = string.Empty;

    /// <summary>
    /// Minimum <c>AbioticEditor.Plugins.Abstractions</c> (SDK) version this plugin needs.
    /// The host refuses to load a plugin whose requirement exceeds the host's SDK version,
    /// turning a hard <see cref="MissingMethodException"/> at runtime into a clear, early
    /// "needs a newer editor" message. Optional; defaults to <c>0.0.0</c> (no floor).
    /// </summary>
    public string MinHostVersion { get; init; } = "0.0.0";

    /// <summary>
    /// Capabilities the plugin claims to provide (<c>"saveOperation"</c>,
    /// <c>"consoleCommand"</c>, <c>"editorTool"</c>). Advisory: used for the list UI and to
    /// let a host skip loading a plugin that offers nothing it can use (e.g. the CLI skips a
    /// UI-only plugin). The actual capabilities come from what <see cref="IAbioticPlugin"/>
    /// registers. See <see cref="PluginCapabilities"/>.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether the plugin is enabled. The host honours this and lets the user toggle it
    /// (persisted back to the manifest). A disabled plugin is listed but never loaded.
    /// Defaults to true.
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Canonical values for <see cref="PluginManifest.Runtime"/>.</summary>
public static class PluginRuntimes
{
    /// <summary>A managed .NET assembly (the default).</summary>
    public const string DotNet = "dotnet";

    /// <summary>A JavaScript file run on the host's bundled engine.</summary>
    public const string JavaScript = "javascript";
}

/// <summary>Canonical capability tokens used in <see cref="PluginManifest.Capabilities"/>.</summary>
public static class PluginCapabilities
{
    /// <summary>Provides one or more <see cref="Saves.ISaveOperation"/>.</summary>
    public const string SaveOperation = "saveOperation";

    /// <summary>Provides one or more <see cref="Cli.IConsoleCommand"/>.</summary>
    public const string ConsoleCommand = "consoleCommand";

    /// <summary>Provides one or more <see cref="Ui.IEditorTool"/>.</summary>
    public const string EditorTool = "editorTool";

    /// <summary>Provides one or more <see cref="Ui.IWebTool"/> (HTML/React UI).</summary>
    public const string WebTool = "webTool";

    /// <summary>Provides one or more <see cref="Ui.IMenuAction"/>.</summary>
    public const string MenuAction = "menuAction";

    /// <summary>Subscribes to one or more host events (see <see cref="Events.PluginEvents"/>).</summary>
    public const string EventHandler = "eventHandler";

    /// <summary>Provides one or more <see cref="Saves.ISaveUpgrader"/>.</summary>
    public const string SaveUpgrader = "saveUpgrader";
}
