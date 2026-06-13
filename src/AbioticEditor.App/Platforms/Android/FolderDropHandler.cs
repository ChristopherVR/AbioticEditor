using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Android implementation: intentionally a no-op. There is no desktop-style flow for
/// dragging a save folder onto a fullscreen app; the folder picker covers Android.
/// </summary>
public static partial class FolderDropHandler
{
    static partial void PlatformAttach(ContentPage page, MainViewModel vm)
    {
        // No-op: the folder picker is the supported way to open a save folder here.
    }
}
