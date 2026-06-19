using AbioticEditor.App.ViewModels;
using CommunityToolkit.Maui.Storage;

namespace AbioticEditor.App.Views;

/// <summary>Folder-picker flow shared by the header bar and the empty state.</summary>
public static class FolderPicking
{
    public static async Task PickAndLoadAsync(Element host, MainViewModel vm)
    {
        try
        {
            var result = await FolderPicker.PickAsync(CancellationToken.None);
            if (result.IsSuccessful && result.Folder is not null)
            {
                await vm.LoadFolderAsync(result.Folder.Path);
            }
            else if (result.Exception is { } ex && !IsCancellation(ex))
            {
                await ViewUtils.AlertAsync(host, Services.LocalizationResourceManager.Instance["FolderPick_Failed"], ex.Message);
            }
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            await ViewUtils.AlertAsync(host, Services.LocalizationResourceManager.Instance["FolderPick_Failed"], ex.Message);
        }
    }

    /// <summary>
    /// The toolkit reports a dismissed dialog as a failure carrying an exception -
    /// closing the picker without choosing is a normal outcome, not an error.
    /// </summary>
    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException ||
        ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);
}
