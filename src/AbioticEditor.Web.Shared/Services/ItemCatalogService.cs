using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Supplies the Razor inventory palette from the same generated game-data registry and
/// live texture extractor used by the retired native slot sidebar. Registry metadata is
/// available offline; icon extraction is attempted only when the local game data exists.
/// </summary>
public sealed class ItemCatalogService : IDisposable
{
    private readonly IReadOnlyList<ItemCatalogEntry> _entries;
    private readonly Dictionary<string, ItemCatalogEntry> _byId;
    public ItemVariantCatalog Variants { get; }
    private readonly Lazy<GameAssetProvider?> _provider = new(CreateProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Caps how many icons are decoded at once. Opening a catalog category asks for ~72 icons
    /// in the same instant, and on the first run of an install none of them are on disk yet.
    /// The provider decodes textures under its own lock, so those requests were serialized
    /// anyway - but each one still parked a thread-pool thread waiting its turn, which starved
    /// the render loop and made the whole editor feel frozen while a category loaded. Letting a
    /// couple through at a time costs nothing in throughput and leaves the UI responsive.
    /// </summary>
    private static readonly SemaphoreSlim IconDecodeGate = new(2, 2);

    private readonly bool _extractsIconsLive;

    /// <param name="files">
    /// Tells the two hosts apart. A host that can reach the local machine extracts icons from
    /// the installed game on demand and serves them from its own endpoint; a browser cannot, so
    /// it uses the icon set dumped ahead of time and shipped as static files.
    /// </param>
    public ItemCatalogService(ProgressionVocabularyService? liveVocabulary = null, ISaveFileSystem? files = null)
    {
        _extractsIconsLive = files is null || files.HasLocalPaths;
        var registry = GameDataRegistry.LoadBundled();
        _bundledNpcDisplayNames = registry?.NpcDisplayNames;
        _narrativeNpcNames = registry?.NarrativeNpcNames;
        var merged = (registry?.Items ?? []).ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        // The slot editor is always present in the desktop shell. Do not make resolving it
        // mount and scan the installed game paks before a save can open. Bundled registry
        // data is immediately usable; merge live data only when another explicit workflow
        // has already loaded it.
        if (liveVocabulary is not null && liveVocabulary.TryGetItemEntries(out var liveEntries))
        {
            foreach (var entry in liveEntries) merged[entry.Id] = entry;
        }
        _entries = merged.Values
            .Where(IsBrowsable)
            .OrderBy(entry => string.Equals(entry.DisplayName, entry.Id, StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _byId = _entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var variantEntries = registry?.ItemVariants ?? Array.Empty<ItemVariantDefinition>();
        if (liveVocabulary is not null && liveVocabulary.TryGetItemVariants(out var liveVariants))
            variantEntries = liveVariants;
        Variants = ItemVariantCatalog.FromRegistry(variantEntries);
    }

    private Task<IReadOnlyList<AbioticEditor.Core.Ini.SandboxSettingDefinition>>? _sandboxSettings;
    public Task<IReadOnlyList<AbioticEditor.Core.Ini.SandboxSettingDefinition>> GetSandboxSettingsAsync()
        => _sandboxSettings ??= Task.Run<IReadOnlyList<AbioticEditor.Core.Ini.SandboxSettingDefinition>>(() =>
        {
            try { return _extractsIconsLive && _provider.Value is { } provider
                ? AbioticEditor.Core.Ini.SandboxSettingCatalog.Load(provider) : []; }
            catch (Exception) { return []; }
        });

    private Task<IReadOnlyList<WeaponCoatingDefinition>>? _coatings;
    public Task<IReadOnlyList<WeaponCoatingDefinition>> GetCoatingsAsync()
        => _coatings ??= Task.Run<IReadOnlyList<WeaponCoatingDefinition>>(() =>
        {
            try { return _extractsIconsLive && _provider.Value is { } provider ? WeaponCoatingCatalog.Load(provider) : []; }
            catch (Exception) { return []; }
        });

    private Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetCareDefinition>>? _petCare;
    private Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetVariant>>? _petVariants;

    public Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetVariant>> GetPetVariantsAsync()
        => _petVariants ??= Task.Run<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetVariant>>(() =>
        {
            lock (GameDataGate.Sync)
            {
                try
                {
                    if (_extractsIconsLive && _provider.Value is { HasMappings: true } provider)
                        AbioticEditor.Core.WorldSaves.PetCatalog.ApplyGameData(
                            AbioticEditor.Core.WorldSaves.PetGameData.TryLoadFrom(provider));
                }
                catch (Exception) { /* The curated companion catalog remains available offline. */ }
                return AbioticEditor.Core.WorldSaves.PetCatalog.BuildVariants(null);
            }
        });

    public Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetCareDefinition>> GetPetCareAsync()
        => _petCare ??= Task.Run<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetCareDefinition>>(() =>
        {
            try { return _extractsIconsLive && _provider.Value is { } provider ? AbioticEditor.Core.WorldSaves.PetCareCatalog.Load(provider) : []; }
            catch (Exception) { return []; }
        });

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetMutationOption>>> _petMutationOptions = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The valid <c>PetMutation</c> targets for a carried pet (item row) - see
    /// <see cref="AbioticEditor.Core.WorldSaves.PetCareCatalog.MutationOptionsFor"/>.</summary>
    public Task<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetMutationOption>> GetPetMutationOptionsAsync(string itemRow)
        => _petMutationOptions.GetOrAdd(itemRow, row => Task.Run<IReadOnlyList<AbioticEditor.Core.WorldSaves.PetMutationOption>>(() =>
        {
            try { return _extractsIconsLive && _provider.Value is { } provider ? AbioticEditor.Core.WorldSaves.PetCareCatalog.MutationOptionsFor(provider, row) : []; }
            catch (Exception) { return []; }
        }));

    private readonly ConcurrentDictionary<string, Task<string?>> _characterNames = new(StringComparer.Ordinal);
    public Task<string?> GetCharacterNameAsync(string actorPath) => _characterNames.GetOrAdd(actorPath, path => Task.Run(() =>
    {
        try { return _extractsIconsLive ? _provider.Value?.TryGetNarrativeCharacterName(path) : null; }
        catch (Exception) { return null; }
    }));

    private readonly IReadOnlyDictionary<string, string>? _narrativeNpcNames;

    /// <summary>
    /// The real character name a placed story-NPC actor's own conversation row gives it (e.g.
    /// "Dr. Manse" for a <c>NarrativeNPC_Human_Hologram</c> slot), or null when this actor isn't
    /// in the bundled registry - see <see cref="AbioticEditor.Core.WorldSaves.NarrativeNpcNameCatalog"/>.
    /// Registry-only and synchronous: unlike <see cref="GetCharacterNameAsync"/> above, this never
    /// falls back to a mounted install - resolving it live would mean loading the actor's whole
    /// level package on demand, and there is no fast per-actor path for that (the registry itself
    /// is built by walking all 77 level packages at once, ~85s - see that catalog's own remarks).
    /// </summary>
    public string? GetNarrativeNpcName(string? actorId)
        => AbioticEditor.Core.WorldSaves.NarrativeNpcNameCatalog.Resolve(_narrativeNpcNames, actorId);

    private readonly IReadOnlyDictionary<string, string>? _bundledNpcDisplayNames;
    private IReadOnlyDictionary<string, string>? _liveNpcDisplayNames;
    private readonly ConcurrentDictionary<string, Task<string?>> _npcDisplayNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The friendly name <c>DT_NPCList</c> gives a spawned NPC's class (e.g. "Defense Robot" for
    /// <c>NPC_Robot_Defense_C</c>), or null when no row matches - see
    /// <see cref="AbioticEditor.Core.WorldSaves.NpcDisplayNameCatalog"/>. A mounted install is
    /// read first (freshest, picks up mods/DLC), falling back to the bundled registry so a
    /// browser build with no game resolves the same names.
    /// </summary>
    public Task<string?> GetNpcDisplayNameAsync(string? classOrShort)
    {
        if (string.IsNullOrWhiteSpace(classOrShort)) return Task.FromResult<string?>(null);
        return _npcDisplayNames.GetOrAdd(classOrShort!, key => Task.Run(() =>
        {
            if (_extractsIconsLive && _provider.Value is { HasMappings: true } provider)
            {
                try
                {
                    _liveNpcDisplayNames ??= AbioticEditor.Core.WorldSaves.NpcDisplayNameCatalog.LoadFrom(provider);
                    if (AbioticEditor.Core.WorldSaves.NpcDisplayNameCatalog.Resolve(_liveNpcDisplayNames, key) is { } liveName)
                        return liveName;
                }
                catch (Exception) { /* fall through to the bundled registry below */ }
            }
            return AbioticEditor.Core.WorldSaves.NpcDisplayNameCatalog.Resolve(_bundledNpcDisplayNames, key);
        }));
    }

    public IReadOnlyList<ItemCatalogEntry> Entries => _entries;
    public ItemCatalogEntry? Find(string? itemId) => itemId is not null && _byId.TryGetValue(itemId, out var entry) ? entry : null;
    /// <summary>
    /// Where to fetch an item's picture. The desktop host serves it from its own endpoint,
    /// decoding it out of the installed game the first time it is asked for. The browser has no
    /// endpoint and no game, so it points at the pre-dumped icon shipped with the app; a missing
    /// file simply renders as the usual blank, exactly as an undecodable icon already does.
    /// </summary>
    /// <remarks>
    /// The shipped file name is the item id lower-cased, and so is this URL. An id is spelled
    /// differently in different places - a save writes <c>Bandage</c> where the game's own data
    /// table calls the row <c>bandage</c> - and Windows, where the pictures were dumped and the
    /// desktop app reads them, does not care. A web server does: every item whose two spellings
    /// disagreed answered 404 and drew the "?" tile instead of its picture.
    /// </remarks>
    public string IconUrl(string itemId) => _extractsIconsLive
        ? $"/item-icons/{Uri.EscapeDataString(itemId)}"
        : $"icons/{Uri.EscapeDataString(itemId.ToLowerInvariant())}.png";

    public Task<string?> GetIconPathAsync(string itemId)
    {
        // Unknown URLs must not grow the cache, and concurrent requests for one real icon
        // must not start duplicate decodes in ConcurrentDictionary's value factory.
        if (Find(itemId) is not { IconAssetPath: { Length: > 0 } }) return Task.FromResult<string?>(null);
        return _icons.GetOrAdd(itemId, static (id, service) => new Lazy<Task<string?>>(
            () => service.ExtractIconAsync(id), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
    }

    private async Task<string?> ExtractIconAsync(string itemId)
    {
        if (Find(itemId) is not { IconAssetPath: { Length: > 0 } } entry) return null;
        await IconDecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var provider = _provider.Value;
                    if (provider is not { HasMappings: true }) return null;
                    var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
                    return raw is null ? null : IconColorizer.Colorize(raw, entry);
                }
                catch { return null; }
            }).ConfigureAwait(false);
        }
        finally
        {
            IconDecodeGate.Release();
        }
    }

    /// <summary>
    /// Which palette group an item belongs to, worked out once per item and then remembered.
    /// </summary>
    /// <remarks>
    /// The answer depends only on the item, and deciding it means dozens of case-insensitive
    /// substring searches over its id and tags. Whole-catalog sweeps call this sixteen hundred
    /// times in a row, so doing the work again every time was a measurable share of the delay
    /// when picking an inventory slot. Keyed by id: two entries sharing an id are the same item.
    /// </remarks>
    public static string CategoryOf(ItemCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Categories.GetOrAdd(entry.Id, static (_, item) => Classify(item), entry);
    }

    private static readonly ConcurrentDictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase);

    private static string Classify(ItemCatalogEntry entry)
    {
        bool Tag(string prefix) => entry.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        bool IdHas(params string[] hints) => hints.Any(hint => entry.Id.Contains(hint, StringComparison.OrdinalIgnoreCase));

        if (Tag("Item.Ammo") || Tag("Item.Weapon") || IdHas("weapon_", "ammo_", "grenade", "frag", "_gun", "magnum", "shotgun", "rifle", "crossbow", "launcher")) return "weapons";
        if (Tag("Item.Gear") || IdHas("armor", "helmet", "backpack_", "trinket", "suit_", "goggles", "headlamp", "watch_", "shield")) return "armor";
        if (IdHas("bandage", "medkit", "syringe", "splint", "pills", "firstaid", "antidote", "vaccine")) return "medical";
        if (Tag("Item.Food") || IdHas("food_", "soup_", "drink", "coffee", "tea_", "snack", "fish_")) return "food";
        if (IdHas("seed", "fertilizer", "gardenplot", "wateringcan", "scarecrow", "plant")) return "farming";
        if (IdHas("trap_", "_trap", "turret", "barricade", "tripwire", "mine_", "noisemaker", "spikes")) return "defense";
        if (IdHas("battery", "powercell", "brick_power", "lamp", "light_", "flashlight", "glowstick", "generator", "solar", "cable")) return "power";
        if (IdHas("bench", "furniture", "chair", "table", "bed_", "shelf", "crate", "couch", "locker", "freezer", "fridge", "stove", "oven", "sink_", "toilet")) return "furniture";
        if (IdHas("tool", "screwdriver", "wrench", "hammer", "drill", "vacuum", "fishingrod", "keypadhacker", "scanner", "extinguisher")) return "tools";
        if (IdHas("scrap_", "gib_", "essence", "crystal", "alloy", "ore_", "ingot", "plastic", "cloth", "tech_", "circuitboard", "harddrive", "casefan", "powersupply", "glue", "tape", "paper", "rubberband", "spring", "gear_", "coil", "wire", "lens", "diode", "carbon", "gem", "silver", "gold")) return "resources";
        return "other";
    }

    private static bool IsBrowsable(ItemCatalogEntry entry)
        => !string.IsNullOrWhiteSpace(entry.DisplayName)
           && entry.DisplayName != "?"
           && !entry.DisplayName.Contains("DEPRECATED", StringComparison.OrdinalIgnoreCase)
           && !entry.DisplayName.Contains("DONOTUSE", StringComparison.OrdinalIgnoreCase);

    private static GameAssetProvider? CreateProvider()
    {
        try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
        catch { return null; }
    }

    public void Dispose() { }
}
