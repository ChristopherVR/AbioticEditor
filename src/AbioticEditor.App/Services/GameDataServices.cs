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

    /// <summary>The trader roster (DT_NPC_Traders + stock); empty without assets.</summary>
    public static IReadOnlyList<Core.Codex.TraderInfo> Traders => _traders ?? Array.Empty<Core.Codex.TraderInfo>();

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
            await Task.Run(() =>
            {
                try
                {
                    _provider = GameAssetProvider.CreateForLocalInstall();
                    if (_provider is not null && _provider.HasMappings)
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
                    }
                }
                catch
                {
                    // Editor must function (with reduced features) even if asset loading fails.
                }

                // Catalog loading churns through hundreds of MB of transient package
                // data; without a nudge the gen2/LOH garbage sits in the committed
                // heap for the whole session. One compacting collection here returns
                // it to the OS.
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            });
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
