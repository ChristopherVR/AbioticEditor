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
        if (folder is not null) { _bundles.Remove(folder); _openedBundles.Remove(folder); CanWrite = true; }
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
        if (folder is not null) { _bundles.Remove(folder); _openedBundles.Remove(folder); CanWrite = false; }
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

    public Task RememberAsync(string folder, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync(
            "abioticSaveFs.rememberRecent", cancellationToken,
            folder,
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            // A world unpacked from a zip has no folder behind it, so the zip goes in instead -
            // otherwise the one kind of world a browser can always reopen would be the one kind
            // that never appeared in the list.
            _openedBundles.TryGetValue(folder, out var zip) ? zip : null).AsTask();

    public async Task<IReadOnlyList<RecentWorld>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await js.InvokeAsync<RecentEntry[]>("abioticSaveFs.listRecent", cancellationToken).ConfigureAwait(false);
        return entries
            .Select(entry => new RecentWorld(entry.Name, ParseOpenedAt(entry.OpenedAt), entry.FromZip))
            .ToArray();
    }

    public async Task<string?> ReopenAsync(
        string folder,
        IProgress<SaveBundleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // A remembered zip needs no permission - it is already ours - so it is unpacked straight
        // back in, read-only as it was before.
        var zip = await ReadRecentZipAsync(folder, cancellationToken).ConfigureAwait(false);
        if (zip is not null) return await OpenBundleAsync($"{folder}.zip", zip, progress, cancellationToken).ConfigureAwait(false);

        var reopened = await js.InvokeAsync<string?>("abioticSaveFs.reopenRecent", cancellationToken, folder).ConfigureAwait(false);
        // Re-granted folders are writable again, and are no longer served from a zip.
        if (reopened is not null) { _bundles.Remove(reopened); _openedBundles.Remove(reopened); CanWrite = true; }
        return reopened;
    }

    private async Task<byte[]?> ReadRecentZipAsync(string folder, CancellationToken cancellationToken)
    {
        var reference = await js.InvokeAsync<IJSStreamReference?>("abioticSaveFs.recentZip", cancellationToken, folder).ConfigureAwait(false);
        if (reference is null) return null;
        await using var stream = await reference.OpenReadStreamAsync(MaximumSaveSize, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public Task ForgetAsync(string folder, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync("abioticSaveFs.forgetRecent", cancellationToken, folder).AsTask();

    private static DateTimeOffset? ParseOpenedAt(string? value)
        => DateTimeOffset.TryParse(
            value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private sealed record RecentEntry(string Name, string? OpenedAt, bool FromZip);

    /// <summary>
    /// The zip each open bundle came from, so it can be kept for next time. Held here rather
    /// than handed straight to storage because a world is only worth remembering once it has
    /// actually opened.
    /// </summary>
    private readonly Dictionary<string, byte[]> _openedBundles = new(StringComparer.OrdinalIgnoreCase);

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
        _openedBundles[bundle.Name] = contents;
        // Re-opening a world of the same name must win over a folder opened earlier, and vice
        // versa, so exactly one source ever answers for a given name.
        await js.InvokeVoidAsync("abioticSaveFs.forget", cancellationToken, bundle.Name).ConfigureAwait(false);
        CanWrite = false;
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
            return bundle
                .Where(pair => pair.Key.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                .Select(pair => new SaveFileEntry(
                    $"{folder}/{pair.Key}", pair.Key, NameOf(pair.Key), pair.Value.LongLength))
                .ToArray();
        }

        var entries = await js.InvokeAsync<BrowserEntry[]>("abioticSaveFs.listSaves", cancellationToken, folder).ConfigureAwait(false);
        return entries
            .Select(entry => new SaveFileEntry(entry.Path, entry.RelativePath, entry.Name, entry.Length))
            .ToArray();
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => BundleFor(path, out var relative) is { } bundle
            ? Task.FromResult(FromBundle(bundle, relative))
            : ReadStreamAsync("abioticSaveFs.readAll", cancellationToken, path);

    public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
        => BundleFor(path, out var relative) is { } bundle
            ? Task.FromResult(Slice(FromBundle(bundle, relative), fromEnd: false, maxBytes))
            : ReadStreamAsync("abioticSaveFs.readHeader", cancellationToken, path, maxBytes);

    public Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
        => BundleFor(path, out var relative) is { } bundle
            ? Task.FromResult(Slice(FromBundle(bundle, relative), fromEnd: true, maxBytes))
            : ReadStreamAsync("abioticSaveFs.readTail", cancellationToken, path, maxBytes);

    public async Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default)
    {
        // An unpacked save only changes when the editor itself writes it, and a write replaces
        // the array - so its length and identity together are a sound "has this changed" token.
        if (BundleFor(path, out var relative) is { } bundle)
        {
            return bundle.TryGetValue(relative, out var contents)
                ? $"{contents.Length}:{contents.GetHashCode()}"
                : null;
        }
        return await js.InvokeAsync<string?>("abioticSaveFs.versionStamp", cancellationToken, path).ConfigureAwait(false);
    }

    public async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        if (BundleFor(path, out var relative) is { } bundle)
        {
            // The zip the player chose is never touched, so it IS the backup.
            bundle[relative] = contents;
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
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed record BrowserEntry(string Path, string RelativePath, string Name, long Length);
}
