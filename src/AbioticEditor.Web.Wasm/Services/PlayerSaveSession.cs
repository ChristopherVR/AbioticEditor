using AbioticEditor.Core.PlayerSaves;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Holds the player save the browser host currently has open, so it survives navigation
/// between editor pages (each page renders a different slice of the same
/// <see cref="PlayerSaveData"/>).
/// </summary>
/// <remarks>
/// This is the browser counterpart of the desktop host's <c>PlayerSaveSession</c>, and keeps
/// the same staged-edit contract: pages call the Core <c>PlayerSaveWriter.Apply*</c> methods to
/// mutate the in-memory save tree and mark the session dirty, and nothing is serialized until
/// the user explicitly downloads. The difference is where the bytes end up - there is no file
/// system here, so "save" means handing the re-serialized bytes to the browser as a download.
/// Because <see cref="PlayerSaveData.Raw"/> IS the tree the reader parsed, everything the
/// editor did not touch round-trips byte-for-byte, exactly as it does in the desktop app.
/// </remarks>
public sealed class PlayerSaveSession(IJSRuntime js)
{
    /// <summary>Largest save this host will read. Real player saves are well under a megabyte.</summary>
    public const long MaximumFileSize = 64L * 1024 * 1024;

    private PlayerSaveData? _data;

    /// <summary>Raised whenever a save is opened, closed, or edited, so the shell can re-render.</summary>
    public event Action? Changed;

    /// <summary>The open save, or null when nothing is loaded.</summary>
    public PlayerSaveData? Data => _data;

    /// <summary>File name the save was opened from; reused as the download name.</summary>
    public string? FileName { get; private set; }

    /// <summary>True once any page has staged an edit that has not been downloaded yet.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True when a save is open and its pages should be reachable.</summary>
    public bool HasSave => _data is not null;

    /// <summary>
    /// Parses <paramref name="stream"/> as a player save and makes it the open document.
    /// Throws when the bytes are not an Abiotic Factor character save; the previously open
    /// save is left untouched in that case, so a mis-click cannot discard staged edits.
    /// </summary>
    public async Task LoadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        // PlayerSaveReader needs to seek, and a browser file stream does not support it.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var parsed = PlayerSaveReader.ReadFromStream(buffer);

        _data = parsed;
        FileName = fileName;
        IsDirty = false;
        Changed?.Invoke();
    }

    /// <summary>Closes the open save, discarding any staged edits.</summary>
    public void Close()
    {
        _data = null;
        FileName = null;
        IsDirty = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Marks the open save as edited and re-renders the shell. Pages call this immediately
    /// after a <c>PlayerSaveWriter.Apply*</c> call.
    /// </summary>
    public void MarkDirty()
    {
        if (_data is null) return;
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-serializes the open save and hands it to the browser as a download, then clears the
    /// dirty flag. The game reads the file by name, so the original file name is preserved -
    /// a browser that already has a file of that name will suffix the copy, and the player is
    /// expected to rename it back when placing it in their save folder.
    /// </summary>
    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (_data is null) return;

        using var output = new MemoryStream();
        _data.Raw.WriteTo(output);
        output.Position = 0;

        using var streamRef = new DotNetStreamReference(output, leaveOpen: true);
        await js.InvokeVoidAsync("downloadFileFromStream", cancellationToken, FileName, streamRef).ConfigureAwait(false);

        IsDirty = false;
        Changed?.Invoke();
    }
}
