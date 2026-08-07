using AbioticEditor.Ui;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Opens outside links from the browser host.
/// </summary>
/// <remarks>
/// A tab can open a URL, but it has no file manager to reveal a path in - and the "paths" this
/// host deals in are not local paths anyway (see <see cref="BrowserSaveFileSystem"/>). Screens
/// that offer "show this file on disk" check
/// <see cref="AbioticEditor.Web.Services.SaveWorkspaceSessionService.HasLocalPaths"/> and hide
/// the action here, so <see cref="RevealPathAsync"/> exists to satisfy the interface and does
/// nothing rather than pretending to succeed.
/// </remarks>
public sealed class BrowserNavigationService(IJSRuntime js) : IExternalNavigationService
{
    public async Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        // noopener/noreferrer: the editor links out to the wiki and to Steam, and neither needs
        // a handle back onto this tab.
        await js.InvokeVoidAsync("open", cancellationToken, url.ToString(), "_blank", "noopener,noreferrer")
            .ConfigureAwait(false);
    }

    public Task RevealPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
