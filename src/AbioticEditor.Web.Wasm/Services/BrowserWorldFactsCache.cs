using AbioticEditor.Web.Services;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Keeps what reading a world save produced in the browser's own storage, so the next visit does
/// not pay for it again.
/// </summary>
/// <remarks>
/// Parsing the ~13 MB facility save costs about a quarter of a second outside a browser and over
/// five seconds inside one, on the single thread the page also draws with. That first read cannot
/// be avoided; every one after it can. Entries are keyed by the save's version stamp, so a save
/// the player has since edited never matches its old entry.
/// </remarks>
public sealed class BrowserWorldFactsCache(IJSRuntime js) : IWorldFactsCache
{
    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
        => await js.InvokeAsync<string?>("abioticSaveFs.worldFactsGet", cancellationToken, key).ConfigureAwait(false);

    public Task WriteAsync(string key, string json, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync("abioticSaveFs.worldFactsPut", cancellationToken, key, json).AsTask();
}
