using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

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
    /// <remarks>
    /// v2 added every catalog below <see cref="Items"/>. v1 carried items alone, which was enough
    /// for the desktop app (it reads the rest straight from the install) but left the browser
    /// build with no recipes, no codex, no traders, no skills and no appearance options at all.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Schema version of this payload (see <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// The game build the dump was taken from, when known (e.g. "1.0.3"). Informational for now -
    /// the load path always prefers a live install over the registry, so a stale stamp only matters
    /// when the game is absent. Stamped by the dump command.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>
    /// The game culture this dump's text was read in (e.g. <c>"ru"</c>), or null for the game's
    /// default (English). Every display name, description, email and journal entry in here is in
    /// that one language, so a host with no game install needs the dump matching the player's
    /// language - see <see cref="FileNameFor"/>.
    /// </summary>
    public string? Culture { get; init; }

    /// <summary>
    /// The file a given game culture's dump is stored under: <c>registry.ru.json</c>, and plain
    /// <c>registry.json</c> for the default. Kept as a rule rather than an index so a host can
    /// ask for a language directly and fall back when it did not ship.
    /// </summary>
    public static string FileNameFor(string? culture)
        => string.IsNullOrWhiteSpace(culture) ? RegistryFileName : $"registry.{culture}.json";

    /// <summary>
    /// The cultures a dump ships for, in the game's own spelling. Kept as a list here rather than
    /// discovered at run time so a host can pick the right file with no wasted request: asking for
    /// a language that did not ship used to mean a 404 on every single page load.
    /// </summary>
    /// <remarks>
    /// Must match the files in <c>assets/registry/</c>; a test asserts the two agree, so a dump
    /// that adds or drops a language fails the build rather than silently mismatching.
    /// </remarks>
    public static IReadOnlyList<string> BundledCultures { get; } =
        ["de", "en", "es-419", "fr", "ja", "pt-BR", "ru", "zh-Hans", "zh-Hant"];

    /// <summary>
    /// The shipped culture closest to <paramref name="requested"/> (a culture name like
    /// <c>"de-DE"</c> or <c>"pt-BR"</c>), or null to use the default dump.
    /// </summary>
    /// <remarks>
    /// Exact match first, because the game ships some regional variants (<c>pt-BR</c>,
    /// <c>es-419</c>) and not their base language. Then any shipped culture with the same
    /// language, so <c>de-AT</c> gets German and <c>pt-PT</c> gets the Brazilian text rather than
    /// falling all the way back to English.
    /// </remarks>
    public static string? BestCultureFor(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;

        foreach (var culture in BundledCultures)
        {
            if (string.Equals(culture, requested, StringComparison.OrdinalIgnoreCase)) return culture;
        }

        var language = requested.Split('-')[0];
        foreach (var culture in BundledCultures)
        {
            if (string.Equals(culture.Split('-')[0], language, StringComparison.OrdinalIgnoreCase)) return culture;
        }

        return null;
    }

    // ----- catalog payloads (each nullable so older/newer bundles degrade to "absent") -----

    /// <summary>Every item row (<c>ItemTable_Global</c> + supplemental tables); null if not dumped.</summary>
    public IReadOnlyList<ItemCatalogEntry>? Items { get; init; }

    /// <summary>
    /// Item id -> the DataTable object reference its row lives in, mirroring
    /// <see cref="ItemTableIndex"/> so the save writers resolve row tables offline.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ItemTableRefs { get; init; }

    /// <summary>Every craftable recipe (<c>DT_Recipes</c> and friends); null if not dumped.</summary>
    public IReadOnlyList<RecipeInfo>? Recipes { get; init; }

    /// <summary>The item upgrade graph (<c>DT_ItemUpgrades</c>); null if not dumped.</summary>
    public IReadOnlyList<ItemUpgrade>? ItemUpgrades { get; init; }

    /// <summary>Level/region names the editor can teleport to; null if not dumped.</summary>
    public IReadOnlyList<string>? Maps { get; init; }

    /// <summary>Skill rows with their descriptions and icon paths; null if not dumped.</summary>
    public IReadOnlyList<SkillDefinition>? Skills { get; init; }

    /// <summary>Skill display name -> its per-level perks (<c>DT_SkillPerks</c>); null if not dumped.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SkillMilestone>>? SkillMilestones { get; init; }

    /// <summary>Trait/background id -> its full row (description, point cost); null if not dumped.</summary>
    public IReadOnlyDictionary<string, TraitDetail>? Traits { get; init; }

    /// <summary>Customization table name -> its selectable rows; null if not dumped.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>>? Customization { get; init; }

    /// <summary>Codex emails; null if not dumped.</summary>
    public IReadOnlyList<EmailEntry>? Emails { get; init; }

    /// <summary>Codex journal objectives; null if not dumped.</summary>
    public IReadOnlyList<JournalEntry>? Journals { get; init; }

    /// <summary>Codex compendium lore entries; null if not dumped.</summary>
    public IReadOnlyList<CompendiumEntry>? Compendium { get; init; }

    /// <summary>Catchable fish rows; null if not dumped.</summary>
    public IReadOnlyList<FishDefinition>? Fish { get; init; }

    /// <summary>The trader roster and their barter stock; null if not dumped.</summary>
    public IReadOnlyList<TraderInfo>? Traders { get; init; }

    /// <summary>The game's own drawn sector maps and which level each depicts; null if not dumped.</summary>
    public IReadOnlyList<SectorMapInfo>? SectorMaps { get; init; }

    /// <summary>
    /// Builds a registry from a mounted game install. Requires usmap mappings (each catalog's
    /// own loader throws without them). Adding a catalog: load it here and assign the payload.
    /// </summary>
    /// <remarks>
    /// Every catalog past the item table is read through <see cref="Optional"/>: one table the
    /// current game build renamed must not throw away the whole dump, because a registry that
    /// fails to build is the difference between "one screen is missing data" and "the browser
    /// editor shows nothing at all". The items table stays required - without it there is no
    /// useful registry to write.
    /// </remarks>
    public static GameDataRegistry BuildFromInstall(GameAssetProvider provider, string? gameVersion = null, string? culture = null)
    {
        var catalog = ItemCatalog.LoadFrom(provider);
        return new GameDataRegistry
        {
            SchemaVersion = CurrentSchemaVersion,
            GameVersion = gameVersion,
            Culture = culture,
            Items = catalog.Entries.ToList(),
            ItemTableRefs = catalog.TableRefs,
            Recipes = Optional("recipes", () => RecipeCatalog.LoadInfosFrom(provider)),
            ItemUpgrades = Optional("item upgrades", () => ItemUpgradeCatalog.LoadFrom(provider).Upgrades),
            Maps = Optional("maps", () => MapCatalog.LoadFrom(provider)),
            Skills = Optional("skills", () => SkillCatalog.LoadFrom(provider)),
            SkillMilestones = Optional("skill perks", () => SkillMilestoneCatalog.LoadFrom(provider)),
            Traits = Optional("traits", () => TraitCatalog.LoadDetailsFrom(provider)),
            Customization = Optional("appearance options", () => CustomizationCatalog.LoadFrom(provider)),
            Emails = Optional("emails", () => CodexCatalog.LoadEmails(provider)),
            Journals = Optional("journals", () => CodexCatalog.LoadJournals(provider)),
            Compendium = Optional("compendium", () => CodexCatalog.LoadCompendium(provider)),
            Fish = Optional("fish", () => CodexCatalog.LoadFish(provider)),
            Traders = Optional("traders", () => TraderCatalog.LoadFrom(provider)),
            SectorMaps = Optional("sector maps", () => SectorMapCatalog.LoadFrom(provider)),
        };
    }

    /// <summary>Reads one optional catalog, logging and skipping it rather than failing the dump.</summary>
    private static T? Optional<T>(string what, Func<T?> load) where T : class
    {
        try { return load(); }
        catch (Exception ex)
        {
            EditorLog.Warn("Registry", $"Skipping {what} in the game-data dump; the table could not be read.", ex);
            return null;
        }
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
        // A host with no file system supplies the registry directly (see Supply). Checked first
        // so it does not depend on a virtual file system behaving like a real one.
        if (Supplied is { } supplied) return supplied;

        if (File.Exists(UserRegistryPath)) return TryLoad(UserRegistryPath);

        // The player's chosen game-data language first, then the default. A player reading the
        // editor in German with no game installed should see German item names, not English ones.
        var directory = Path.Combine(AppContext.BaseDirectory, "registry");
        if (GameDataLanguageStore.Saved is { Length: > 0 } culture)
        {
            var localized = Path.Combine(directory, FileNameFor(culture));
            if (File.Exists(localized) && TryLoad(localized) is { } match) return match;
        }

        return TryLoad(Path.Combine(directory, RegistryFileName));
    }

    private static GameDataRegistry? Supplied;

    /// <summary>
    /// Hands the registry to <see cref="LoadBundled"/> directly, for a host that cannot read it
    /// off disk. The browser build fetches it over HTTP at startup and calls this.
    /// </summary>
    /// <remarks>
    /// The alternative - writing the bytes into the in-memory file system WebAssembly provides
    /// and letting the normal path find them - looked simpler but failed silently: the registry
    /// never loaded, every catalog came back empty, and because that failure is only ever written
    /// to the log FILE there was nothing in the browser console to say so. Supplying the object
    /// removes the guesswork about what a virtual file system does with an absolute path.
    /// </remarks>
    public static void Supply(GameDataRegistry? registry) => Supplied = registry;

    /// <summary>
    /// Reads a registry from already-fetched bytes. Same validation as <see cref="TryLoad(string)"/>.
    /// </summary>
    public static GameDataRegistry? TryRead(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var registry = JsonSerializer.Deserialize(utf8Json, GameDataRegistryJsonContext.Default.GameDataRegistry);
            if (registry is null) return null;
            if (registry.SchemaVersion != CurrentSchemaVersion)
            {
                EditorLog.Warn("Registry",
                    $"Bundled registry is schema v{registry.SchemaVersion}, editor expects "
                    + $"v{CurrentSchemaVersion}; ignoring it.");
                return null;
            }
            return registry;
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Registry", "Failed to read the supplied registry.", ex);
            return null;
        }
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
