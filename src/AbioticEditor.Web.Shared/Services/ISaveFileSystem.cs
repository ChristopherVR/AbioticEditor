namespace AbioticEditor.Web.Services;

/// <summary>
/// Where the editor's save files come from and go back to.
/// </summary>
/// <remarks>
/// <para>The desktop host reads and writes the player's real save folder with
/// <see cref="System.IO"/>. The browser host has no such access: it works through the browser's
/// own file APIs, where the closest thing to a folder is a directory handle the player granted,
/// and the closest thing to a path is an entry name inside it. Both shapes fit the same small
/// surface, which is what lets <see cref="SaveWorkspaceSessionService"/> - and the eighteen
/// components that inject it - be shared instead of duplicated.</para>
///
/// <para><c>path</c> here is an opaque identifier, NOT necessarily a local file-system path.
/// It is whatever <see cref="ListSavesAsync"/> handed back, and only the implementation that
/// produced it may interpret it. Anything that genuinely needs a real local path (revealing a
/// file in the OS file manager, Game Pass container packing, the JSON side-car bridge) must
/// check <see cref="HasLocalPaths"/> first and offer the feature only where it is true.</para>
/// </remarks>
public interface ISaveFileSystem
{
    /// <summary>
    /// True when the <c>path</c> values from this file system are real local file-system paths
    /// that other tools can open. False in the browser, where they are handle-relative names.
    /// Gate any feature that hands a path to something outside the editor on this.
    /// </summary>
    bool HasLocalPaths { get; }

    /// <summary>True when <paramref name="folder"/> is present and readable.</summary>
    Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <c>.sav</c> under <paramref name="folder"/>, searched recursively - a world folder
    /// keeps its player saves in a <c>PlayerData</c> subfolder.
    /// </summary>
    Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Reads a save's full contents.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from the start of a save, or the whole file if
    /// it is smaller. Opening a folder identifies every save from its header alone, and a region
    /// save can be 16 MB, so discovery must never pull whole files into memory just to read the
    /// few dozen bytes that say what each one is.
    /// </summary>
    Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a save's contents, first keeping the previous contents as a <c>.bak</c> beside
    /// it. Implementations must not leave a truncated file behind if the write fails partway:
    /// the editor's promise is that one bad save can always be undone.
    /// </summary>
    Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default);
}

/// <summary>One save file discovered in a workspace folder.</summary>
/// <param name="Path">
/// Identifier to pass back to <see cref="ISaveFileSystem"/>. A real path only when the file
/// system reports <see cref="ISaveFileSystem.HasLocalPaths"/>.
/// </param>
/// <param name="RelativePath">Location within the opened folder, for display and grouping.</param>
/// <param name="Name">File name on its own.</param>
/// <param name="Length">Size in bytes.</param>
public sealed record SaveFileEntry(string Path, string RelativePath, string Name, long Length);
