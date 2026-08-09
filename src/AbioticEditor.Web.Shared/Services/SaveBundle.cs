using System.IO.Compression;

namespace AbioticEditor.Web.Services;

/// <summary>How far unpacking a zip has got, for the "please wait" line on screen.</summary>
/// <param name="Done">Files unpacked so far, counting the one in progress.</param>
/// <param name="Total">Files to unpack in all.</param>
/// <param name="CurrentFile">The file being unpacked, without its folder.</param>
public sealed record SaveBundleProgress(int Done, int Total, string CurrentFile);

/// <summary>A world unpacked from a zip: what to call it, and every save inside it.</summary>
/// <param name="Name">The world's name, for the sidebar and the file identifiers built from it.</param>
/// <param name="Saves">Save contents keyed by their path within the world folder, using '/'.</param>
public sealed record SaveBundleContents(string Name, IReadOnlyDictionary<string, byte[]> Saves);

/// <summary>
/// Reads a zipped save folder back into something the editor can open.
/// </summary>
/// <remarks>
/// The other end of EXPORT. A browser hands the whole world back as one zip because that is the
/// only shape a tab can deliver, and a player who then wants to carry on editing it - or who was
/// sent one by whoever they play with - had no way back in: the editor would only take a folder,
/// and only from a browser that has the folder picker at all. Accepting the zip closes that loop
/// and works in every browser.
/// </remarks>
public static class SaveBundle
{
    /// <summary>How much unpacked save data one bundle may hold, so a hostile zip cannot fill the tab.</summary>
    private const long MaximumUnpackedBytes = 512L * 1024 * 1024;

    /// <summary>
    /// How much of one save to decompress before letting the page draw. Small enough that the
    /// biggest save in the game cannot stall a frame for long, large enough that the pauses do
    /// not dominate: a 16 MB save costs sixteen of them.
    /// </summary>
    private const int ChunkBytes = 1024 * 1024;

    /// <summary>
    /// Unpacks the saves out of <paramref name="zip"/>.
    /// </summary>
    /// <param name="fallbackName">
    /// What to call the world when the zip's own layout does not name it - normally the zip's
    /// file name, which is what EXPORT names after the world in the first place.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The file is not a readable zip, or holds no save at all.
    /// </exception>
    public static SaveBundleContents Read(Stream zip, string fallbackName)
        => ReadAsync(zip, fallbackName).GetAwaiter().GetResult();

    /// <summary>
    /// Unpacks the saves out of <paramref name="zip"/>, giving the page a chance to draw between
    /// files and telling <paramref name="progress"/> how far along it is.
    /// </summary>
    /// <remarks>
    /// The yielding is the whole point of this overload. A browser runs the editor on the one
    /// thread it also draws with, so unpacking a real world - sixty-odd files, seventy megabytes -
    /// locked the tab solid for several seconds with nothing on screen to say why. Handing control
    /// back between files costs a few milliseconds and turns a frozen page into a moving one.
    /// </remarks>
    public static async Task<SaveBundleContents> ReadAsync(
        Stream zip,
        string fallbackName,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zip);

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entries = archive.Entries
            .Where(entry => entry.Name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length == 0)
        {
            throw new InvalidDataException(
                "That zip holds no save files. Zip up the world folder itself - the one with "
                + "WorldSave_*.sav files and a PlayerData folder in it - or use the zip the editor's "
                + "own EXPORT hands you.");
        }

        var total = entries.Sum(entry => entry.Length);
        if (total > MaximumUnpackedBytes)
        {
            throw new InvalidDataException(
                "That zip unpacks to more than a browser tab can hold. Zip a single world folder "
                + "rather than a whole save library.");
        }

        var paths = entries.Select(entry => Normalize(entry.FullName)).ToArray();
        var worldFolder = SharedTopFolder(paths);

        var saves = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var relative = worldFolder is null ? paths[index] : paths[index][(worldFolder.Length + 1)..];

            progress?.Report(new SaveBundleProgress(index + 1, entries.Length, NameOf(relative)));
            // Before the file, not after: the report above is only worth making if the page gets
            // a turn to draw it.
            await UiBreather.BreatheAsync(cancellationToken).ConfigureAwait(false);

            // Straight into an array of the size the zip already declares. Going through a
            // growing MemoryStream first meant holding two copies of every save and repeatedly
            // reallocating - on the 16 MB facility save that is the difference worth having.
            var contents = new byte[entry.Length];
            using (var reader = entry.Open())
            {
                // Read in pieces rather than in one call. Decompressing is the slow part, and the
                // facility save alone is ~16 MB - long enough on its own to freeze the page even
                // if every other file is handled politely. Breathing between pieces keeps the
                // longest stall down to roughly the cost of one piece.
                var read = 0;
                while (read < contents.Length)
                {
                    var wanted = Math.Min(ChunkBytes, contents.Length - read);
                    var got = await reader
                        .ReadAtLeastAsync(contents.AsMemory(read, wanted), wanted, throwOnEndOfStream: false, cancellationToken)
                        .ConfigureAwait(false);
                    if (got == 0) break;
                    read += got;
                    if (read < contents.Length) await UiBreather.BreatheAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            saves[relative] = contents;
        }

        return new SaveBundleContents(worldFolder ?? CleanName(fallbackName), saves);
    }

    /// <summary>
    /// The one folder every save sits under, or null when they are already at the top.
    /// </summary>
    /// <remarks>
    /// Both shapes turn up and both are reasonable. The editor's own EXPORT writes the saves at
    /// the top of the zip; a player zipping the world folder in their file manager gets them
    /// under a folder named after the world. Stripping that folder means the paths inside match
    /// either way - and it names the world into the bargain, which is better than naming it
    /// after whatever the zip file happened to be called.
    /// </remarks>
    private static string? SharedTopFolder(IReadOnlyList<string> paths)
    {
        string? shared = null;
        foreach (var path in paths)
        {
            var separator = path.IndexOf('/');
            if (separator <= 0) return null; // a save at the top level: no shared folder
            var top = path[..separator];
            if (shared is null) shared = top;
            else if (!string.Equals(shared, top, StringComparison.OrdinalIgnoreCase)) return null;
        }
        // "PlayerData" alone is part of a world's layout, not a folder wrapping one.
        return string.Equals(shared, "PlayerData", StringComparison.OrdinalIgnoreCase) ? null : shared;
    }

    /// <summary>Zip entries always use '/', but a zip written on Windows by some tools uses '\'.</summary>
    private static string Normalize(string entryPath) => entryPath.Replace('\\', '/').TrimStart('/');

    private static string NameOf(string relative)
    {
        var separator = relative.LastIndexOf('/');
        return separator < 0 ? relative : relative[(separator + 1)..];
    }

    /// <summary>The zip's file name without its extension, as the world's name.</summary>
    private static string CleanName(string fileName)
    {
        var name = fileName.Replace('\\', '/');
        var lastSeparator = name.LastIndexOf('/');
        if (lastSeparator >= 0) name = name[(lastSeparator + 1)..];
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length == 0 ? "Saves" : name;
    }
}

/// <summary>
/// A host that can open a zipped save folder as if it were a folder.
/// </summary>
/// <remarks>
/// Only the browser implements this. The desktop has no need for it - a player there unzips into
/// a folder and opens that - and screens check for it rather than assuming, so the button only
/// appears where it does something.
/// </remarks>
public interface ISaveBundleReader
{
    /// <summary>
    /// Opens the saves inside a zip as the current workspace folder, read-only: nothing goes back
    /// into the zip, and the player takes their edits away with EXPORT.
    /// </summary>
    /// <param name="progress">
    /// Told how far the unpacking has got, so a screen can say so. Unpacking a real world takes
    /// seconds, which is far too long to leave a page looking like it has died.
    /// </param>
    /// <returns>The folder identifier to hand to <c>SaveWorkspaceSessionService.OpenAsync</c>.</returns>
    Task<string> OpenBundleAsync(
        string fileName,
        byte[] contents,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A world the player opened before, offered as a way straight back into it.</summary>
/// <param name="Name">The folder's name, which is also what the sidebar showed.</param>
/// <param name="OpenedAt">When it was last opened, or null when that was not recorded.</param>
/// <param name="StoredInBrowser">
/// True when the saves themselves are kept in the browser rather than in a folder on disk - a
/// world opened from a zip, or from a browser that could only take a copy. It reopens without
/// asking anyone's permission and comes back exactly as it was left, edits included, because
/// each save was re-stored as it was written. It is still a copy: taking it anywhere real means
/// EXPORT.
/// </param>
public sealed record RecentWorld(string Name, DateTimeOffset? OpenedAt, bool StoredInBrowser = false);

/// <summary>
/// A host that can remember which worlds were open and get back into them later.
/// </summary>
/// <remarks>
/// Only the browser implements this, and only it needs to. A desktop already finds the player's
/// worlds by looking where the game keeps them; a browser is handed one folder and forgets it the
/// moment the page reloads, which meant every refresh started with the folder picker again.
///
/// What is remembered is a reference to the folder, not its contents, and the browser drops the
/// permission that came with it when the tab closes - so getting back in asks the player again.
/// That prompt only appears in response to a click, which is why these are offered as buttons.
/// </remarks>
public interface IRecentWorldStore
{
    /// <summary>Notes that <paramref name="folder"/> is open, so it can be offered later.</summary>
    Task RememberAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>The worlds worth offering, most recently opened first.</summary>
    Task<IReadOnlyList<RecentWorld>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the player's permission again and makes <paramref name="folder"/> readable.
    /// Must be called from a click. Returns null when permission was refused or it has gone.
    /// </summary>
    /// <param name="progress">
    /// Told how far along a remembered zip is being unpacked. A folder reopens instantly and
    /// reports nothing; a zip takes as long as it did the first time and must say so.
    /// </param>
    Task<string?> ReopenAsync(
        string folder,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Drops a world from the offered list.</summary>
    Task ForgetAsync(string folder, CancellationToken cancellationToken = default);
}
