using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.Plugins;

/// <summary>
/// The sink a plugin uses inside <see cref="IAbioticPlugin.Configure"/> to declare what it
/// contributes. A plugin may add any mix of capabilities (zero or more of each) - that is
/// how one DLL can be "a fix", "a CLI command", and "a UI panel" at once, which is exactly
/// the spread the editor wants to support.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>The host that owns this registration pass (same instance Configure received).</summary>
    IPluginHost Host { get; }

    /// <summary>
    /// Registers an operation that reads/edits a single save file. Available to the CLI
    /// (<c>plugins run</c>) and the GUI (the Plugins panel runs it against the open save).
    /// </summary>
    void AddSaveOperation(ISaveOperation operation);

    /// <summary>
    /// Registers a console command. The CLI exposes it as a first-class subcommand; hosts
    /// without a console (the GUI) simply ignore it.
    /// </summary>
    void AddConsoleCommand(IConsoleCommand command);

    /// <summary>
    /// Registers a UI tool (a view the GUI hosts in its Plugins panel). Hosts without a UI
    /// (the CLI) ignore it. The view object is created lazily and only by GUI hosts, so the
    /// MAUI dependency stays entirely inside the plugin and never reaches this SDK.
    /// </summary>
    void AddEditorTool(IEditorTool tool);

    /// <summary>
    /// Registers an HTML/web UI tool rendered in a web view (supports React and other web
    /// front-ends). GUI-only; the CLI ignores it.
    /// </summary>
    void AddWebTool(IWebTool tool);

    /// <summary>
    /// Registers a save upgrader: the host calls it when it can't otherwise read a save (a
    /// newer/unknown game version), so the plugin can up-convert the raw bytes. This is the
    /// "keep the editor working across game updates" extension point.
    /// </summary>
    void AddSaveUpgrader(ISaveUpgrader upgrader);

    /// <summary>
    /// Registers a click-to-run menu action. The GUI surfaces it as a menu item / button;
    /// the CLI ignores it.
    /// </summary>
    void AddMenuAction(IMenuAction action);

    /// <summary>
    /// Subscribes <paramref name="handler"/> to a host event (see <see cref="PluginEvents"/>).
    /// The host invokes it whenever that event fires - this is how a plugin "triggers when
    /// something happens" (a save opens, a file is written). Handlers must be quick and not
    /// throw; the host logs and isolates failures.
    /// </summary>
    void AddEventHandler(string eventName, Action<PluginEvent> handler);

    /// <summary>
    /// Contributes UI translations. <paramref name="strings"/> maps resource keys (the same
    /// keys the app's <c>AppResources.resx</c> uses, e.g. <c>Common_Save</c>) to their
    /// translated text for <paramref name="culture"/> (a culture code such as <c>de</c> or
    /// <c>pt-BR</c>). The host merges these into its string table, so a plugin can add a whole
    /// new language or override individual keys; GUI text bound via the localization system
    /// updates live. Hosts without a UI (the CLI) accept the call and ignore it.
    /// <para>
    /// This is the code path (a .NET or JavaScript plugin). The zero-code alternative is a
    /// <see cref="PluginRuntimes.Localization"/> plugin that just ships resx/json files - see
    /// <see cref="PluginManifest.Localizations"/>.
    /// </para>
    /// </summary>
    void AddLocalization(string culture, IReadOnlyDictionary<string, string> strings);
}
