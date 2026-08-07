using AbioticEditor.Ui;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Picking the raw-JSON file to import, shared by the player and world RAW tabs. Import used to
/// accept only the JSON file exported beside the save, which meant an edited copy kept anywhere
/// else could not be put back; now the tabs ask which file to use.
/// </summary>
public static class RawJsonImport
{
    /// <summary>
    /// Asks for a JSON file and returns a local path the importer can read, plus whether that
    /// path is a temporary copy the caller should delete afterwards. Returns null when the
    /// user cancels.
    /// </summary>
    public static async Task<(string Path, bool Temporary)?> ChooseAsync(
        IFilePicker picker, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picker);
        var picked = await picker.PickFileAsync(
            new FilePickerRequest
            {
                Title = title,
                FileTypes = [new FileTypeFilter("Save JSON", [".json"])],
            },
            cancellationToken).ConfigureAwait(false);
        if (picked is null) return null;

        if (picked.Path is { Length: > 0 } local && File.Exists(local)) return (local, false);

        // A browser-sandbox selection has no path on this machine, so stage the bytes in a
        // temporary file and let the same validated importer read them from there.
        var staged = Path.Combine(Path.GetTempPath(), $"AbioticEditor-import-{Guid.NewGuid():N}.json");
        await using (var source = await picked.OpenReadAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(staged))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
        return (staged, true);
    }

    /// <summary>Removes a staged temporary copy once the import has finished with it.</summary>
    public static void CleanUp(string? path, bool temporary)
    {
        if (!temporary || string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stranded temp file is harmless; the operating system sweeps it up.
        }
    }
}
