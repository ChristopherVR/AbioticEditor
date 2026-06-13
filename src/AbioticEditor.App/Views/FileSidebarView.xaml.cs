using AbioticEditor.App.ViewModels;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.App.Views;

/// <summary>Left pane: grouped save-file list + config (ini) files. Pure bindings,
/// plus a right-click "reveal in file manager" context action per file row.</summary>
public partial class FileSidebarView : ContentView
{
    public FileSidebarView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Context-menu "Open in Explorer/Finder" for both save rows (SaveFileSummary) and
    /// config rows (ConfigFileOption); the flyout item inherits the row's BindingContext.
    /// </summary>
    private void OnRevealFileClicked(object? sender, EventArgs e)
    {
        var path = (sender as MenuFlyoutItem)?.BindingContext switch
        {
            SaveFileSummary save => save.FullPath,
            ConfigFileOption config => config.File.FullPath,
            _ => null,
        };
        FileRevealer.Reveal(path);
    }

    /// <summary>
    /// Strips the WinUI TextBox's own border + light fill (and its hover/focus brushes) so
    /// the search field reads as part of the dark rounded container instead of a pale box.
    /// </summary>
    private void OnSearchEntryHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (sender is Entry { Handler.PlatformView: Microsoft.UI.Xaml.Controls.TextBox tb })
        {
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tb.Background = clear;
            tb.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            tb.Padding = new Microsoft.UI.Xaml.Thickness(0);
            // Keep the field transparent in every visual state (pointer-over / focused
            // otherwise repaint the default pale background).
            tb.Resources["TextControlBackground"] = clear;
            tb.Resources["TextControlBackgroundPointerOver"] = clear;
            tb.Resources["TextControlBackgroundFocused"] = clear;
            tb.Resources["TextControlBorderBrush"] = clear;
            tb.Resources["TextControlBorderBrushPointerOver"] = clear;
            tb.Resources["TextControlBorderBrushFocused"] = clear;
        }
#endif
    }

    /// <summary>
    /// "+" beside the PLAYERS header. The button lives in a group-header DataTemplate (its
    /// own namescope), so the command is invoked from code via the view's BindingContext
    /// rather than an x:Reference binding across the template boundary.
    /// </summary>
    private void OnAddPlayerTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is MainViewModel vm && vm.AddPlayerCommand.CanExecute(null))
        {
            vm.AddPlayerCommand.Execute(null);
        }
    }
}
