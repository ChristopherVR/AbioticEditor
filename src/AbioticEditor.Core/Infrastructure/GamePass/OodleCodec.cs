using System.Runtime.InteropServices;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// Oodle (de)compression for the Game Pass <c>ABF_SAVE_VERSION</c> bundle payload. The bundle
/// stores every world/player member as one Oodle-compressed stream (UE5's default codec), so the
/// editor needs both directions: decompress to read, recompress to write. We P/Invoke
/// <c>OodleLZ_Decompress</c> / <c>OodleLZ_Compress</c> directly on the native library.
///
/// <para>The DLL is resolved lazily from, in order: the <c>ABIOTIC_OODLE_DLL</c> env var, the
/// installed game's copy (next to its executable), a copy this codec previously downloaded and
/// cached under <c>%LOCALAPPDATA%/AbioticEditor/oodle</c>, then CUE4Parse's downloader (the same
/// mechanism the editor already uses for pak decompression) - that last step needs an internet
/// connection but only ever runs once, since a successful download is cached for next time. When
/// none can be obtained the codec throws <see cref="OodleUnavailableException"/> so callers can
/// degrade with a clear message rather than a crash.</para>
/// </summary>
public static class OodleCodec
{
    // Both platforms' names are listed on every platform on purpose: the cache lookup has to find
    // whatever CUE4Parse's downloader wrote, and on Linux that is the .so. Listing only the Windows
    // DLLs meant a Linux download was cached and then never found again, so every run needed the
    // internet and an offline machine could not open a Game Pass save at all.
    private static readonly string[] DllNames =
    {
        "oo2core_9_win64.dll",
        "oodle-data-shared.dll",
        "liboodle-data-shared.so",
        "liboo2corelinux64.so",
    };

    /// <summary>Where a downloaded Oodle library is cached so later runs don't need internet again.</summary>
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor", "oodle");

    // OodleLZ_Compressor: Kraken is a good general default; the decompressor auto-detects the
    // codec from the stream, so the editor need not match the game's exact compressor.
    private const int CompressorKraken = 8;

    // OodleLZ_CompressionLevel_Normal. Any valid level produces a stream the game can decode.
    private const int CompressionLevelNormal = 4;

    private static readonly object Gate = new();
    private static bool _resolved;
    private static OodleLZ_DecompressDelegate? _decompress;
    private static OodleLZ_CompressDelegate? _compress;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleLZ_DecompressDelegate(
        nint compBuf, long compBufSize, nint rawBuf, long rawLen,
        int fuzzSafe, int checkCrc, int verbosity,
        nint decBufBase, long decBufSize, nint fpCallback, nint callbackUserData,
        nint decoderMemory, long decoderMemorySize, int threadPhase);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleLZ_CompressDelegate(
        int compressor, nint rawBuf, long rawLen, nint compBuf, int level,
        nint options, nint dictionaryBase, nint lrm, nint scratchMem, long scratchSize);

    /// <summary>True once a native Oodle library has been located and bound.</summary>
    public static bool IsAvailable
    {
        get
        {
            try { EnsureLoaded(); return true; }
            catch (OodleUnavailableException) { return false; }
        }
    }

    /// <summary>
    /// Decompresses <paramref name="compressed"/> into a buffer of exactly
    /// <paramref name="rawLength"/> bytes (the size the bundle TOC records).
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int rawLength)
    {
        EnsureLoaded();
        var raw = new byte[rawLength];
        var comp = compressed.ToArray();
        long produced;
        unsafe
        {
            fixed (byte* cp = comp)
            fixed (byte* rp = raw)
            {
                produced = _decompress!(
                    (nint)cp, comp.Length, (nint)rp, rawLength,
                    1 /*fuzzSafe*/, 0 /*checkCrc*/, 0 /*verbosity*/,
                    0, 0, 0, 0, 0, 0, 3 /*threadPhase: unthreaded*/);
            }
        }
        if (produced != rawLength)
        {
            throw new InvalidDataException(
                $"Oodle decompression produced {produced} bytes, expected {rawLength}.");
        }
        return raw;
    }

    /// <summary>
    /// Compresses <paramref name="raw"/> with Kraken using Oodle default options. The game's
    /// <c>OodleLZ_Decompress</c> handles multi-quantum streams; the key contract is that the
    /// bundle's Field1 (written by <see cref="AbfSaveBundle.Serialize"/>) equals the actual
    /// decompressed size so the game allocates the right buffer and passes the right rawLen.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        EnsureLoaded();
        var input = raw.ToArray();
        // Worst-case Oodle output: rawLen + 274 bytes per 256 KB block + small fixed header.
        var cap = input.Length + 274 * ((input.Length + 0x3FFFF) / 0x40000) + 64;
        var comp = new byte[cap];
        long produced;
        unsafe
        {
            fixed (byte* ip = input)
            fixed (byte* op = comp)
            {
                produced = _compress!(
                    CompressorKraken, (nint)ip, input.Length, (nint)op, CompressionLevelNormal,
                    0, 0, 0, 0, 0);
            }
        }
        if (produced <= 0)
            throw new InvalidDataException($"Oodle compression failed (returned {produced}).");
        return comp[..(int)produced];
    }

    private static void EnsureLoaded()
    {
        if (_resolved && _decompress is not null && _compress is not null) return;
        lock (Gate)
        {
            if (_resolved && _decompress is not null && _compress is not null) return;

            var dll = ResolveDllPath();
            if (dll is null)
            {
                throw new OodleUnavailableException(
                    "Could not locate the Oodle library (oo2core / oodle-data-shared.dll) near the installed "
                    + "game, and downloading it failed. The download needs an internet connection the first "
                    + "time it runs (it's cached afterwards, so you won't need to be online again). Connect "
                    + "to the internet and try again, or set ABIOTIC_OODLE_DLL to a copy's path.");
            }

            nint handle;
            try
            {
                handle = NativeLibrary.Load(dll);
            }
            catch (Exception ex)
            {
                throw new OodleUnavailableException($"Failed to load Oodle library '{dll}': {ex.Message}", ex);
            }

            if (!NativeLibrary.TryGetExport(handle, "OodleLZ_Decompress", out var decPtr)
                || !NativeLibrary.TryGetExport(handle, "OodleLZ_Compress", out var compPtr))
            {
                throw new OodleUnavailableException(
                    $"Oodle library '{dll}' is missing OodleLZ_Compress/Decompress exports.");
            }

            _decompress = Marshal.GetDelegateForFunctionPointer<OodleLZ_DecompressDelegate>(decPtr);
            _compress = Marshal.GetDelegateForFunctionPointer<OodleLZ_CompressDelegate>(compPtr);
            _resolved = true;
            Diagnostics.EditorLog.Info("GamePass", $"Oodle bound from {dll}");
        }
    }

    private static string? ResolveDllPath()
    {
        // 1. Explicit override.
        var env = Environment.GetEnvironmentVariable("ABIOTIC_OODLE_DLL");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        // 2. The installed game ships oo2core next to its executable.
        foreach (var candidate in GameInstallOodleCandidates())
        {
            if (File.Exists(candidate)) return candidate;
        }

        // 3. A copy this codec already downloaded on a previous run, so a machine that has been
        // online once doesn't need internet again just to open a Game Pass save offline later.
        foreach (var name in DllNames)
        {
            var cached = Path.Combine(CacheDir, name);
            if (File.Exists(cached)) return cached;
        }

        // 4. CUE4Parse downloads oodle-data-shared.dll on demand. Load the file it just wrote,
        // not a copy: on Linux the download sets the executable bit on that exact file, and a
        // plain File.Copy of a native library is not guaranteed to preserve everything the loader
        // needs. A best-effort copy into our cache dir (step 3) is still made for next time, but
        // it never changes what gets loaded this run.
        try
        {
            string? path = null;
            if (CUE4Parse.Compression.OodleHelper.DownloadOodleDll(ref path)
                && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                TryCacheForNextRun(path);
                return path;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Oodle DLL download failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>Best-effort copies a freshly downloaded library into <see cref="CacheDir"/> so a
    /// later run finds it via step 3 without needing internet again. Never affects this run:
    /// failures (read-only cache dir, etc.) are swallowed since the download already succeeded.</summary>
    private static void TryCacheForNextRun(string downloadedPath)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var dest = Path.Combine(CacheDir, Path.GetFileName(downloadedPath));
            if (string.Equals(Path.GetFullPath(downloadedPath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            File.Copy(downloadedPath, dest, overwrite: true);
            if (OperatingSystem.IsLinux())
            {
                // File.Copy does not carry over the executable bit DownloadOodleDllFromOodleUEAsync
                // set on the original; without it a future run's cache hit could fail to load.
                File.SetUnixFileMode(dest,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Could not cache the downloaded Oodle library for reuse: {ex.Message}");
        }
    }

    private static IEnumerable<string> GameInstallOodleCandidates()
    {
        string?[] roots = { AfInstallLocator.FindPaksDirectory(), AfInstallLocator.FindInstallRoot() };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            // The paks dir is <install>/AbioticFactor/Content/Paks; the DLL lives a few levels up
            // near the executable (<install>/AbioticFactor/Binaries/Win64 or the install root).
            var dirs = new List<string> { root };
            var d = root;
            for (var i = 0; i < 5 && Path.GetDirectoryName(d) is { } parent; i++)
            {
                dirs.Add(parent);
                d = parent;
            }
            foreach (var dir in dirs)
            {
                foreach (var name in DllNames)
                {
                    yield return Path.Combine(dir, name);
                    yield return Path.Combine(dir, "Binaries", "Win64", name);
                }
            }
        }
    }
}

/// <summary>Thrown when no native Oodle library can be located or downloaded.</summary>
public sealed class OodleUnavailableException : Exception
{
    public OodleUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
