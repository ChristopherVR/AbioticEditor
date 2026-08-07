using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// The list of game pictures that were extracted ahead of time and shipped with the editor:
/// skill icons, trader portraits, chapter cards, sector maps, creature and pet portraits, and
/// the appearance previews. Item icons are NOT here - those are keyed by item id and handled
/// separately (see the CLI's <c>dump-icons</c>).
///
/// This exists for the same reason <see cref="GameDataRegistry"/> does: the browser build cannot
/// mount the game's pak archives, so anything it needs from them has to be dumped in advance.
/// A host with the game installed ignores this entirely and extracts live.
///
/// It is only a list of names. Knowing WHICH pictures shipped is what lets a screen decide
/// between drawing one and drawing its fallback symbol, without firing a request that 404s.
/// </summary>
public sealed class BundledArt
{
    /// <summary>Bumped when the on-disk shape changes incompatibly; a mismatch is ignored, not loaded.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this payload (see <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Game asset refs that have a matching PNG in the same folder as this manifest.</summary>
    public IReadOnlyList<string> Refs { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    private HashSet<string>? _lookup;

    /// <summary>True when a picture for <paramref name="gameRef"/> shipped with the editor.</summary>
    public bool Has(string? gameRef)
    {
        if (string.IsNullOrEmpty(gameRef)) return false;
        _lookup ??= new HashSet<string>(Refs, StringComparer.OrdinalIgnoreCase);
        return _lookup.Contains(gameRef);
    }

    /// <summary>
    /// The file name a given game ref is stored under. Derived from the ref itself rather than
    /// numbered, so re-running the dump against a new game build rewrites the same names instead
    /// of renaming every file (which would show up as thousands of changes in the repo).
    /// </summary>
    public static string FileNameFor(string gameRef)
    {
        var name = new StringBuilder(gameRef.Length + 4);
        foreach (var c in gameRef)
        {
            name.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }
        return name.Append(".png").ToString();
    }

    /// <summary>The canonical manifest file name, inside the art folder.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>Writes the manifest to <paramref name="path"/> (creating parent dirs).</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, this, BundledArtJsonContext.Default.BundledArt);
    }

    /// <summary>Reads a manifest from already-fetched bytes; null when unreadable or the wrong version.</summary>
    public static BundledArt? TryRead(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(utf8Json, BundledArtJsonContext.Default.BundledArt);
            if (manifest is null) return null;
            if (manifest.SchemaVersion != CurrentSchemaVersion)
            {
                EditorLog.Warn("Art",
                    $"Bundled art manifest is schema v{manifest.SchemaVersion}, editor expects "
                    + $"v{CurrentSchemaVersion}; ignoring it.");
                return null;
            }
            return manifest;
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Art", "Failed to read the supplied art manifest.", ex);
            return null;
        }
    }

    private static BundledArt? Supplied;

    /// <summary>
    /// Hands the manifest to <see cref="LoadBundled"/> directly, for a host that cannot read it
    /// off disk. The browser build fetches it over HTTP at startup and calls this. Mirrors
    /// <see cref="GameDataRegistry.Supply"/>.
    /// </summary>
    public static void Supply(BundledArt? manifest) => Supplied = manifest;

    /// <summary>The manifest to use, or null when none shipped and none was supplied.</summary>
    public static BundledArt? LoadBundled()
    {
        if (Supplied is { } supplied) return supplied;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "art", ManifestFileName);
            if (!File.Exists(path)) return null;
            return TryRead(File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Art", "Failed to read the bundled art manifest.", ex);
            return null;
        }
    }
}

/// <summary>Source-generated (trim/AOT-safe) JSON context for <see cref="BundledArt"/>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BundledArt))]
public partial class BundledArtJsonContext : JsonSerializerContext
{
}
