using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Provides the installed game's recipe vocabulary to UI sessions. Save rows remain usable
/// without a game installation; a live install simply makes locked recipes available to browse
/// and unlock. The result is cached because mounting paks and reading data tables is expensive.
/// </summary>
public sealed class RecipeVocabularyService
{
    private volatile IReadOnlyList<RecipeInfo>? _recipeInfos;

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

    public void Reload() => _recipeInfos = null;

    private static IReadOnlyList<RecipeInfo> LoadInfos()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
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
