using AbioticEditor.Web.Services;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Hands a file back as a browser download.
/// </summary>
/// <remarks>
/// Works in every browser, which is the point: the folder APIs the editor prefers are Chromium
/// only, so on Firefox and Safari this is the only way a player gets an edited save back out.
/// </remarks>
public sealed class BrowserSaveExporter(IJSRuntime js) : ISaveExporter
{
    public bool CanExport => true;

    public async Task ExportAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        // Streamed rather than passed as a JSON array, for the same reason saves are read that
        // way: a whole-world zip is tens of megabytes and base64 would dominate the wait.
        using var source = new MemoryStream(contents, writable: false);
        using var streamRef = new DotNetStreamReference(source, leaveOpen: true);
        await js.InvokeVoidAsync("downloadFileFromStream", cancellationToken, fileName, streamRef).ConfigureAwait(false);
    }
}
