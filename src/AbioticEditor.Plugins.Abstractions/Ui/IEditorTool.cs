namespace AbioticEditor.Plugins.Ui;

/// <summary>
/// A UI extension: a view the GUI hosts inside its Plugins panel (dynamic component
/// loading). Only GUI hosts ever call <see cref="CreateView"/>; the CLI and tests skip it.
///
/// <para>
/// The view is returned as <see cref="object"/> on purpose. That single seam is what keeps
/// this SDK free of any MAUI reference: the plugin returns a <c>Microsoft.Maui.Controls.View</c>
/// (the plugin, not the SDK, takes the MAUI dependency) and the GUI host casts it back. The
/// trade is a loss of compile-time view typing here in exchange for one tiny, stable SDK
/// that the CLI and Core can reference without pulling in the UI framework. The host
/// validates the cast and reports a clear error if a plugin returns the wrong type.
/// </para>
/// </summary>
public interface IEditorTool
{
    /// <summary>Stable id (kebab-case), unique within the plugin.</summary>
    string Id { get; }

    /// <summary>Title shown on the tool's tab/entry in the Plugins panel.</summary>
    string Title { get; }

    /// <summary>
    /// Optional single-glyph icon (e.g. an emoji or icon-font character) shown next to the
    /// title. Null for no glyph.
    /// </summary>
    string? Glyph => null;

    /// <summary>Where the host should surface the tool. Hosts may treat this as a hint.</summary>
    EditorToolPlacement Placement => EditorToolPlacement.Panel;

    /// <summary>
    /// Builds the view. Called by GUI hosts only, on the UI thread. Return a
    /// <c>Microsoft.Maui.Controls.View</c>. The host owns the returned view's lifetime and
    /// will dispose it (if disposable) when the tool closes. May be called again to rebuild
    /// after a theme change, so do not cache a single instance across calls.
    /// </summary>
    /// <param name="context">Access to host services and the currently open save, if any.</param>
    object CreateView(IEditorToolContext context);
}

/// <summary>Hint for where a UI tool wants to appear in the host shell.</summary>
public enum EditorToolPlacement
{
    /// <summary>A standalone panel/page opened from the Plugins list (default, always supported).</summary>
    Panel,

    /// <summary>A tab alongside the editor's own tabs, when the host supports it.</summary>
    Tab,

    /// <summary>A compact widget in a sidebar, when the host supports it.</summary>
    Sidebar,
}
