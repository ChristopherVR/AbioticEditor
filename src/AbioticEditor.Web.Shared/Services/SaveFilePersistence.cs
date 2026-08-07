using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The single place an open editor session's save gets written back.
/// </summary>
/// <remarks>
/// Both session models (player and world) end their save the same way: re-serialize the raw
/// GVAS tree and put it where it came from. Which "where" that is now depends on the host, so
/// the choice lives here rather than being duplicated in each session.
/// </remarks>
internal static class SaveFilePersistence
{
    /// <summary>
    /// Writes <paramref name="save"/> to <paramref name="path"/> through
    /// <paramref name="files"/>, or straight to the local file system when no file system was
    /// supplied (the long-standing behaviour, which the tests rely on).
    /// </summary>
    public static async ValueTask WriteAsync(
        ISaveFileSystem? files,
        string path,
        SaveGame save,
        CancellationToken cancellationToken = default)
    {
        if (files is null)
        {
            SaveBackup.WriteWithBackup(path, save.WriteTo);
            return;
        }

        // Serializing into memory first is what lets a host that is not a file system take the
        // bytes. Saves are small enough for this to be unremarkable: the largest is the ~16 MB
        // Facility region save, and the desktop path immediately streams it back out to disk.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);

        try
        {
            await files.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            EditorLog.Error("Save", $"Failed to write {path}", ex);
            throw;
        }
    }
}
