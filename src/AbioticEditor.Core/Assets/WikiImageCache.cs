using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Downloads images from abioticfactor.wiki.gg (via <c>Special:FilePath/&lt;FileName&gt;</c>,
/// which 302-redirects to the actual file) and caches them under a local directory so
/// every file is fetched at most once per machine. Lookups for an already-cached file
/// never touch the network.
///
/// Failures (offline, 404, non-image responses) return <c>null</c> and Warn-log once per
/// file name. Responses are validated by magic bytes (PNG/JPEG/WebP/GIF); the cached
/// file keeps the extension matching its actual format so image controls can load it.
///
/// Wiki content is CC BY-NC-SA - surfaces showing these images must carry the
/// <see cref="AttributionText"/> line.
/// </summary>
public sealed class WikiImageCache
{
    /// <summary>One-line credit required under any displayed wiki image.</summary>
    public const string AttributionText = "Image: abioticfactor.wiki.gg";

    private const string FilePathBase = "https://abioticfactor.wiki.gg/wiki/Special:FilePath/";

    /// <summary>Extensions a cached file can carry, in probe order.</summary>
    private static readonly string[] KnownExtensions = [".png", ".jpg", ".webp", ".gif"];

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Special:FilePath answers with a redirect to /images/...; HttpClientHandler
        // follows same-scheme redirects by default.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AbioticEditor/1.0 (save editor; wiki image cache)");
        return client;
    }

    /// <summary>Process-wide instance caching under <c>%LOCALAPPDATA%\AbioticEditor\wiki</c>.</summary>
    public static WikiImageCache Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "wiki"));

    private readonly object _sync = new();
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<byte[]?>> _downloader;

    /// <param name="cacheDirectory">Where cached images live (created on first download).</param>
    /// <param name="downloader">
    /// Test hook: given the full download URL, returns the raw response bytes, or
    /// <c>null</c> for a clean miss (404). May throw for transport failures. When omitted,
    /// a shared <see cref="HttpClient"/> fetches from the live wiki.
    /// </param>
    public WikiImageCache(string cacheDirectory, Func<string, Task<byte[]?>>? downloader = null)
    {
        _cacheDirectory = cacheDirectory;
        _downloader = downloader ?? DownloadAsync;
    }

    /// <summary>
    /// Resolves a wiki file name (e.g. <c>Itemicon_antefish.png</c>, with or without a
    /// <c>File:</c> prefix) to a local image path. Returns the cached path immediately
    /// when present; downloads and caches otherwise. <c>null</c> when the file doesn't
    /// exist on the wiki, the response isn't an image, or the network is unavailable.
    /// </summary>
    public Task<string?> GetAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return Task.FromResult<string?>(null);

        var wikiName = NormalizeWikiName(fileName);
        var safeBase = SafeNameFor(wikiName);

        lock (_sync)
        {
            // Already cached on disk: answer without any network involvement.
            foreach (var ext in KnownExtensions)
            {
                var candidate = Path.Combine(_cacheDirectory, safeBase + ext);
                if (File.Exists(candidate)) return Task.FromResult<string?>(candidate);
            }

            // Coalesce concurrent requests for the same file into one download.
            if (_inFlight.TryGetValue(safeBase, out var pending)) return pending;
            var task = FetchAndCacheAsync(wikiName, safeBase);
            if (!task.IsCompleted)
            {
                // Register, then deregister on completion. The completed-synchronously
                // case (test downloaders) must never be registered at all - the
                // continuation could otherwise run before the insert and leave a
                // permanently failed entry behind.
                _inFlight[safeBase] = task;
                _ = task.ContinueWith(
                    t => Deregister(safeBase, t),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            return task;
        }
    }

    /// <summary>
    /// Tries each candidate file name in order and returns the first image that
    /// resolves; <c>null</c> when none do.
    /// </summary>
    public async Task<string?> GetFirstAsync(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        foreach (var name in fileNames)
        {
            var path = await GetAsync(name).ConfigureAwait(false);
            if (path is not null) return path;
        }
        return null;
    }

    private async Task<string?> FetchAndCacheAsync(string wikiName, string safeBase)
    {
        try
        {
            byte[]? data;
            try
            {
                data = await _downloader(FilePathBase + Uri.EscapeDataString(wikiName)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                WarnOnce(wikiName, $"download failed ({ex.GetType().Name}: {ex.Message})");
                return null;
            }

            if (data is null || data.Length == 0)
            {
                WarnOnce(wikiName, "not found on the wiki");
                return null;
            }

            var extension = ExtensionForImage(data);
            if (extension is null)
            {
                // Typically an HTML error page served on a missing file.
                WarnOnce(wikiName, "response is not a recognized image format");
                return null;
            }

            var finalPath = Path.Combine(_cacheDirectory, safeBase + extension);
            Directory.CreateDirectory(_cacheDirectory);

            // Write-then-move so a crash mid-write never leaves a half image that
            // would be treated as cached forever.
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tempPath, data).ConfigureAwait(false);
            File.Move(tempPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WarnOnce(wikiName, $"cache write failed ({ex.Message})");
            return null;
        }
    }

    private void Deregister(string safeBase, Task<string?> task)
    {
        lock (_sync)
        {
            // Only remove our own registration; a newer fetch may already be in flight.
            if (_inFlight.TryGetValue(safeBase, out var current) && ReferenceEquals(current, task))
            {
                _inFlight.Remove(safeBase);
            }
        }
    }

    private void WarnOnce(string wikiName, string reason)
    {
        lock (_sync)
        {
            if (!_warned.Add(wikiName)) return;
        }
        EditorLog.Warn("WikiImage", $"{wikiName}: {reason}");
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        using var response = await Http.GetAsync(new Uri(url)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Strips a <c>File:</c> prefix and folds spaces to underscores - MediaWiki treats
    /// the two interchangeably, and one canonical form keeps the cache key stable.
    /// </summary>
    private static string NormalizeWikiName(string fileName)
    {
        var name = fileName.Trim();
        if (name.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) name = name[5..];
        return name.Replace(' ', '_');
    }

    /// <summary>
    /// The cache file name (without extension) for a wiki file name: every character
    /// that isn't a letter, digit, dash or underscore becomes <c>_</c>, so path
    /// separators and other hostile input can never escape the cache directory.
    /// </summary>
    public static string SafeNameFor(string fileName)
    {
        var name = NormalizeWikiName(fileName);
        // Drop a recognized image extension; the cached extension reflects the actual
        // downloaded format instead.
        foreach (var ext in KnownExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^ext.Length];
                break;
            }
        }
        Span<char> safe = stackalloc char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            safe[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }
        var result = new string(safe).Trim('_');
        if (result.Length == 0) return "_";

        // Windows reserves device names (CON, NUL, COM1...) even with an extension.
        return ReservedDeviceNames.Contains(result) ? "_" + result : result;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The file extension matching the image format of <paramref name="data"/>
    /// (PNG/JPEG/WebP/GIF magic bytes), or <c>null</c> when it isn't a supported image.
    /// </summary>
    public static string? ExtensionForImage(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return ".png";
        }
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }
        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            return ".webp";
        }
        if (data.Length >= 6
            && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8'
            && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
        {
            return ".gif";
        }
        return null;
    }
}
