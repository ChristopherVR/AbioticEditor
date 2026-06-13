using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Wires folder drag-and-drop onto the main page. The shared part owns the gesture
/// plumbing (DragOver accepts a Copy, Drop loads the resolved folder or reports the
/// failure on <see cref="MainViewModel.StatusMessage"/>); each platform supplies the
/// drop handling itself. Windows and Mac Catalyst extract a folder path from the
/// native drop payload; Android and iOS are no-ops because dragging a folder onto a
/// fullscreen app is not a meaningful flow there - the folder picker covers them.
/// </summary>
public static partial class FolderDropHandler
{
    /// <summary>
    /// Attaches drop handling to the root layout of <paramref name="page"/> on the
    /// platforms that support dropping folders onto the window.
    /// </summary>
    /// <param name="page">The page whose root layout receives the drop gesture.</param>
    /// <param name="vm">Receives the dropped folder via <see cref="MainViewModel.LoadFolderGuardedAsync"/>.</param>
    public static void Attach(ContentPage page, MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(vm);
        PlatformAttach(page, vm);
    }

    static partial void PlatformAttach(ContentPage page, MainViewModel vm);

#if WINDOWS || MACCATALYST
    /// <summary>
    /// Desktop plumbing shared by the Windows and Mac Catalyst implementations:
    /// accepts the drag-over as a Copy and routes the drop to the platform handler.
    /// Exceptions land in <see cref="MainViewModel.StatusMessage"/> instead of
    /// escaping the platform drop callback.
    /// </summary>
    private static void AttachRecognizer(ContentPage page, MainViewModel vm, Func<MainViewModel, DropEventArgs, Task> handleDropAsync)
    {
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Copy;
        drop.Drop += async (_, e) =>
        {
            try
            {
                await handleDropAsync(vm, e);
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"Could not load the dropped folder: {ex.Message}";
            }
        };
        if (page.Content is Layout root)
        {
            root.GestureRecognizers.Add(drop);
        }
    }

    /// <summary>
    /// Loads <paramref name="path"/> (a folder, or the parent directory already
    /// resolved from a dropped file) into the editor; empty paths are ignored.
    /// </summary>
    private static async Task LoadPathAsync(MainViewModel vm, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        await vm.LoadFolderGuardedAsync(path);
    }
#endif
}
