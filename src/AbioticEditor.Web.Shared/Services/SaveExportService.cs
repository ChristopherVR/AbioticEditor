using System.IO.Compression;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Packages saves for the player to take away: one file on its own, or the whole open world as a
/// single zip.
/// </summary>
/// <remarks>
/// The whole-world export exists because of how the editor's cross-save actions work. Setting a
/// story chapter rewrites <c>WorldSave_Facility.sav</c>, and with "move players" ticked it
/// rewrites every <c>Player_*.sav</c> too - files the player never opened and may not think to
/// look for. Rather than trying to track exactly which files an action touched (and being wrong
/// about it), the export takes everything, which is always a correct answer and matches how a
/// player thinks about a save: one world, not a list of files.
/// </remarks>
public sealed class SaveExportService(ISaveFileSystem files, ISaveExporter exporter, SaveWorkspaceSessionService workspace)
{
    /// <summary>True when this host puts the EXPORT actions on screen at all.</summary>
    public bool OffersSaveExport => exporter.OffersSaveExport;

    /// <summary>Exports a single save under its own name.</summary>
    public async Task ExportSaveAsync(WorkspaceSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        var bytes = await files.ReadAllBytesAsync(save.Path, cancellationToken).ConfigureAwait(false);
        await exporter.ExportAsync(save.Name, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports every save in the open world as one zip, laid out the way the game's own folder is
    /// so it can be copied straight back over a save folder.
    /// </summary>
    /// <returns>How many saves went into the zip, and whether unsaved edits were left out of it.</returns>
    /// <remarks>
    /// The zip is built by reading the saves back, so anything still staged in an open editor is
    /// NOT in it unless it has been written first. On a read-only folder that write is free (it
    /// only updates the copy held in this tab) so it happens automatically. On a normal folder it
    /// must not: the player asked to export, not to save. There they are told instead, because an
    /// export that quietly omits the edit they just made is the worst possible outcome.
    /// </remarks>
    public async Task<(int Files, bool UnsavedEditsOmitted)> ExportWorkspaceAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (workspace.Current is not { } current) return (0, false);

        var unsavedOmitted = false;
        if (workspace.HasStagedEdits)
        {
            if (workspace.CanWrite) unsavedOmitted = true;
            else await workspace.FlushStagedEditsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Built in memory: the whole point is hosts with no scratch disk. A world is tens of
        // megabytes, which a tab holds comfortably, and the saves are already compressed-ish so
        // the entries are stored rather than deflated to keep the packing cheap.
        using var buffer = new MemoryStream();
        var written = 0;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var save in current.Saves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(save.Name);
                try
                {
                    var bytes = await files.ReadAllBytesAsync(save.Path, cancellationToken).ConfigureAwait(false);
                    var entry = archive.CreateEntry(EntryNameFor(save), CompressionLevel.NoCompression);
                    await using var stream = entry.Open();
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    written++;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // One unreadable save must not cost the player the other sixty-one.
                    EditorLog.Warn("Export", $"Left '{save.Name}' out of the export; it could not be read.", exception);
                }
            }
        }

        await exporter.ExportAsync(FileNameFor(current), buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        return (written, unsavedOmitted);
    }

    /// <summary>
    /// Where a save sits inside the zip. Its location within the opened folder is preserved so
    /// player saves land back under <c>PlayerData/</c> where the game expects them.
    /// </summary>
    private static string EntryNameFor(WorkspaceSave save)
        => string.IsNullOrWhiteSpace(save.RelativePath) ? save.Name : save.RelativePath.Replace('\\', '/');

    /// <summary>
    /// Names the zip after the world, since that is what the player calls it. The opened folder
    /// is the world: a real path on the desktop, already just the folder's name in a browser.
    /// </summary>
    private static string FileNameFor(SaveWorkspace workspace)
    {
        var folder = workspace.GamePass?.WorldName ?? workspace.WorldFolder;
        var name = folder.Replace('\\', '/').TrimEnd('/');
        var lastSeparator = name.LastIndexOf('/');
        if (lastSeparator >= 0) name = name[(lastSeparator + 1)..];
        if (string.IsNullOrWhiteSpace(name)) name = "abiotic-saves";
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return $"{name}.zip";
    }
}
