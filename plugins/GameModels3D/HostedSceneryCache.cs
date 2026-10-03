using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>Optional Pages data, copied into the ordinary local cache after integrity checks.</summary>
internal sealed partial class HostedSceneryCache
{
    internal const string Site = "https://christophervr.github.io/AbioticEditor/scenery/v1/";
    private const int MaxFileBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly HttpClient _client;
    private readonly Uri _root;
    private readonly Lazy<Manifest?> _manifest;
    private readonly ConcurrentDictionary<string, Lazy<bool>> _requests = new(StringComparer.Ordinal);
    private int _offline;

    internal sealed record Entry(long Size, string Sha256);
    internal sealed record Manifest(int Format, string Signature, Dictionary<string, Entry> Files);

    internal HostedSceneryCache(string signature, HttpClient? client = null, Uri? site = null)
    {
        _client = client ?? Client;
        _root = new Uri(site ?? new Uri(Site), signature + "/");
        _manifest = new Lazy<Manifest?>(() => ReadManifest(signature));
    }

    /// <summary>Portable archive identity: pak footer/index signatures, sizes and mappings, never install dates.</summary>
    internal static string? Signature(string? paks, string? mappings)
    {
        if (paks is null || mappings is null || !Directory.Exists(paks) || !File.Exists(mappings)) return null;
        var files = Directory.EnumerateFiles(paks).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".pak" or ".utoc" or ".ucas")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return null;
        var identity = new StringBuilder("scenery-v1\n");
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            var tail = new byte[Path.GetExtension(file).Equals(".utoc", StringComparison.OrdinalIgnoreCase)
                ? checked((int)stream.Length) : Path.GetExtension(file).Equals(".ucas", StringComparison.OrdinalIgnoreCase)
                    ? 0 : (int)Math.Min(65536, stream.Length)];
            stream.Position = stream.Length - tail.Length;
            stream.ReadExactly(tail);
            identity.Append(Path.GetFileName(file).ToLowerInvariant()).Append(':')
                .Append(stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(Hash(tail)).Append('\n');
        }
        identity.Append("mappings:").Append(Hash(File.ReadAllBytes(mappings))).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(identity.ToString()));
    }

    internal bool Fetch(string folder, string filename, string destination)
    {
        var key = folder + "/" + filename;
        if (Volatile.Read(ref _offline) != 0 || !CacheFile().IsMatch(key)) return false;
        return _requests.GetOrAdd(key, _ => new Lazy<bool>(() => Download(key, destination))).Value;
    }

    private Manifest? ReadManifest(string signature)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = _client.GetAsync(new Uri(_root, "manifest.json"), HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream();
            var bytes = ReadLimited(stream, 8 * 1024 * 1024, deadline.Token);
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes, Json);
            return manifest is { Format: 1 } && manifest.Signature == signature && manifest.Files is not null ? manifest : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            Interlocked.Exchange(ref _offline, 1);
            return null;
        }
    }

    private bool Download(string key, string destination)
    {
        if (_manifest.Value is not { } manifest || !manifest.Files.TryGetValue(key, out var entry)
            || entry is null || entry.Size <= 0 || entry.Size > MaxFileBytes || !Digest().IsMatch(entry.Sha256 ?? "")) return false;
        string? temporary = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = _client.GetAsync(new Uri(_root, key), HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream();
            var bytes = ReadLimited(stream, (int)entry.Size, deadline.Token);
            if (bytes.LongLength != entry.Size || Hash(bytes) != entry.Sha256) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // One failed connection is enough. Offline areas fall back to the player's game files.
            Interlocked.Exchange(ref _offline, 1);
            return File.Exists(destination);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[] ReadLimited(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        int count;
        while ((count = stream.ReadAsync(block.AsMemory(), cancellationToken).AsTask().GetAwaiter().GetResult()) != 0)
        {
            if (buffer.Length + count > limit) throw new IOException("Hosted scenery exceeds its declared size.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [GeneratedRegex("^[a-z0-9-]+/[a-f0-9]{64}\\.(json|bin|abm|png)$", RegexOptions.CultureInvariant)]
    private static partial Regex CacheFile();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Digest();
}
