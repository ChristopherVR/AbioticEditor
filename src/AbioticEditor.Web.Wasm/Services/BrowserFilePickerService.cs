using AbioticEditor.Ui;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Browser-native <see cref="IFilePicker"/>/<see cref="IFolderPicker"/> backed by the File
/// System Access API (Chromium only - see <c>wwwroot/js/filePicker.js</c>). Selected bytes are
/// read into memory in JS and handed across the interop boundary; nothing is ever uploaded to a
/// server, since this host has none.
/// </summary>
public sealed class BrowserFilePickerService(IJSRuntime js, BrowserSaveFileSystem saveFiles) : IFilePicker, IFolderPicker
{
    public async Task<PickedFile?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        var files = await PickFilesAsync(request, cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    public async Task<IReadOnlyList<PickedFile>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        var picked = await js.InvokeAsync<PickedFileJs[]>(
            "abioticFilePicker.pickFiles", cancellationToken,
            request.Title, true, request.FileTypes);

        return picked
            .Select(f => new PickedFile(f.Name, null, _ => Task.FromResult<Stream>(new MemoryStream(f.Bytes, writable: false))))
            .ToArray();
    }

    /// <summary>
    /// Prompts for a save folder. This deliberately routes through
    /// <see cref="BrowserSaveFileSystem"/> rather than picking a folder of its own: the granted
    /// directory handle has to land in the registry that later reads and writes the saves, or
    /// the editor would open a folder it cannot then touch.
    /// </summary>
    /// <remarks>
    /// <para><see cref="PickedFolder.Path"/> carries the file system's folder identifier, which is
    /// what the caller hands to <c>SaveWorkspaceSessionService.OpenAsync</c>. It is not a local
    /// path - see <see cref="AbioticEditor.Web.Services.ISaveFileSystem"/>.</para>
    ///
    /// <para>A browser without the File System Access API (Firefox, Safari) falls back to opening
    /// the folder read-only. That is a real difference the player will notice - they save with
    /// EXPORT instead - but it is far better than the alternative, which until now was simply
    /// being unable to open anything at all.</para>
    /// </remarks>
    public async Task<PickedFolder?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await saveFiles.IsSupportedAsync().ConfigureAwait(false)
            ? await saveFiles.PickFolderAsync(cancellationToken).ConfigureAwait(false)
            : await saveFiles.UploadFolderAsync(cancellationToken).ConfigureAwait(false);
        return folder is null ? null : new PickedFolder(folder, folder);
    }

    private sealed record PickedFileJs(string Name, byte[] Bytes);
}
