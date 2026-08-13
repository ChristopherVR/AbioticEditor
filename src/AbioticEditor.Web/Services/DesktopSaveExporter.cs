
namespace AbioticEditor.Web.Services;

/// <summary>
/// Writes an exported file into the machine's Downloads folder and opens it in the file manager,
/// so the desktop's "save me a copy" lands somewhere the player will actually find it.
/// </summary>
/// <remarks>
/// <para>Deliberately not a Save-As dialog: the desktop host's picker abstraction only opens files
/// and folders, and inventing a save dialog here would mean new native code on two platforms for a
/// convenience. Downloads is where a browser would have put it anyway, so both hosts behave the
/// same way from the player's point of view.</para>
///
/// <para>What comes through here on the desktop is the side-car files: the raw JSON view of a
/// save, and an appearance file opened from outside the world folder. Not whole saves - the
/// desktop already writes those into the game's own folder.</para>
/// </remarks>
public sealed class DesktopSaveExporter(AbioticEditor.Ui.IExternalNavigationService externalNavigation) : ISaveExporter
{
    /// <summary>
    /// False: EXPORT is a browser-only action.
    /// </summary>
    /// <remarks>
    /// The desktop saves into the game's real save folder, so a zip of the world, or a copy of one
    /// save, is a duplicate of files the player already has in a place they already know. Offering
    /// it here only raised the question of which copy the game was reading. The browser is the
    /// host where it is not a duplicate but the only way out, and that is where it stays.
    /// </remarks>
    public bool OffersSaveExport => false;

    public async Task ExportAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var directory = DownloadsDirectory();
        Directory.CreateDirectory(directory);
        var path = Unique(Path.Combine(directory, fileName));

        await File.WriteAllBytesAsync(path, contents, cancellationToken).ConfigureAwait(false);
        await externalNavigation.RevealPathAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The user's Downloads folder. .NET has no special folder for it, so it is composed from the
    /// profile folder, which is right on Windows and on the Linux default.
    /// </summary>
    private static string DownloadsDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>
    /// Never overwrite: an export is a copy the player asked to keep, and silently replacing last
    /// week's copy of the same world would be the one mistake this feature must not make.
    /// </summary>
    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
