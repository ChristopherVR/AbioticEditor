using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Provides the installed game's recipe vocabulary to UI sessions. Save rows remain usable
/// without a game installation; a live install simply makes locked recipes available to browse
/// and unlock. The result is cached because mounting paks and reading data tables is expensive.
/// </summary>
public sealed class RecipeVocabularyService
{
    private volatile IReadOnlyList<RecipeInfo>? _recipeInfos;
    private RecipeCatalogSnapshot? _workflowSnapshot;
    private readonly TimeProvider _clock;
    private readonly Func<(long Revision, string? PaksDirectory)> _inputs;
    private readonly Func<long, string?, DateTimeOffset, RecipeCatalogSnapshot> _loadSnapshot;
    private readonly Action _invalidateMounts;
    private DateTimeOffset _lastLoadAttempt;
    private static readonly TimeSpan FallbackRetryInterval = TimeSpan.FromMinutes(1);

    public RecipeVocabularyService(TimeProvider? clock = null)
        : this(clock ?? TimeProvider.System, GameDataGate.EnsureCurrentInputs, LoadWorkflow, GameDataGate.Invalidate) { }

    internal RecipeVocabularyService(TimeProvider clock,
        Func<(long Revision, string? PaksDirectory)> inputs,
        Func<long, string?, DateTimeOffset, RecipeCatalogSnapshot> loadSnapshot, Action invalidateMounts)
    {
        _clock = clock;
        _inputs = inputs;
        _loadSnapshot = loadSnapshot;
        _invalidateMounts = invalidateMounts;
    }

    /// <summary>
    /// Checks file/settings inputs and returns one coherent workflow snapshot. Call off the render
    /// path when opening a recipe task or explicitly editing an unlock; ordinary getters stay cheap.
    /// </summary>
    public RecipeCatalogSnapshot EnsureCurrent(ItemCatalogService items, CodexVocabularyService codex)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(codex);
        lock (GameDataGate.Sync)
        {
            var (revision, paks) = _inputs();
            var now = _clock.GetUtcNow();
            var retryFallback = _workflowSnapshot?.Metadata.HasFallback == true
                && now - _lastLoadAttempt >= FallbackRetryInterval;
            if (retryFallback || _workflowSnapshot?.Metadata.Source == RecipeCatalogSource.Unavailable)
            {
                _invalidateMounts();
                (revision, paks) = _inputs();
            }
            if (_workflowSnapshot is null || _workflowSnapshot.Metadata.Revision != revision
                || _workflowSnapshot.Metadata.Source == RecipeCatalogSource.Unavailable)
            {
                _lastLoadAttempt = now;
                _workflowSnapshot = _loadSnapshot(revision, paks, now);
            }
            else
                _workflowSnapshot = _workflowSnapshot with
                {
                    Metadata = _workflowSnapshot.Metadata with { CheckedAt = now },
                };
            _recipeInfos = _workflowSnapshot.Recipes;
            items.ApplyRecipeSnapshot(_workflowSnapshot);
            codex.ApplyRecipeSnapshot(_workflowSnapshot);
            return _workflowSnapshot;
        }
    }

    public RecipeCatalogMetadata? Metadata => _workflowSnapshot?.Metadata;

    public IReadOnlyList<string> GetRecipes() => GetRecipeInfos().Select(recipe => recipe.Id).ToList();
    public bool TryGetRecipes(out IReadOnlyList<string> recipes)
    {
        if (_recipeInfos is not { } loaded) { recipes = Array.Empty<string>(); return false; }
        recipes = loaded.Select(recipe => recipe.Id).ToList();
        return true;
    }

    /// <summary>
    /// Full recipe rows (crafted item, ingredients, benches), used by richer browsers such as
    /// the world-recipe editor. Loaded once under the shared pak gate; an empty result (a
    /// transient mount failure) is retried on the next request rather than cached forever.
    /// </summary>
    public IReadOnlyList<RecipeInfo> GetRecipeInfos()
    {
        if (_recipeInfos is { Count: > 0 } cached) return cached;
        lock (GameDataGate.Sync)
        {
            if (_recipeInfos is { Count: > 0 } raced) return raced;
            return _recipeInfos = LoadInfos();
        }
    }

    public bool TryGetRecipeInfos(out IReadOnlyList<RecipeInfo> infos)
    {
        if (_recipeInfos is not { } loaded) { infos = Array.Empty<RecipeInfo>(); return false; }
        infos = loaded;
        return true;
    }

    public void Reload()
    {
        lock (GameDataGate.Sync)
        {
            _invalidateMounts();
            _recipeInfos = null;
            _workflowSnapshot = null;
        }
    }

    private static RecipeCatalogSnapshot LoadWorkflow(long revision, string? paks, DateTimeOffset checkedAt)
    {
        var culture = GameDataLanguageStore.Saved ?? "en";
        try
        {
            var provider = GameDataGate.CreateProvider(culture);
            if (provider is { HasMappings: true })
            {
                var recipes = RecipeCatalog.LoadInfosFrom(provider);
                var itemCatalog = ItemCatalog.LoadFrom(provider);
                if (recipes.Count > 0 && itemCatalog.Count > 0)
                {
                    var vocabulary = CodexVocabularyService.LoadFromProvider(provider);
                    var variants = ItemVariantCatalog.LoadFrom(provider).Entries;
                    return new RecipeCatalogSnapshot(recipes, itemCatalog.Entries.ToArray(), vocabulary,
                        new RecipeCatalogMetadata(revision, RecipeCatalogSource.InstalledGame, checkedAt,
                            null, culture, false)) { ItemVariants = variants };
                }
            }
        }
        catch
        {
            // A partial installed load must not be mixed with old snapshot definitions.
        }

        var registry = GameDataRegistry.LoadBundled(out var sourcePath);
        var source = registry is null ? RecipeCatalogSource.Unavailable
            : GameDataRegistry.SuppliedRegistry is not null ? RecipeCatalogSource.SuppliedSnapshot
            : string.Equals(sourcePath, GameDataRegistry.UserRegistryPath, StringComparison.OrdinalIgnoreCase)
                ? RecipeCatalogSource.UserSnapshot : RecipeCatalogSource.BundledSnapshot;
        return new RecipeCatalogSnapshot(registry?.Recipes ?? [], registry?.Items ?? [],
            CodexVocabularyService.FromRegistry(registry),
            new RecipeCatalogMetadata(revision, source, checkedAt, registry?.GameVersion,
                registry?.Culture ?? culture, paks is not null)) { Registry = registry, ItemVariants = registry?.ItemVariants };
    }

    private static IReadOnlyList<RecipeInfo> LoadInfos()
    {
        try
        {
            var provider = GameDataGate.CreateProvider();
            if (provider is { HasMappings: true })
            {
                var live = RecipeCatalog.LoadInfosFrom(provider);
                if (live.Count > 0) return live;
            }
        }
        catch
        {
            // Fall through to the bundled dump; recipe editing also still supports rows
            // already present in a save even when neither source is available.
        }

        return GameDataRegistry.LoadBundled()?.Recipes ?? Array.Empty<RecipeInfo>();
    }
}
