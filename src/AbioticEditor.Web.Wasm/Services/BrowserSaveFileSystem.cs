using AbioticEditor.Web.Services;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Reaches the player's save folder through the browser's File System Access API.
/// </summary>
/// <remarks>
/// <para>The directory handles themselves live in JavaScript (they cannot cross the interop
/// boundary), so this type refers to files by the <c>"folderName/pathInsideFolder"</c>
/// identifiers <c>wwwroot/js/saveFileSystem.js</c> produces. They are deliberately NOT local
/// file-system paths, which is why <see cref="HasLocalPaths"/> is false and every feature that
/// would hand a path to something outside the editor is switched off on this host.</para>
///
/// <para>Chromium only. <see cref="IsSupportedAsync"/> is how the UI decides whether to offer
/// folder mode at all; Firefox and Safari get single-file open and download instead.</para>
/// </remarks>
public sealed class BrowserSaveFileSystem(IJSRuntime js) : ISaveFileSystem
{
    /// <summary>
    /// Ceiling for a single save read. The largest real save is the ~16 MB Facility region save;
    /// this leaves generous headroom while still refusing to pull something absurd into a tab.
    /// </summary>
    private const long MaximumSaveSize = 128L * 1024 * 1024;

    public bool HasLocalPaths => false;

    /// <summary>True when this browser can open a folder at all (Chromium at time of writing).</summary>
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("abioticSaveFs.isSupported");

    /// <summary>
    /// Prompts for a save folder and returns the identifier to hand to
    /// <see cref="ListSavesAsync"/>, or null when the player cancelled.
    /// </summary>
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
        => await js.InvokeAsync<string?>("abioticSaveFs.pickFolder", cancellationToken).ConfigureAwait(false);

    /// <summary>Raised when the player drops a save folder onto the window.</summary>
    public event Func<string, Task>? FolderDropped;

    /// <summary>
    /// Starts listening for a folder dropped onto the window. Safe to call more than once; the
    /// listener is only wired up the first time.
    /// </summary>
    public async Task ListenForDroppedFolderAsync(CancellationToken cancellationToken = default)
    {
        _dropReference ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("abioticSaveFs.listenForDroppedFolder", cancellationToken, _dropReference).ConfigureAwait(false);
    }

    /// <summary>Called from JavaScript once a dropped folder has been registered.</summary>
    [JSInvokable]
    public Task OnFolderDropped(string folder)
        => FolderDropped is { } handler ? handler(folder) : Task.CompletedTask;

    private DotNetObjectReference<BrowserSaveFileSystem>? _dropReference;

    public async Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default)
        => await js.InvokeAsync<bool>("abioticSaveFs.folderExists", cancellationToken, folder).ConfigureAwait(false);

    public async Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
    {
        var entries = await js.InvokeAsync<BrowserEntry[]>("abioticSaveFs.listSaves", cancellationToken, folder).ConfigureAwait(false);
        return entries
            .Select(entry => new SaveFileEntry(entry.Path, entry.RelativePath, entry.Name, entry.Length))
            .ToArray();
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => ReadStreamAsync("abioticSaveFs.readAll", cancellationToken, path);

    public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
        => ReadStreamAsync("abioticSaveFs.readHeader", cancellationToken, path, maxBytes);

    public async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        using var source = new MemoryStream(contents, writable: false);
        using var streamRef = new DotNetStreamReference(source, leaveOpen: true);
        await js.InvokeVoidAsync("abioticSaveFs.write", cancellationToken, path, streamRef).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls bytes back from JavaScript as a stream rather than a JSON array: a region save is
    /// megabytes, and base64 in both directions would dominate the time to open a world.
    /// </summary>
    private async Task<byte[]> ReadStreamAsync(string identifier, CancellationToken cancellationToken, params object?[] arguments)
    {
        var reference = await js.InvokeAsync<IJSStreamReference>(identifier, cancellationToken, arguments).ConfigureAwait(false);
        await using var stream = await reference.OpenReadStreamAsync(MaximumSaveSize, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed record BrowserEntry(string Path, string RelativePath, string Name, long Length);
}
