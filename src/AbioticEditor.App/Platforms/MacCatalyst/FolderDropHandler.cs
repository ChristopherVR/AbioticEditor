using AbioticEditor.App.ViewModels;
using Foundation;

namespace AbioticEditor.App.Views;

/// <summary>
/// Mac Catalyst implementation: pulls file URLs out of the UIKit drop session. Finder
/// hands a dropped folder over as a file URL whose <c>HasDirectoryPath</c> is true; a
/// dropped file resolves to its parent directory. The load runs inside the URL's
/// security-scoped resource access so sandboxed file reads succeed.
/// </summary>
public static partial class FolderDropHandler
{
    /// <summary>Uniform type identifier for file URLs (kUTTypeFileURL).</summary>
    private const string FileUrlTypeIdentifier = "public.file-url";

    static partial void PlatformAttach(ContentPage page, MainViewModel vm)
        => AttachRecognizer(page, vm, HandleDropAsync);

    private static async Task HandleDropAsync(MainViewModel vm, DropEventArgs e)
    {
        var session = e.PlatformArgs?.DropSession;
        if (session is null) return;

        foreach (var item in session.Items)
        {
            var provider = item.ItemProvider;
            if (!provider.HasItemConformingTo(FileUrlTypeIdentifier)) continue;

            if (await provider.LoadItemAsync(FileUrlTypeIdentifier, null) is not NSUrl url) continue;

            var scoped = url.StartAccessingSecurityScopedResource();
            try
            {
                var path = url.Path;
                if (string.IsNullOrEmpty(path)) continue;

                await LoadPathAsync(vm, url.HasDirectoryPath ? path : Path.GetDirectoryName(path));
                return;
            }
            finally
            {
                if (scoped)
                {
                    url.StopAccessingSecurityScopedResource();
                }
            }
        }
    }
}
