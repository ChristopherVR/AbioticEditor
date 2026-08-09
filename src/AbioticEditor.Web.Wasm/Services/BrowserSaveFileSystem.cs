using AbioticEditor.Web.Services;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Wasm.Services;

/// <summary>
/// Reaches the player's save folder through the browser's File System Access API.
/// </summary>
/// <remarks>
/// <para>The directory handles themselves live in JavaScript (they cannot cross the interop
/// boundary), so this type refers to files by the <c>"folderName/pathInsideFolder"</c>
/// identifiers <c>wwwroot/js/saveFileSystem.js</c> produces. They are deliberately NOT local
/// file-system paths, which is why <see cref="HasLocalPaths"/> is false and every feature that
/// would hand a path to something outside the editor is switched off on this host.</para>
///
/// <para>Chromium only, and there is currently no fallback: Firefox and Safari have neither the
/// directory picker nor <c>showOpenFilePicker</c>, so on those browsers the editor loads and runs
/// but cannot open a save at all. The player is told exactly that (see
/// <c>UserFacingErrorService.IsFolderPickerUnavailable</c>) rather than being sent to check a
/// folder that is fine. <see cref="IsSupportedAsync"/> reports the capability; nothing calls it
/// yet, because there is no second mode to switch to.</para>
/// </remarks>
public sealed class BrowserSaveFileSystem(IJSRuntime js) : ISaveFileSystem, ISaveBundleReader, IRecentWorldStore
{
    /// <summary>
    /// Ceiling for a single save read. The largest real save is the ~16 MB Facility region save;
    /// this leaves generous headroom while still refusing to pull something absurd into a tab.
    /// </summary>
    private const long MaximumSaveSize = 128L * 1024 * 1024;

    public bool HasLocalPaths => false;

    /// <summary>
    /// False once a folder has been opened read-only, which is what happens on a browser with no
    /// File System Access API. Set by whichever open method was used; the editor holds one
    /// workspace at a time, so this describes the folder that is actually open.
    /// </summary>
    public bool CanWrite { get; private set; } = true;

    /// <summary>True when this browser can open a folder it can also write back to (Chromium).</summary>
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("abioticSaveFs.isSupported");

    /// <summary>
    /// Prompts for a save folder and returns the identifier to hand to
    /// <see cref="ListSavesAsync"/>, or null when the player cancelled.
    /// </summary>
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var folder = await js.InvokeAsync<string?>("abioticSaveFs.pickFolder", cancellationToken).ConfigureAwait(false);
        if (folder is not null) { _bundles.Remove(folder); _storedWorlds.Remove(folder); CanWrite = true; }
        return folder;
    }

    /// <summary>
    /// Opens a save folder read-only, for browsers that cannot grant write access to one. The
    /// player picks the folder in their own OS dialog and every file in it is handed over as a
    /// snapshot, so the editor can read and edit a whole world but must hand the result back
    /// through <see cref="ISaveExporter"/> instead of saving in place.
    /// </summary>
    /// <returns>The folder's name, or null when the player cancelled.</returns>
    public async Task<string?> UploadFolderAsync(CancellationToken cancellationToken = default)
    {
        var folder = await js.InvokeAsync<string?>("abioticSaveFs.uploadFolder", cancellationToken).ConfigureAwait(false);
        if (folder is not null) { _bundles.Remove(folder); _storedWorlds.Remove(folder); CanWrite = false; }
        return folder;
    }

    /// <summary>Raised when the player drops a save folder onto the window.</summary>
    public event Func<string, Task>? FolderDropped;

    /// <summary>Raised when the player drops a zipped save folder onto the window.</summary>
    public event Func<string, Task>? BundleDropped;

    /// <summary>
    /// Starts listening for a folder dropped onto the window. Safe to call more than once; the
    /// listener is only wired up the first time.
    /// </summary>
    public async Task ListenForDroppedFolderAsync(CancellationToken cancellationToken = default)
    {
        _dropReference ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("abioticSaveFs.listenForDroppedFolder", cancellationToken, _dropReference).ConfigureAwait(false);
    }

    /// <summary>Called from JavaScript once a dropped folder has been registered.</summary>
    [JSInvokable]
    public Task OnFolderDropped(string folder)
        => FolderDropped is { } handler ? handler(folder) : Task.CompletedTask;

    /// <summary>Called from JavaScript when the dropped file is a zip, naming it.</summary>
    [JSInvokable]
    public Task OnZipDropped(string fileName)
        => BundleDropped is { } handler ? handler(fileName) : Task.CompletedTask;

    /// <summary>
    /// Unpacks the zip JavaScript is holding from the last drop and opens it as the workspace.
    /// </summary>
    /// <returns>The folder identifier to hand to the workspace.</returns>
    public async Task<string> OpenDroppedBundleAsync(
        string fileName,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var contents = await ReadStreamAsync("abioticSaveFs.readDroppedZip", cancellationToken).ConfigureAwait(false);
        return await OpenBundleAsync(fileName, contents, progress, cancellationToken).ConfigureAwait(false);
    }

    private DotNetObjectReference<BrowserSaveFileSystem>? _dropReference;

    // ---------- worlds the player opened before ----------

    /// <summary>
    /// Notes an open world so it can be offered later, storing its saves when there is no folder
    /// to point back at.
    /// </summary>
    /// <remarks>
    /// The saves are stored unpacked rather than as the zip they arrived in. Reopening is then a
    /// straight read with no unzipping, and - the part that matters - an edit can replace the one
    /// file it touched, so the world comes back as the player left it instead of as they first
    /// opened it.
    /// </remarks>
    public async Task RememberAsync(string folder, CancellationToken cancellationToken = default)
    {
        var contents = _bundles.TryGetValue(folder, out var saves) ? saves : null;
        await js.InvokeVoidAsync(
            "abioticSaveFs.rememberRecent", cancellationToken,
            folder,
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            contents is not null).ConfigureAwait(false);
        if (contents is null) return;

        foreach (var (relative, bytes) in contents.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StoreRecentFileAsync(folder, relative, bytes, cancellationToken).ConfigureAwait(false);
            // Storing a whole world is dozens of writes; let the page draw between them so
            // remembering never costs the responsiveness that opening just regained.
            await UiBreather.BreatheAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StoreRecentFileAsync(string folder, string relative, byte[] bytes, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var streamRef = new DotNetStreamReference(source, leaveOpen: true);
        await js.InvokeVoidAsync("abioticSaveFs.rememberRecentFile", cancellationToken, folder, relative, streamRef)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecentWorld>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await js.InvokeAsync<RecentEntry[]>("abioticSaveFs.listRecent", cancellationToken).ConfigureAwait(false);
        return entries
            .Select(entry => new RecentWorld(entry.Name, ParseOpenedAt(entry.OpenedAt), entry.FromStorage))
            .ToArray();
    }

    public async Task<string?> ReopenAsync(
        string folder,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Saves kept in storage need nobody's permission - they are already ours - so they come
        // straight back, edits and all, read-only exactly as they were.
        // Only the list of saves and their sizes - a few kilobytes. The contents stay in storage
        // until something actually asks for them, exactly as a folder's files stay on disk.
        // Pulling all sixty-odd across up front took six and a half seconds for a world the
        // player might only want one save out of.
        var stored = await js.InvokeAsync<StoredFile[]>("abioticSaveFs.recentFileList", cancellationToken, folder).ConfigureAwait(false);
        if (stored.Length > 0)
        {
            _storedWorlds[folder] = stored.ToDictionary(file => file.Path, file => file.Length, StringComparer.OrdinalIgnoreCase);
            _bundles[folder] = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            CanWrite = true;
            return folder;
        }

        var reopened = await js.InvokeAsync<string?>("abioticSaveFs.reopenRecent", cancellationToken, folder).ConfigureAwait(false);
        // A re-granted folder is writable again, and is no longer served from memory.
        if (reopened is not null) { _bundles.Remove(reopened); _storedWorlds.Remove(reopened); CanWrite = true; }
        return reopened;
    }

    public Task ForgetAsync(string folder, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync("abioticSaveFs.forgetRecent", cancellationToken, folder).AsTask();

    private static DateTimeOffset? ParseOpenedAt(string? value)
        => DateTimeOffset.TryParse(
            value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private sealed record RecentEntry(string Name, string? OpenedAt, bool FromStorage);

    private sealed record StoredFile(string Path, long Length);

    /// <summary>
    /// Worlds whose saves live in the browser's storage: path to size, contents left behind.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="_bundles"/>, which holds contents actually in memory. A
    /// world reopened from storage starts with this list and an empty bundle, and each save is
    /// pulled across only when something reads it - then cached in the bundle so a second read is
    /// free. Header and tail reads are served by slicing in JavaScript and never load the file.
    /// </remarks>
    private readonly Dictionary<string, Dictionary<string, long>> _storedWorlds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times the editor has written each save, keyed by its full identifier.</summary>
    private readonly Dictionary<string, int> _bundleRevisions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sizes of a stored world's saves, or null when this world is not one.</summary>
    private Dictionary<string, long>? StoredWorldFor(string path, out string root, out string relative)
    {
        var separator = path.IndexOf('/');
        root = separator < 0 ? path : path[..separator];
        relative = separator < 0 ? string.Empty : path[(separator + 1)..];
        return _storedWorlds.TryGetValue(root, out var files) ? files : null;
    }

    /// <summary>
    /// Reads part of a save kept in storage, without loading the rest of it.
    /// </summary>
    /// <param name="offset">Negative counts back from the end, for a tail read.</param>
    /// <param name="length">-1 for everything from <paramref name="offset"/> on.</param>
    private Task<byte[]> ReadStoredSliceAsync(
        string root, string relative, long offset, long length, CancellationToken cancellationToken)
        => ReadStreamAsync("abioticSaveFs.recentFileSlice", cancellationToken, root, relative, offset, length);

    // ---------- worlds opened from a zip ----------

    /// <summary>
    /// Worlds unpacked from a zip, by world name, each holding its saves by path within it.
    /// </summary>
    /// <remarks>
    /// These live here in .NET rather than in the JavaScript registry the picked and uploaded
    /// folders use, because unpacking the zip is .NET's job - the browser has no unzip of its
    /// own and the editor already carries one. Every read below therefore checks here first,
    /// and a write lands here too: nothing goes back into the zip file, so the edited copy stays
    /// in the tab until EXPORT hands it back, exactly like a folder opened read-only.
    /// </remarks>
    private readonly Dictionary<string, Dictionary<string, byte[]>> _bundles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> OpenBundleAsync(
        string fileName,
        byte[] contents,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Not Task.Run: a browser has one thread, so that would not move the work off anything -
        // it would just block the same thread a moment later, which is exactly what froze the tab
        // for six seconds. ReadAsync hands control back between files instead.
        var bundle = await SaveBundle
            .ReadAsync(new MemoryStream(contents, writable: false), fileName, progress, cancellationToken)
            .ConfigureAwait(false);

        _bundles[bundle.Name] = bundle.Saves.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        // Freshly unpacked: everything is in memory, so nothing is owed to storage yet.
        _storedWorlds.Remove(bundle.Name);
        // Re-opening a world of the same name must win over a folder opened earlier, and vice
        // versa, so exactly one source ever answers for a given name.
        await js.InvokeVoidAsync("abioticSaveFs.forget", cancellationToken, bundle.Name).ConfigureAwait(false);
        // Writable, because a write now lands somewhere that survives the tab: the copy kept in
        // the browser. SAVE therefore means saved, and the world comes back with those edits next
        // time. It still is not the player's own folder - the toast after a save says as much,
        // and EXPORT remains the way to get the files back to the game.
        CanWrite = true;
        return bundle.Name;
    }

    /// <summary>The unpacked world a path belongs to, or null when it is not one of them.</summary>
    private Dictionary<string, byte[]>? BundleFor(string path, out string relative)
    {
        relative = string.Empty;
        var separator = path.IndexOf('/');
        var root = separator < 0 ? path : path[..separator];
        if (!_bundles.TryGetValue(root, out var bundle)) return null;
        relative = separator < 0 ? string.Empty : path[(separator + 1)..];
        return bundle;
    }

    public async Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default)
        => _bundles.ContainsKey(folder)
            || await js.InvokeAsync<bool>("abioticSaveFs.folderExists", cancellationToken, folder).ConfigureAwait(false);

    public async Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (_bundles.TryGetValue(folder, out var bundle))
        {
            // Sizes come from storage for a reopened world, whose bundle starts empty, and from
            // the bundle itself for one just unpacked. Either way no save is read to list it.
            var sizes = _storedWorlds.TryGetValue(folder, out var stored)
                ? stored
                : bundle.ToDictionary(pair => pair.Key, pair => pair.Value.LongLength, StringComparer.OrdinalIgnoreCase);
            return sizes
                .Where(pair => pair.Key.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                .Select(pair => new SaveFileEntry($"{folder}/{pair.Key}", pair.Key, NameOf(pair.Key), pair.Value))
                .ToArray();
        }

        var entries = await js.InvokeAsync<BrowserEntry[]>("abioticSaveFs.listSaves", cancellationToken, folder).ConfigureAwait(false);
        return entries
            .Select(entry => new SaveFileEntry(entry.Path, entry.RelativePath, entry.Name, entry.Length))
            .ToArray();
    }

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (BundleFor(path, out var relative) is not { } bundle)
            return await ReadStreamAsync("abioticSaveFs.readAll", cancellationToken, path).ConfigureAwait(false);

        if (bundle.TryGetValue(relative, out var loaded)) return loaded;

        // Not in memory yet: this world came back from storage. Fetch the save once and keep it,
        // so editing it and saving it behave exactly as they do for a freshly unpacked world.
        var contents = await ReadStoredAsync(path, relative, -1, cancellationToken).ConfigureAwait(false);
        bundle[relative] = contents;
        return contents;
    }

    public async Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (BundleFor(path, out var relative) is not { } bundle)
            return await ReadStreamAsync("abioticSaveFs.readHeader", cancellationToken, path, maxBytes).ConfigureAwait(false);
        if (bundle.TryGetValue(relative, out var loaded)) return Slice(loaded, fromEnd: false, maxBytes);
        return await ReadStoredAsync(path, relative, maxBytes, cancellationToken, fromEnd: false).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (BundleFor(path, out var relative) is not { } bundle)
            return await ReadStreamAsync("abioticSaveFs.readTail", cancellationToken, path, maxBytes).ConfigureAwait(false);
        if (bundle.TryGetValue(relative, out var loaded)) return Slice(loaded, fromEnd: true, maxBytes);
        return await ReadStoredAsync(path, relative, maxBytes, cancellationToken, fromEnd: true).ConfigureAwait(false);
    }

    /// <summary>Serves a read for a world whose saves are still in storage.</summary>
    private async Task<byte[]> ReadStoredAsync(
        string path, string relative, long maxBytes, CancellationToken cancellationToken, bool fromEnd = false)
    {
        if (StoredWorldFor(path, out var root, out _) is null)
        {
            throw new FileNotFoundException($"'{relative}' is not in the world you opened. Open it again.", relative);
        }
        var offset = fromEnd ? -maxBytes : 0;
        return await ReadStoredSliceAsync(root, relative, offset, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default)
    {
        // The stamp has to mean "has this file changed", and nothing else. Length paired with a
        // write counter does exactly that, and - the part that matters - gives the same answer
        // whether the save happens to be in memory yet or still sitting in storage.
        //
        // It used to be length plus the byte array's identity, which changed the moment a save
        // was first read in. Every cache keyed on the stamp therefore missed as soon as its file
        // was loaded, and the ~16 MB facility save was re-parsed from scratch each time: several
        // seconds of frozen page, over and over, for a world that had not changed at all.
        if (BundleFor(path, out var relative) is { } bundle)
        {
            var revision = _bundleRevisions.TryGetValue(path, out var written) ? written : 0;
            if (bundle.TryGetValue(relative, out var contents)) return $"{contents.Length}:{revision}";
            return StoredWorldFor(path, out _, out _) is { } sizes && sizes.TryGetValue(relative, out var size)
                ? $"{size}:{revision}"
                : null;
        }
        return await js.InvokeAsync<string?>("abioticSaveFs.versionStamp", cancellationToken, path).ConfigureAwait(false);
    }

    public async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        if (BundleFor(path, out var relative) is { } bundle)
        {
            // The file the player chose is never touched, so it IS the backup.
            bundle[relative] = contents;
            // The one thing that genuinely invalidates anything cached about this save.
            _bundleRevisions[path] = (_bundleRevisions.TryGetValue(path, out var written) ? written : 0) + 1;
            // And the remembered copy moves with it, so closing the tab and coming back gives
            // this world as the player left it rather than as they first opened it.
            var separator = path.IndexOf('/');
            var world = separator < 0 ? path : path[..separator];
            await StoreRecentFileAsync(world, relative, contents, cancellationToken).ConfigureAwait(false);
            return;
        }
        using var source = new MemoryStream(contents, writable: false);
        using var streamRef = new DotNetStreamReference(source, leaveOpen: true);
        await js.InvokeVoidAsync("abioticSaveFs.write", cancellationToken, path, streamRef).ConfigureAwait(false);
    }

    private static byte[] FromBundle(Dictionary<string, byte[]> bundle, string relative)
        => bundle.TryGetValue(relative, out var contents)
            ? contents
            : throw new FileNotFoundException($"'{relative}' is not in the zip you opened. Open it again.", relative);

    private static byte[] Slice(byte[] contents, bool fromEnd, int maxBytes)
    {
        var length = Math.Min(maxBytes, contents.Length);
        var start = fromEnd ? contents.Length - length : 0;
        return contents.AsSpan(start, length).ToArray();
    }

    private static string NameOf(string relative)
    {
        var separator = relative.LastIndexOf('/');
        return separator < 0 ? relative : relative[(separator + 1)..];
    }

    /// <summary>
    /// Pulls bytes back from JavaScript as a stream rather than a JSON array: a region save is
    /// megabytes, and base64 in both directions would dominate the time to open a world.
    /// </summary>
    private async Task<byte[]> ReadStreamAsync(string identifier, CancellationToken cancellationToken, params object?[] arguments)
    {
        var reference = await js.InvokeAsync<IJSStreamReference>(identifier, cancellationToken, arguments).ConfigureAwait(false);
        await using var stream = await reference.OpenReadStreamAsync(MaximumSaveSize, cancellationToken).ConfigureAwait(false);

        // The length is known up front, so the buffer is allocated once instead of doubling its
        // way up to 16 MB.
        var length = (int)reference.Length;
        var contents = new byte[length];
        var read = 0;
        while (read < length)
        {
            var wanted = Math.Min(TransferChunkBytes, length - read);
            var got = await stream
                .ReadAtLeastAsync(contents.AsMemory(read, wanted), wanted, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            if (got == 0) break;
            read += got;
            // Awaiting the read is not enough to let the page draw: on this runtime those
            // continuations run inside the same turn. Fetching a 16 MB region save therefore
            // stalled the tab for whole seconds in one go. Breathing between chunks costs a
            // millisecond each and keeps the editor answering while a big save comes across.
            if (read < length) await UiBreather.BreatheAsync(cancellationToken).ConfigureAwait(false);
        }
        return read == length ? contents : contents[..read];
    }

    /// <summary>
    /// How much of a save to bring across before letting the page draw. Big enough that the
    /// pauses do not dominate a transfer, small enough that no single chunk is a visible stall.
    /// </summary>
    private const int TransferChunkBytes = 2 * 1024 * 1024;

    private sealed record BrowserEntry(string Path, string RelativePath, string Name, long Length);
}
