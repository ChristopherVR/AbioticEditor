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

    // The game's GDK save reader decompresses in 512 KB quanta. Each independently-compressed
    // chunk must not exceed this size as raw output, otherwise the game's first OodleLZ_Decompress
    // call requests 524288 bytes of output but our single stream header encodes a larger raw
    // length, which Oodle rejects with "LZ corruption : OodleLZ_Decompress failed".
    private const int QuantumSize = 512 * 1024;

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
    /// Compresses <paramref name="raw"/> with Kraken in <see cref="QuantumSize"/> chunks so the
    /// compressed output matches the game's chunked decompression: each independent chunk decodes
    /// to at most 512 KB, matching the quantum size the GDK save reader passes to
    /// <c>OodleLZ_Decompress</c>. The chunks are concatenated; <see cref="Decompress"/> handles
    /// them correctly since <c>OodleLZ_Decompress</c> processes multiple chunks when given the
    /// full buffer and the total uncompressed length.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        EnsureLoaded();
        using var result = new MemoryStream();
        var offset = 0;
        while (offset < raw.Length)
        {
            var chunkLen = Math.Min(QuantumSize, raw.Length - offset);
            var chunk = raw.Slice(offset, chunkLen).ToArray();
            // Worst-case Oodle output is rawLen + 274 bytes per 256 KB block plus a small header.
            var cap = chunkLen + 274 * ((chunkLen + 0x3FFFF) / 0x40000) + 64;
            var comp = new byte[cap];
            long produced;
            unsafe
            {
                fixed (byte* ip = chunk)
                fixed (byte* op = comp)
                {
                    produced = _compress!(
                        CompressorKraken, (nint)ip, chunkLen, (nint)op, CompressionLevelNormal,
                        0, 0, 0, 0, 0);
                }
            }
            if (produced <= 0)
            {
                throw new InvalidDataException($"Oodle compression failed on chunk at offset {offset} (returned {produced}).");
            }
            result.Write(comp, 0, (int)produced);
            offset += chunkLen;
        }
        return result.ToArray();
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
