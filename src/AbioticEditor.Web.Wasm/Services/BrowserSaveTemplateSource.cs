using AbioticEditor.Web.Services;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Fetches the blank save templates from the app's own static files.
/// </summary>
/// <remarks>
/// The desktop host reads these off disk beside its executable. There is no disk here, so they
/// ship as ordinary published assets under <c>Templates/</c> and are fetched over HTTP from the
/// page's own origin - no network call leaves the machine the app was loaded from.
/// </remarks>
public sealed class BrowserSaveTemplateSource(HttpClient http) : ISaveTemplateSource
{
    public async Task<byte[]> ReadTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"Templates/{name}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The bundled {name} template is unavailable. Reload the page and try again.");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
