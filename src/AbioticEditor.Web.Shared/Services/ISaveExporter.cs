namespace AbioticEditor.Web.Services;

/// <summary>
/// Hands a file back to the player, outside whatever folder the editor is working in.
/// </summary>
/// <remarks>
/// <para>Two different needs meet here. In a browser the editor may be holding the only edited
/// copy of a save - a folder opened read-only, or a single file opened on a browser with no
/// folder support at all - so exporting is the ONLY way to get the result back. On the desktop
/// it is the ordinary "save me a copy" that the desktop app never had.</para>
///
/// <para>It also answers a problem the editor has on every host: a single action can change saves
/// the player never opened (setting a story chapter rewrites the Facility save and, with the
/// opt-in ticked, every player save). Being able to take the whole set away is what makes that
/// recoverable.</para>
/// </remarks>
public interface ISaveExporter
{
    /// <summary>
    /// True when this host can hand files back. Screens hide their export actions when false
    /// rather than offering something that does nothing.
    /// </summary>
    bool CanExport { get; }

    /// <summary>
    /// Delivers <paramref name="contents"/> to the player under <paramref name="fileName"/>.
    /// The name is a suggestion: a browser puts it in the downloads folder, and the player may
    /// be asked where it goes.
    /// </summary>
    Task ExportAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default);
}
