using AbioticEditor.Web.Services;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Downloads the diagnostics log, since a browser tab has no folder to reveal.
/// </summary>
/// <remarks>
/// The log really does exist - <c>EditorLog</c> writes it into the in-memory file system
/// WebAssembly gives the app - it just is not anywhere the player can browse to. Handing it over
/// as a download is the only way it can reach a bug report.
/// </remarks>
public sealed class BrowserLogDelivery(IJSRuntime js) : IDiagnosticsLogDelivery
{
    public bool RevealsFolder => false;

    public async Task DeliverAsync(string logDirectory, string currentLogPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(currentLogPath))
            throw new InvalidOperationException("There is no log yet. Turn on detailed logging, reproduce the problem, then export.");

        // Copied into memory first: the log is being appended to as this runs, and handing the
        // live file to the download would risk a torn read.
        var bytes = await File.ReadAllBytesAsync(currentLogPath, cancellationToken).ConfigureAwait(false);
        using var source = new MemoryStream(bytes, writable: false);
        using var streamRef = new DotNetStreamReference(source, leaveOpen: true);
        await js.InvokeVoidAsync("downloadFileFromStream", cancellationToken, Path.GetFileName(currentLogPath), streamRef)
            .ConfigureAwait(false);
    }
}
