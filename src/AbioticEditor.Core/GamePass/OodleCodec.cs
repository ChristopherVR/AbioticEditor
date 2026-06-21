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
/// installed game's copy (next to its executable), then CUE4Parse's downloader (the same
/// mechanism the editor already uses for pak decompression). When none can be obtained the
/// codec throws <see cref="OodleUnavailableException"/> so callers can degrade with a clear
/// message rather than a crash.</para>
/// </summary>
public static class OodleCodec
{
    // OodleLZ_Compressor: Kraken is a good general default; the decompressor auto-detects the
    // codec from the stream, so the editor need not match the game's exact compressor.
    private const int CompressorKraken = 8;

    // OodleLZ_CompressionLevel_Normal. Any valid level produces a stream the game can decode.
    private const int CompressionLevelNormal = 4;

    private static readonly object Gate = new();
    private static bool _resolved;
    private static OodleLZ_DecompressDelegate? _decompress;
    private static OodleLZ_CompressDelegate? _compress;
    private static OodleLZ_CompressOptions_GetDefaultDelegate? _compressOptionsGetDefault;

    // OodleLZ_CompressOptions struct (oo2core_9 layout, 48 bytes).
    // seekChunkLen=0 disables seek-chunk splitting so the entire payload is one quantum;
    // that matches how the game writes the bundle and how OodleLZ_Decompress reads it
    // (one call, rawLen = total uncompressed size).
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct OodleLZ_CompressOptions
    {
        public int Verbosity;
        public int MinMatchLen;
        public int SeekChunkReset;
        public int SeekChunkLen;      // 0 = no seek chunks = single quantum
        public int Profile;
        public int DictionarySize;
        public int SpaceSpeedTradeoffBytes;
        public int Reserved1;
        public int SendQuantumCRCs;
        public int MaxLocalDictionarySize;
        public int MakeLongRangeMatcher;
        public int LookAheadSize;
        // 4 bytes of implicit padding to reach Size=48
    }

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OodleLZ_CompressOptions_GetDefaultDelegate(
        int compressor, int level, nint outOptions);

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
    /// Compresses <paramref name="raw"/> with Kraken as a SINGLE Oodle quantum (seekChunkLen=0).
    /// The game's GDK save reader calls <c>OodleLZ_Decompress(blob, blobSize, out, totalRawSize)</c>
    /// in one shot; that expects the compressed data to be one self-contained stream whose
    /// embedded rawLen equals the total. Using the default seek chunk length (512 KB) splits any
    /// payload larger than 512 KB into multiple independent streams; <c>OodleLZ_Decompress</c>
    /// only decodes the first and returns its size (524288), causing a mismatch error against the
    /// expected totalRawSize. Setting seekChunkLen=0 forces the whole payload into one quantum.
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
            // Build options with seekChunkLen=0 (no seek chunks = one quantum for the whole
            // payload). If GetDefault is available it fills in the remaining fields; we then
            // force seekChunkLen back to 0 since the game writes its own bundles that way.
            var opts = new OodleLZ_CompressOptions
            {
                Verbosity = 0,
                MinMatchLen = 0,
                SeekChunkReset = 0,
                SeekChunkLen = 0,           // 0 = no seek-chunk splitting = single quantum
                Profile = 0,
                DictionarySize = -1,        // Oodle default: auto
                SpaceSpeedTradeoffBytes = 256,
                Reserved1 = 0,
                SendQuantumCRCs = 0,
                MaxLocalDictionarySize = 0,
                MakeLongRangeMatcher = 1,
                LookAheadSize = 0,
            };
            if (_compressOptionsGetDefault != null)
            {
                // opts is a stack local - take address directly, no fixed needed.
                _compressOptionsGetDefault(CompressorKraken, CompressionLevelNormal, (nint)(&opts));
                opts.SeekChunkLen = 0;      // override after GetDefault
            }
            fixed (byte* ip = input)
            fixed (byte* op = comp)
            {
                produced = _compress!(
                    CompressorKraken, (nint)ip, input.Length, (nint)op, CompressionLevelNormal,
                    (nint)(&opts), 0, 0, 0, 0);
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
                    "Could not locate or download the Oodle library (oo2core / oodle-data-shared.dll). "
                    + "Set ABIOTIC_OODLE_DLL to its path, or install Abiotic Factor.");
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

            // Optional - available in oo2core_9; used to fill in safe defaults before we
            // override seekChunkLen. Missing export is not fatal; we hard-code the same values.
            if (NativeLibrary.TryGetExport(handle, "OodleLZ_CompressOptions_GetDefault", out var getDefaultPtr))
            {
                _compressOptionsGetDefault =
                    Marshal.GetDelegateForFunctionPointer<OodleLZ_CompressOptions_GetDefaultDelegate>(getDefaultPtr);
            }

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

        // 3. CUE4Parse downloads oodle-data-shared.dll on demand (cached in the working dir).
        try
        {
            string? path = null;
            if (CUE4Parse.Compression.OodleHelper.DownloadOodleDll(ref path)
                && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Oodle DLL download failed: {ex.Message}");
        }
        return null;
    }

    private static IEnumerable<string> GameInstallOodleCandidates()
    {
        string?[] roots = { AfInstallLocator.FindPaksDirectory(), AfInstallLocator.FindInstallRoot() };
        var names = new[] { "oo2core_9_win64.dll", "oodle-data-shared.dll" };
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
                foreach (var name in names)
                {
                    yield return Path.Combine(dir, name);
                }
                yield return Path.Combine(dir, "Binaries", "Win64", names[0]);
            }
        }
    }
}

/// <summary>Thrown when no native Oodle library can be located or downloaded.</summary>
public sealed class OodleUnavailableException : Exception
{
    public OodleUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
