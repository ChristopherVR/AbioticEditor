namespace AbioticEditor.Plugins.Ui;

/// <summary>
/// A command surfaced as a menu item / action button in the GUI. Unlike an
/// <see cref="IEditorTool"/> (which hosts a whole view), a menu action is a single
/// click-to-run entry - the lightweight way for a plugin to add a verb to the UI. Hosts
/// without menus (the CLI) ignore these.
///
/// <para>
/// A menu action takes no MAUI dependency, so it lives in the host-agnostic SDK and can be
/// registered by managed and JavaScript plugins alike.
/// </para>
/// </summary>
public interface IMenuAction
{
    /// <summary>Stable id (kebab-case), unique within the plugin.</summary>
    string Id { get; }

    /// <summary>Menu text, e.g. "Export skills to CSV".</summary>
    string Title { get; }

    /// <summary>Optional single-glyph icon shown next to the title.</summary>
    string? Glyph => null;

    /// <summary>
    /// Optional sub-menu/grouping label, so several actions from one plugin can nest under a
    /// shared heading. Null places the action at the top level of the plugin menu.
    /// </summary>
    string? Group => null;

    /// <summary>Runs the action. Invoked on the UI thread.</summary>
    Task InvokeAsync(IMenuActionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context passed to <see cref="IMenuAction.InvokeAsync"/>.</summary>
public interface IMenuActionContext
{
    /// <summary>Host services (logging, data directory, versions).</summary>
    IPluginHost Host { get; }

    /// <summary>Absolute path of the save the editor currently has open, or null.</summary>
    string? ActiveSavePath { get; }

    /// <summary>Detected kind of the open save, or null when none is open.</summary>
    SaveKind? ActiveSaveKind { get; }

    /// <summary>
    /// Shows a short message to the user (host-implemented; a dialog in the GUI). Lets a menu
    /// action report a result without taking a UI dependency.
    /// </summary>
    Task NotifyAsync(string message);
}
