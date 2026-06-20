using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.App;

/// <summary>
/// Thin modal wrapper around <see cref="PluginsPanel"/>, kept for the plugin host API
/// (<c>abiotic.ui.openPluginsPanel</c>). The same panel is shown inline in the settings
/// PLUGINS tab; this just hosts it in the shared facility chrome with a close button.
/// </summary>
public sealed class PluginsPage : ContentPage
{
    public PluginsPage(MainViewModel vm, Func<Task> rebuildHost)
    {
        _ = rebuildHost; // accepted for call-site compatibility; the panel needs no host rebuild
        Title = Services.LocalizationResourceManager.Instance["Plugins_Title"];
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];

        var cards = new PluginsPanel(this, vm).BuildCards();

        var close = ModalChrome.Button(Services.LocalizationResourceManager.Instance["Common_Close"], primary: false);
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        Content = ModalChrome.Scaffold(
            Services.LocalizationResourceManager.Instance["Settings_Plugins"],
            Services.LocalizationResourceManager.Instance["Plugins_Title"],
            cards,
            ModalChrome.Footer(close),
            maxWidth: 760);
    }
}

/// <summary>A simple modal page that hosts a plugin-provided view with a close button.</summary>
internal sealed class PluginToolHostPage : ModalCleanupPage
{
    private readonly View _toolView;
    private readonly IDisposable? _context;

    public PluginToolHostPage(string title, View toolView, IEditorToolContext context)
    {
        Title = title;
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        _toolView = toolView;
        _context = context as IDisposable;

        var close = new Button { Text = LocalizationResourceManager.Instance["Common_Close"], Margin = new Thickness(12) };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        Content = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            Children = { toolView, close },
        };
        Grid.SetRow(toolView, 0);
        Grid.SetRow(close, 1);
    }

    /// <summary>
    /// Disposes the tool context (severing the <c>ActiveSaveChanged</c> subscription a tool
    /// view-model holds) and the view / its view-model when the panel is genuinely closed - not
    /// when the tool pushes a modal over itself (see <see cref="ModalCleanupPage"/>) - so neither
    /// the tool nor the save it parsed outlives the page.
    /// </summary>
    protected override void OnModalRemoved()
    {
        _context?.Dispose();
        (_toolView.BindingContext as IDisposable)?.Dispose();
        (_toolView as IDisposable)?.Dispose();
    }
}
