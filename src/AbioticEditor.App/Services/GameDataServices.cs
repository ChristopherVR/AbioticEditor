using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// Process-wide singleton wrapping the heavy game-data services (paks + usmap + catalog).
/// One initialization, used across view-models. All members are safe to read concurrently
/// after <see cref="EnsureLoadedAsync"/> completes.
/// </summary>
public static class GameDataServices
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static GameAssetProvider? _provider;
    private static ItemCatalog? _catalog;
    private static IReadOnlyList<SkillDefinition>? _skills;
    private static bool _loaded;

    public static GameAssetProvider? Provider => _provider;
    public static ItemCatalog? Catalog => _catalog;
    public static bool IsCatalogReady => _catalog is not null;

    /// <summary>
    /// The user's explicit game-install folder, or null when set to auto-detect. Backed by
    /// <see cref="Core.Assets.GamePathStore"/> so the CLI honors the same choice. Setting it
    /// only changes the persisted value; call <see cref="ReloadAsync"/> (the Settings &gt; Game
    /// Data card does this) to apply it live, or it takes effect on the next launch.
    /// </summary>
    public static string? CustomInstallPath
    {
        get => Core.Assets.GamePathStore.Saved;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                Core.Assets.GamePathStore.Clear();
            else
                Core.Assets.GamePathStore.Save(value);
        }
    }

    /// <summary>
    /// Whether the editor mounts Abiotic Factor mod paks (<c>~mods</c>/<c>LogicMods</c>). Backed
    /// by <see cref="Core.Assets.ModLoadStore"/> so the CLI honors the same choice. Setting it
    /// only persists the flag; call <see cref="ReloadAsync"/> to apply it live. The
    /// <c>ABIOTIC_NO_MODS</c> env var still forces this off (see <see cref="ModsDisabledByEnv"/>).
    /// </summary>
    public static bool ModsEnabled
    {
        get => Core.Assets.ModLoadStore.ModsEnabled;
        set => Core.Assets.ModLoadStore.SetPersistedEnabled(value);
    }

    /// <summary>True when the <c>ABIOTIC_NO_MODS</c> env var force-disables mods (toggle is locked off).</summary>
    public static bool ModsDisabledByEnv => Core.Assets.ModLoadStore.DisabledByEnv;

    /// <summary>
    /// Names of the mods currently mounted from the install's <c>~mods</c>/<c>LogicMods</c> folders
    /// (empty when none are present, mods are off, or each was individually disabled). Display-only.
    /// </summary>
    public static IReadOnlyList<string> LoadedMods => _provider?.LoadedMods ?? Array.Empty<string>();

    /// <summary>
    /// Every mod installed under the game's <c>~mods</c>/<c>LogicMods</c> folders, whether enabled or
    /// not - the list the Settings mod manager shows a per-mod toggle for. Read live from disk
    /// (resolving the configured/auto-detected install), so it reflects newly added mods on reload.
    /// </summary>
    public static IReadOnlyList<Core.Assets.AfInstallLocator.InstalledMod> InstalledMods
        => Core.Assets.AfInstallLocator.FindMods(Core.Assets.AfInstallLocator.FindPaksDirectory());

    /// <summary>Whether the named mod is individually enabled (independent of the master switch).</summary>
    public static bool IsModEnabled(string modName) => Core.Assets.ModLoadStore.IsModEnabled(modName);

    /// <summary>Turns one mod on/off (persisted); call <see cref="ReloadAsync"/> to apply it live.</summary>
    public static void SetModEnabled(string modName, bool enabled)
        => Core.Assets.ModLoadStore.SetModEnabled(modName, enabled);

    /// <summary>
    /// True only once the game paks mounted AND usmap mappings loaded - the state every
    /// asset-backed catalog (traders, items, recipes, ...) needs to be non-empty. Drives
    /// the editor's "game data not found" empty states. Note this is about the <em>live</em>
    /// install: it stays false when catalogs are served from the bundled registry (which has
    /// data but no icons) - see <see cref="IsUsingBundledRegistry"/>.
    /// </summary>
    public static bool IsGameDataLoaded => _provider is { } p && p.HasMappings;

    private static bool _usingRegistry;

    /// <summary>
    /// True when the game isn't usable live (not installed, or no mappings) but catalogs were
    /// populated from the bundled <see cref="Core.Assets.GameDataRegistry"/> instead. Item names
    /// and stats are available offline; icons are not (they still need the live install).
    /// </summary>
    public static bool IsUsingBundledRegistry => _usingRegistry;

    private static GameDataStatus _status = GameDataStatus.NotLoaded;

    /// <summary>
    /// Why game data is (or isn't) available after the last load. Lets the UI tell apart the
    /// two distinct failures - the game install wasn't found, versus it was found but
    /// Mappings.usmap is missing - instead of one vague "unavailable".
    /// </summary>
    public static GameDataStatus Status => _status;

    /// <summary>
    /// A human-readable explanation of <see cref="Status"/> that names the fix. Shown on the
    /// empty inventory / recipe / lore surfaces and in Settings &gt; Game Data.
    /// </summary>
    public static string StatusMessage => _status switch
    {
        GameDataStatus.Ready => "Game data loaded.",
        GameDataStatus.InstallNotFound =>
            (_usingRegistry
                ? "Abiotic Factor's data files weren't found, so the editor is using its bundled data "
                  + "(item names and stats work, but icons need the game). "
                : "Abiotic Factor's data files were not found, so the item, recipe and lore catalogs "
                  + "are empty. ")
            + "Open Settings > Game Data and choose LOCATE GAME FOLDER to point the "
            + "editor at your install (this works for non-Steam copies too).",
        GameDataStatus.MappingsMissing =>
            (_usingRegistry
                ? "The game was found but Mappings.usmap is missing, so the editor is using its bundled "
                  + "data (item names and stats work, but icons need the game). "
                : "The game was found, but Mappings.usmap is missing so its data tables can't be read. ")
            + "Keep Mappings.usmap next to the editor, or import one in Settings > Game Data.",
        GameDataStatus.LoadFailed =>
            "Game data failed to load. Turn on diagnostic logging in Settings and check the log "
            + "for the cause.",
        _ => "Game data has not been loaded yet.",
    };

    /// <summary>
    /// Ordered skill definitions - from the game's DT_Skills when assets are available,
    /// otherwise the built-in fallback table. Never null/empty.
    /// </summary>
    public static IReadOnlyList<SkillDefinition> SkillDefinitions => _skills ?? SkillCatalog.Fallback;

    private static IReadOnlyList<RecipeInfo>? _recipeInfos;
    private static IReadOnlyDictionary<string, TraitDetail>? _traitDetails;
    private static ItemUpgradeCatalog? _itemUpgrades;

    /// <summary>The DT_ItemUpgrades graph (weapons/armor/trinkets/backpacks/tools); Empty without assets.</summary>
    public static ItemUpgradeCatalog ItemUpgrades => _itemUpgrades ?? ItemUpgradeCatalog.Empty;

    private static BackpackSpecialSlotCatalog? _backpackSlots;

    /// <summary>
    /// Backpack special-slot table, read from the game's DT_ItemCosmetics data-asset
    /// chain so future packs are covered; the verified built-in table without assets.
    /// </summary>
    public static BackpackSpecialSlotCatalog BackpackSlots => _backpackSlots ?? BackpackSpecialSlotCatalog.Fallback;

    private static IReadOnlyList<Core.WorldSaves.SectorMapInfo>? _sectorMaps;

    /// <summary>The in-game sector maps (DT_MapPamphlets); empty without assets.</summary>
    public static IReadOnlyList<Core.WorldSaves.SectorMapInfo> SectorMaps
        => _sectorMaps ?? Array.Empty<Core.WorldSaves.SectorMapInfo>();

    private static IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>>? _customizationOptions;

    /// <summary>DT_Customization_* options per table name; empty without assets.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>> CustomizationOptions
        => _customizationOptions ?? new Dictionary<string, IReadOnlyList<CustomizationOption>>();

    /// <summary>
    /// Full recipe rows across the game's three recipe tables; empty when game assets
    /// aren't available (the recipe browser disables itself then).
    /// </summary>
    public static IReadOnlyList<RecipeInfo> AllRecipeInfos => _recipeInfos ?? Array.Empty<RecipeInfo>();

    /// <summary>Every recipe row name, derived from <see cref="AllRecipeInfos"/>.</summary>
    public static IReadOnlyList<string> AllRecipes => AllRecipeInfos.Select(r => r.Id).ToList();

    /// <summary>Trait details (descriptions, point costs) from CDT_AllTraits; empty without assets.</summary>
    public static IReadOnlyDictionary<string, TraitDetail> TraitDetails
        => _traitDetails ?? new Dictionary<string, TraitDetail>();

    private static IReadOnlyList<Core.Codex.EmailEntry>? _emails;
    private static IReadOnlyList<Core.Codex.JournalEntry>? _journals;
    private static IReadOnlyList<Core.Codex.CompendiumEntry>? _compendium;

    /// <summary>All 197 emails with full text, from DT_Emails; empty without assets.</summary>
    public static IReadOnlyList<Core.Codex.EmailEntry> Emails => _emails ?? Array.Empty<Core.Codex.EmailEntry>();

    /// <summary>All journal objectives from DT_JournalEntries; empty without assets.</summary>
    public static IReadOnlyList<Core.Codex.JournalEntry> Journals => _journals ?? Array.Empty<Core.Codex.JournalEntry>();

    /// <summary>All compendium lore entries from DT_Compendium; empty without assets.</summary>
    public static IReadOnlyList<Core.Codex.CompendiumEntry> Compendium => _compendium ?? Array.Empty<Core.Codex.CompendiumEntry>();

    private static IReadOnlyList<string>? _maps;

    /// <summary>All map pamphlet rows from DT_MapPamphlets; empty without assets.</summary>
    public static IReadOnlyList<string> AllMaps => _maps ?? Array.Empty<string>();

    private static IReadOnlyList<Core.Codex.FishDefinition>? _fish;

    /// <summary>All catchable fish from DT_Fish; empty without assets.</summary>
    public static IReadOnlyList<Core.Codex.FishDefinition> AllFish => _fish ?? Array.Empty<Core.Codex.FishDefinition>();

    private static IReadOnlyList<string>? _npcStates;

    /// <summary>E_NarrativeNPCStates value names; empty without assets.</summary>
    public static IReadOnlyList<string> NpcStates => _npcStates ?? Array.Empty<string>();

    private static IReadOnlyList<Core.Codex.TraderInfo>? _traders;

    /// <summary>
    /// The trader roster (DT_NPC_Traders + stock). Falls back to the built-in
    /// <see cref="Core.Codex.TraderCatalog.Fallback"/> snapshot when the game isn't installed,
    /// so traders and their unlock flags are always available (live data, with portraits,
    /// supersedes it when the game is present).
    /// </summary>
    public static IReadOnlyList<Core.Codex.TraderInfo> Traders => _traders ?? Core.Codex.TraderCatalog.Fallback;

    private static ILookup<string, RecipeInfo>? _craftedBy;
    private static ILookup<string, RecipeInfo>? _usedIn;
    private static ILookup<string, Core.Codex.TraderInfo>? _soldBy;

    /// <summary>Recipes that produce the given item.</summary>
    public static IEnumerable<RecipeInfo> RecipesCrafting(string itemId)
        => _craftedBy?[itemId] ?? Enumerable.Empty<RecipeInfo>();

    /// <summary>Recipes that consume the given item as an ingredient.</summary>
    public static IEnumerable<RecipeInfo> RecipesUsing(string itemId)
        => _usedIn?[itemId] ?? Enumerable.Empty<RecipeInfo>();

    /// <summary>Traders offering the given item.</summary>
    public static IEnumerable<Core.Codex.TraderInfo> TradersSelling(string itemId)
        => _soldBy?[itemId] ?? Enumerable.Empty<Core.Codex.TraderInfo>();

    public static async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;
            await Task.Run(LoadCore);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Re-runs game-data loading from scratch after the user changes the install folder or
    /// imports a usmap, so the inventory / recipe / lore surfaces fill in without an app
    /// restart. Disposes the previous provider and clears every cached catalog first, then
    /// reloads against the now-current <see cref="CustomInstallPath"/>. Callers must refresh
    /// any catalog-derived view state afterwards (e.g. the item palette and the open save).
    /// </summary>
    public static async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            ResetState();
            await Task.Run(LoadCore);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The actual load: resolve the install (honoring <see cref="CustomInstallPath"/>), mount
    /// the paks and, when mappings are present, populate every catalog. Records the outcome in
    /// <see cref="Status"/>. Runs on a background thread; never throws (a failure degrades to an
    /// empty catalog with <see cref="GameDataStatus.LoadFailed"/>).
    /// </summary>
    private static void LoadCore()
    {
        try
        {
            // Apply the user's explicit install folder (Settings > Game Data) before resolving,
            // so CreateForLocalInstall's AfInstallLocator picks it up ahead of Steam detection.
            AfInstallLocator.OverrideInstallRoot = CustomInstallPath;

            _provider = GameAssetProvider.CreateForLocalInstall();
            if (_provider is null)
            {
                _status = GameDataStatus.InstallNotFound;
                TryLoadRegistryFallback();
            }
            else if (!_provider.HasMappings)
            {
                _status = GameDataStatus.MappingsMissing;
                TryLoadRegistryFallback();
            }
            else
            {
                _catalog = ItemCatalog.LoadFrom(_provider);
                _skills = SkillCatalog.LoadFrom(_provider);
                _recipeInfos = RecipeCatalog.LoadInfosFrom(_provider);
                _traitDetails = TraitCatalog.LoadDetailsFrom(_provider);
                _emails = Core.Codex.CodexCatalog.LoadEmails(_provider);
                _journals = Core.Codex.CodexCatalog.LoadJournals(_provider);
                _compendium = Core.Codex.CodexCatalog.LoadCompendium(_provider);
                _maps = MapCatalog.LoadFrom(_provider);
                _fish = Core.Codex.CodexCatalog.LoadFish(_provider);
                _npcStates = Core.WorldSaves.NpcStateCatalog.LoadFrom(_provider);
                _traders = Core.Codex.TraderCatalog.LoadFrom(_provider);
                _itemUpgrades = ItemUpgradeCatalog.LoadFrom(_provider);
                _backpackSlots = BackpackSpecialSlotCatalog.LoadFrom(_provider);
                _sectorMaps = Core.WorldSaves.SectorMapCatalog.LoadFrom(_provider);
                _customizationOptions = CustomizationCatalog.LoadFrom(_provider);

                // Reverse indexes for the item encyclopedia (crafted-by /
                // used-in / sold-by lookups).
                _craftedBy = _recipeInfos!
                    .Where(r => r.CreatesItemId is not null)
                    .ToLookup(r => r.CreatesItemId!, StringComparer.OrdinalIgnoreCase);
                _usedIn = _recipeInfos!
                    .SelectMany(r => r.IngredientList.Select(i => (i.ItemId, Recipe: r)))
                    .ToLookup(t => t.ItemId, t => t.Recipe, StringComparer.OrdinalIgnoreCase);
                _soldBy = _traders!
                    .SelectMany(t => t.Sells.Select(o => (o.ItemId, Trader: t)))
                    .ToLookup(t => t.ItemId, t => t.Trader, StringComparer.OrdinalIgnoreCase);

                _status = GameDataStatus.Ready;
            }
        }
        catch
        {
            // Editor must function (with reduced features) even if asset loading fails.
            _status = GameDataStatus.LoadFailed;
        }

        // Catalog loading churns through hundreds of MB of transient package
        // data; without a nudge the gen2/LOH garbage sits in the committed
        // heap for the whole session. One compacting collection here returns
        // it to the OS.
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    /// <summary>
    /// Populates the catalogs the bundled <see cref="Core.Assets.GameDataRegistry"/> can provide
    /// when the live install is unavailable (no game, or no mappings). Best-effort: a missing or
    /// unreadable registry leaves the catalogs empty, exactly as before. Only the data tables come
    /// from here - icons still need the live install, so <see cref="IsGameDataLoaded"/> stays false.
    /// As more catalogs are added to the registry, populate them here too.
    /// </summary>
    private static void TryLoadRegistryFallback()
    {
        var registry = Core.Assets.GameDataRegistry.LoadBundled();
        if (registry is null) return;

        if (registry.Items is { Count: > 0 } items)
        {
            _catalog = ItemCatalog.FromRegistry(items, registry.ItemTableRefs ?? new Dictionary<string, string>());
            _usingRegistry = true;
        }
    }

    /// <summary>Disposes the mounted provider and clears every cached catalog before a reload.</summary>
    private static void ResetState()
    {
        _provider?.Dispose();
        _provider = null;
        _catalog = null;
        _skills = null;
        _recipeInfos = null;
        _traitDetails = null;
        _emails = null;
        _journals = null;
        _compendium = null;
        _maps = null;
        _fish = null;
        _npcStates = null;
        _traders = null;
        _itemUpgrades = null;
        _backpackSlots = null;
        _sectorMaps = null;
        _customizationOptions = null;
        _craftedBy = null;
        _usedIn = null;
        _soldBy = null;
        _usingRegistry = false;
        _loaded = false;
        _status = GameDataStatus.NotLoaded;
    }
}

/// <summary>The outcome of the last game-data load - see <see cref="GameDataServices.Status"/>.</summary>
public enum GameDataStatus
{
    /// <summary>Loading hasn't run yet.</summary>
    NotLoaded,

    /// <summary>Paks mounted and mappings loaded; catalogs are populated.</summary>
    Ready,

    /// <summary>No Abiotic Factor install could be located (auto-detect failed and no folder set).</summary>
    InstallNotFound,

    /// <summary>The game was found, but no Mappings.usmap is available to read its data tables.</summary>
    MappingsMissing,

    /// <summary>Loading threw - the catalogs are empty; see the diagnostic log.</summary>
    LoadFailed,
}
