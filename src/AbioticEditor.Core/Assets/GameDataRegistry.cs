using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// A pre-extracted snapshot of the game's data tables, dumped once from a real install (see the
/// CLI's <c>dump-registry</c> command) and bundled in the editor's <c>assets/</c> so the catalogs
/// work with no game installed.
///
/// This is the generalization of the per-catalog hand-written <c>Fallback</c> tables
/// (<see cref="PlayerSaves.SkillCatalog.Fallback"/>, <c>TraderCatalog.Fallback</c>, ...): instead
/// of curating those by hand, the registry is generated from the paks and covers far more.
///
/// What it deliberately does NOT carry: icons/textures and fonts (binary pak assets), which still
/// need the live install. The registry stores icon <em>paths</em>, so the editor shows names and
/// stats offline and fills in icons only when the game is present.
///
/// Live pak data always wins when the game is installed (richer, with icons and DLC tables picked
/// up automatically); the registry is the fallback, not a replacement.
/// </summary>
public sealed class GameDataRegistry
{
    /// <summary>Bumped when the on-disk shape changes incompatibly; a mismatch is ignored, not loaded.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this payload (see <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// The game build the dump was taken from, when known (e.g. "1.0.3"). Informational for now -
    /// the load path always prefers a live install over the registry, so a stale stamp only matters
    /// when the game is absent. Stamped by the dump command.
    /// </summary>
    public string? GameVersion { get; init; }

    // ----- catalog payloads (each nullable so older/newer bundles degrade to "absent") -----

    /// <summary>Every item row (<c>ItemTable_Global</c> + supplemental tables); null if not dumped.</summary>
    public IReadOnlyList<ItemCatalogEntry>? Items { get; init; }

    /// <summary>
    /// Item id -> the DataTable object reference its row lives in, mirroring
    /// <see cref="ItemTableIndex"/> so the save writers resolve row tables offline.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ItemTableRefs { get; init; }

    /// <summary>
    /// Builds a registry from a mounted game install. Requires usmap mappings (each catalog's
    /// own loader throws without them). Adding a catalog: load it here and assign the payload.
    /// </summary>
    public static GameDataRegistry BuildFromInstall(GameAssetProvider provider, string? gameVersion = null)
    {
        var catalog = ItemCatalog.LoadFrom(provider);
        return new GameDataRegistry
        {
            SchemaVersion = CurrentSchemaVersion,
            GameVersion = gameVersion,
            Items = catalog.Entries.ToList(),
            ItemTableRefs = catalog.TableRefs,
        };
    }

    /// <summary>Serializes this registry to <paramref name="path"/> (creating parent dirs).</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, this, GameDataRegistryJsonContext.Default.GameDataRegistry);
    }

    /// <summary>
    /// Loads and validates a registry file, or returns null if it's absent, unreadable, or carries
    /// an unsupported <see cref="SchemaVersion"/>. Never throws - a bad bundle just means "no
    /// registry", and the editor degrades to empty catalogs exactly as before.
    /// </summary>
    public static GameDataRegistry? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            var registry = JsonSerializer.Deserialize(fs, GameDataRegistryJsonContext.Default.GameDataRegistry);
            if (registry is null) return null;
            if (registry.SchemaVersion != CurrentSchemaVersion)
            {
                EditorLog.Warn("Registry",
                    $"Bundled registry at '{path}' is schema v{registry.SchemaVersion}, editor expects "
                    + $"v{CurrentSchemaVersion}; ignoring it.");
                return null;
            }
            return registry;
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Registry", $"Failed to read registry at '{path}'.", ex);
            return null;
        }
    }

    /// <summary>
    /// Finds the registry to use, or null. Resolution mirrors
    /// <see cref="GameAssetProvider.FindConventionalMappings"/>:
    /// 1. <c>%LOCALAPPDATA%/AbioticEditor/registry/registry.json</c> (user-supplied, wins so a
    ///    fresh dump can override the bundled one), then
    /// 2. <c>registry/registry.json</c> next to the executable (bundled with the editor).
    /// </summary>
    public static GameDataRegistry? LoadBundled()
    {
        if (File.Exists(UserRegistryPath)) return TryLoad(UserRegistryPath);

        var bundled = Path.Combine(AppContext.BaseDirectory, "registry", RegistryFileName);
        return TryLoad(bundled);
    }

    /// <summary>The canonical registry file name (same name in both the user-override and bundled dirs).</summary>
    public const string RegistryFileName = "registry.json";

    /// <summary>
    /// The user-override registry location. A file here wins over the bundled one, so players on
    /// newer game builds can drop in a fresh dump without updating the editor.
    /// </summary>
    public static string UserRegistryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "registry",
        RegistryFileName);
}

/// <summary>
/// System.Text.Json source-generated (reflection-free, trim/AOT-safe) context for the registry.
/// Adding a catalog payload that uses a new collection/record shape may need a matching
/// <c>[JsonSerializable]</c> entry here.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GameDataRegistry))]
public partial class GameDataRegistryJsonContext : JsonSerializerContext
{
}
