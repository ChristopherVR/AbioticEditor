namespace AbioticEditor.Web.Services;

/// <summary>
/// Hands a file back to the player, outside whatever folder the editor is working in.
/// </summary>
/// <remarks>
/// <para>Two different needs meet here. In a browser the editor may be holding the only edited
/// copy of a save - a folder opened read-only, or a single file opened on a browser with no
/// folder support at all - so handing it back is the ONLY way to get the result out. On the
/// desktop the same mechanism is what carries a side-car file (the raw JSON view, an appearance
/// file loaded from outside the world folder) to somewhere the player can find it.</para>
///
/// <para>It also answers a problem the browser editor has: a single action can change saves the
/// player never opened (setting a story chapter rewrites the Facility save and, with the opt-in
/// ticked, every player save). Being able to take the whole set away is what makes that
/// recoverable.</para>
/// </remarks>
public interface ISaveExporter
{
    /// <summary>
    /// True when the editor's EXPORT actions - the whole world as a zip, and a single save on its
    /// own - belong on this host. Screens hide those actions when it is false.
    /// </summary>
    /// <remarks>
    /// Only the browser build offers them. There, a save the player edited exists nowhere but in
    /// the tab until it is downloaded, so EXPORT is the way work reaches the game and has to be
    /// on screen. The desktop writes straight into the game's own save folder, so the same button
    /// would only ever produce a second copy of files the player already has, and it is left off
    /// on purpose rather than because it could not be built. Handing back one file through
    /// <see cref="ExportAsync"/> still works on both, which is what the raw-data and appearance
    /// downloads use.
    /// </remarks>
    bool OffersSaveExport { get; }

    /// <summary>
    /// Delivers <paramref name="contents"/> to the player under <paramref name="fileName"/>.
    /// The name is a suggestion: a browser puts it in the downloads folder, and the player may
    /// be asked where it goes.
    /// </summary>
    Task ExportAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default);
}
