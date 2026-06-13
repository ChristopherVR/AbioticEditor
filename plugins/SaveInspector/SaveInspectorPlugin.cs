using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.Samples.SaveInspector;

/// <summary>
/// Entry point: registers the inspector UI tool. The host calls
/// <see cref="InspectorTool.CreateView"/> when the user opens it, and the tool wires up the
/// XAML view with its view-model.
/// </summary>
public sealed class SaveInspectorPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
        => registry.AddEditorTool(new InspectorTool());
}

/// <summary>
/// A full-MVVM UI tool: builds the compiled XAML <see cref="InspectorView"/> bound to an
/// <see cref="InspectorViewModel"/>. The host hosts whatever view this returns, with no
/// compile-time knowledge of either type.
/// </summary>
public sealed class InspectorTool : IEditorTool
{
    public string Id => "save-inspector";

    public string Title => "Inspector";

    public string Glyph => "🔬";

    public EditorToolPlacement Placement => EditorToolPlacement.Panel;

    public object CreateView(IEditorToolContext context)
        => new InspectorView(new InspectorViewModel(context));
}
