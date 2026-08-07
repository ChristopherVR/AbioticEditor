using AbioticEditor.Ui;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Browser-native <see cref="IFilePicker"/>/<see cref="IFolderPicker"/> backed by the File
/// System Access API (Chromium only - see <c>wwwroot/js/filePicker.js</c>). Selected bytes are
/// read into memory in JS and handed across the interop boundary; nothing is ever uploaded to a
/// server, since this host has none.
/// </summary>
public sealed class BrowserFilePickerService(IJSRuntime js) : IFilePicker, IFolderPicker
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

    public async Task<PickedFolder?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default)
    {
        var picked = await js.InvokeAsync<PickedFolderJs?>("abioticFilePicker.pickFolder", cancellationToken, request.Title);
        return picked is null ? null : new PickedFolder(picked.Name, null);
    }

    private sealed record PickedFileJs(string Name, byte[] Bytes);

    private sealed record PickedFolderJs(string Name);
}
