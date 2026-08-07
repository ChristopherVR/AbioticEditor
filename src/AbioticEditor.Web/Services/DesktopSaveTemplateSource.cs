namespace AbioticEditor.Web.Services;

/// <summary>
/// Reads the blank save templates from the <c>Templates</c> folder shipped beside the
/// executable (see the csproj items that copy them there).
/// </summary>
/// <remarks>
/// Replaces the direct <c>IWebHostEnvironment.ContentRootPath</c> lookup
/// <see cref="CreateWorldService"/> used to do, so that service - and the create-world screen
/// over it - can be shared with the browser host, which has no content root to read.
/// </remarks>
public sealed class DesktopSaveTemplateSource(IWebHostEnvironment environment) : ISaveTemplateSource
{
    public async Task<byte[]> ReadTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(environment.ContentRootPath, "Templates", name);
        if (!File.Exists(path))
            throw new InvalidOperationException($"The bundled {name} template is unavailable. Reinstall the editor.");
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
