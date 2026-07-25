using Microsoft.AspNetCore.Components.Forms;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Imports browser-selected save files into a circuit-local temporary workspace.
/// Browsers deliberately do not expose a dropped folder's real path, so this is the
/// safe fallback for a remote or sandboxed Razor surface.
/// </summary>
public sealed class BrowserSaveImportService : IAsyncDisposable
{
    private const long MaximumFileSize = 128L * 1024 * 1024;
    private string? _importDirectory;

    public async Task<string> ImportAsync(IReadOnlyList<IBrowserFile> files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0) throw new ArgumentException("Drop or select one or more .sav files.", nameof(files));
        if (files.Count > 32) throw new ArgumentException("Import no more than 32 save files at once.", nameof(files));

        await ClearAsync().ConfigureAwait(false);
        _importDirectory = Path.Combine(Path.GetTempPath(), "AbioticEditor", "browser-imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_importDirectory);

        foreach (var file in files)
        {
            if (!Path.GetExtension(file.Name).Equals(".sav", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{file.Name} is not a .sav file.", nameof(files));
            if (file.Size > MaximumFileSize) throw new ArgumentException($"{file.Name} is larger than 128 MB.", nameof(files));

            var name = Path.GetFileName(file.Name);
            var destination = Path.Combine(_importDirectory, name);
            if (File.Exists(destination)) throw new ArgumentException($"The selection contains duplicate file name {name}.", nameof(files));

            await using var input = file.OpenReadStream(MaximumFileSize, cancellationToken);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        return _importDirectory;
    }

    public async ValueTask DisposeAsync() => await ClearAsync().ConfigureAwait(false);

    private Task ClearAsync()
    {
        if (_importDirectory is { } directory && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _importDirectory = null;
        return Task.CompletedTask;
    }
}
