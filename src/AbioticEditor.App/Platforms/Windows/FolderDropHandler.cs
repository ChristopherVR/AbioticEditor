using AbioticEditor.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace AbioticEditor.App.Views;

/// <summary>
/// Windows implementation: reads the storage items off the WinUI drag event and loads
/// the first dropped folder, or the parent directory of the first dropped file.
/// </summary>
public static partial class FolderDropHandler
{
    static partial void PlatformAttach(ContentPage page, MainViewModel vm)
        => AttachRecognizer(page, vm, HandleDropAsync);

    private static async Task HandleDropAsync(MainViewModel vm, DropEventArgs e)
    {
        var dragEvent = e.PlatformArgs?.DragEventArgs;
        if (dragEvent is null) return;
        if (!dragEvent.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await dragEvent.DataView.GetStorageItemsAsync();
        var path = items.OfType<StorageFolder>().FirstOrDefault()?.Path
            ?? Path.GetDirectoryName(items.OfType<StorageFile>().FirstOrDefault()?.Path ?? string.Empty);

        await LoadPathAsync(vm, path);
    }
}
