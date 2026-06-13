using Microsoft.Maui.Controls;

namespace AbioticEditor.Samples.SaveInspector;

/// <summary>
/// Code-behind for the compiled XAML view. The host assigns the view-model via the
/// constructor, keeping the view passive (all state lives in <see cref="InspectorViewModel"/>).
/// </summary>
public partial class InspectorView : ContentView
{
    public InspectorView(InspectorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
