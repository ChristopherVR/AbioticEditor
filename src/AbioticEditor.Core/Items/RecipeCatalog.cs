using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Items;

/// <summary>
/// One recipe row: which item it crafts and where it comes from. The crafted item id is
/// a row name into <c>ItemTable_Global</c>, so display names/icons resolve through
/// <see cref="ItemCatalog"/>.
/// </summary>
/// <param name="Id">Recipe row name, e.g. <c>recipe_bandage</c>.</param>
/// <param name="CreatesItemId">Row name of the crafted item (null if unreadable).</param>
/// <param name="Count">How many items one craft produces.</param>
/// <param name="Category">Recipe category enum value name, e.g. <c>Tools</c>.</param>
/// <param name="Source">Which table the row came from: Crafting, Soup, Chemistry, or
/// Misc for rows from dynamically discovered <c>DT_*Recipes</c> tables.</param>
/// <param name="Ingredients">What the craft consumes.</param>
/// <param name="Benches">Item rows of required benches, e.g. <c>Deployable_Bench_Crafting</c>.</param>
public sealed record RecipeInfo(
    string Id,
    string? CreatesItemId,
    int Count,
    string? Category,
    string Source,
    IReadOnlyList<RecipeIngredient>? Ingredients = null,
    IReadOnlyList<string>? Benches = null)
{
    public IReadOnlyList<RecipeIngredient> IngredientList => Ingredients ?? Array.Empty<RecipeIngredient>();
    public IReadOnlyList<string> BenchList => Benches ?? Array.Empty<string>();
}

/// <summary>One recipe input: an item row and how many of it the craft consumes.</summary>
public sealed record RecipeIngredient(string ItemId, int Count);

/// <summary>
/// The complete recipe vocabulary, read from the game's three recipe DataTables.
/// Player saves (<c>RecipesUnlock_</c>) and the world metadata save
/// (<c>GlobalRecipesUnlocked_</c>/<c>GlobalRecipesResearched_</c>) mix rows from all
/// three: crafting (<c>recipe_*</c>), soups (<c>srecipe_*</c>) and chemistry
/// (<c>frecipe_*</c>/<c>trecipe_*</c>/<c>crecipe_*</c>).
/// </summary>
public static class RecipeCatalog
{
    private static readonly (string Path, string Source)[] Tables =
    {
        ("AbioticFactor/Content/Blueprints/DataTables/DT_Recipes", "Crafting"),
        ("AbioticFactor/Content/Blueprints/DataTables/DT_SoupRecipes", "Soup"),
        ("AbioticFactor/Content/Blueprints/DataTables/DT_ChemistryRecipes", "Chemistry"),
    };

    /// <summary>Source label given to rows from tables found by <see cref="DiscoverExtraTables"/>.</summary>
    public const string DiscoveredSource = "Misc";

    /// <summary>
    /// True when <paramref name="assetPath"/> (a mounted pak file path like
    /// <c>AbioticFactor/Content/Blueprints/DataTables/DT_FooRecipes.uasset</c>) is a
    /// recipe DataTable we don't already load by name. Future-proofing: a game patch
    /// adding e.g. <c>DT_BakingRecipes</c> gets picked up without an editor update.
    /// </summary>
    public static bool IsDiscoveredRecipeTable(string assetPath)
    {
        if (!assetPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return false;
        if (!assetPath.Contains("Blueprints/DataTables/", StringComparison.OrdinalIgnoreCase)) return false;

        var name = Path.GetFileNameWithoutExtension(assetPath);
        if (!name.StartsWith("DT_", StringComparison.OrdinalIgnoreCase)) return false;
        if (!name.EndsWith("Recipes", StringComparison.OrdinalIgnoreCase)) return false;

        // Already covered by the known table list?
        foreach (var (path, _) in Tables)
        {
            if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Scans the mounted asset list for <c>Blueprints/DataTables/DT_*Recipes</c> tables
    /// that are not in the known list, returning their package paths (no extension).
    /// With current game data this is empty - it exists so rows from tables added by
    /// future patches still reach the recipe vocabulary.
    /// </summary>
    public static IReadOnlyList<string> DiscoverExtraTables(GameAssetProvider provider)
    {
        var result = new List<string>();
        try
        {
            foreach (var assetPath in provider.AssetPaths)
            {
                if (IsDiscoveredRecipeTable(assetPath))
                {
                    result.Add(assetPath[..^".uasset".Length]);
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("RecipeCatalog", "Recipe table discovery scan failed.", ex);
        }
        return result;
    }

    /// <summary>Every recipe row name across the three tables, in table order.</summary>
    public static IReadOnlyList<string> LoadFrom(GameAssetProvider provider)
        => LoadInfosFrom(provider).Select(r => r.Id).ToList();

    /// <summary>
    /// Full recipe rows across the three tables (in table order), or an empty list when
    /// the game assets aren't available.
    /// </summary>
    public static IReadOnlyList<RecipeInfo> LoadInfosFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Array.Empty<RecipeInfo>();

        var result = new List<RecipeInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, source) in Tables)
        {
            LoadTableInto(provider, path, source, result, seen);
        }

        // Future-proofing: pull in any DT_*Recipes tables we don't know by name yet.
        var discovered = DiscoverExtraTables(provider);
        Diagnostics.EditorLog.Info(
            "RecipeCatalog",
            $"Loaded {result.Count} recipe rows from {Tables.Length} known tables; discovered {discovered.Count} extra recipe table(s).");
        foreach (var path in discovered)
        {
            LoadTableInto(provider, path, DiscoveredSource, result, seen);
        }

        return result;
    }

    private static void LoadTableInto(
        GameAssetProvider provider, string path, string source, List<RecipeInfo> result, HashSet<string> seen)
    {
        try
        {
            var pkg = provider.LoadPackageInternal(path);
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                foreach (var kv in dt.RowMap)
                {
                    var id = kv.Key.Text;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                    result.Add(BuildInfo(id, kv.Value, source));
                }
            }
        }
        catch (Exception ex)
        {
            // A missing/unreadable table just shrinks the vocabulary.
            Diagnostics.EditorLog.Warn("RecipeCatalog", $"Failed to load recipe table '{path}'.", ex);
        }
    }

    private static RecipeInfo BuildInfo(string id, FStructFallback row, string source)
    {
        string? createsItem = null;
        var count = 1;
        string? category = null;
        var ingredients = new List<RecipeIngredient>();
        var benches = new List<string>();

        foreach (var p in row.Properties)
        {
            var name = p.Name.Text;
            if (name.StartsWith("ItemToCreate_", StringComparison.Ordinal))
            {
                createsItem = RowNameOf(p.Tag?.GenericValue);
            }
            else if (name.StartsWith("CountToCreate_", StringComparison.Ordinal))
            {
                count = p.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
            }
            else if (name.StartsWith("Category_", StringComparison.Ordinal))
            {
                // Enum renders as "E_RecipeCategory::NewEnumeratorX" or a friendly name
                // depending on mappings; keep the tail segment.
                var s = p.Tag?.GenericValue?.ToString();
                if (!string.IsNullOrEmpty(s))
                {
                    var idx = s.LastIndexOf(':');
                    category = idx >= 0 ? s[(idx + 1)..] : s;
                }
            }
            else if (name.StartsWith("RecipeItems_", StringComparison.Ordinal))
            {
                // Array of { Item_ = row handle, Count_ = int }.
                foreach (var entry in StructArray(p.Tag?.GenericValue))
                {
                    string? itemId = null;
                    var n = 1;
                    foreach (var ep in entry.Properties)
                    {
                        if (ep.Name.Text.StartsWith("Item_", StringComparison.Ordinal))
                        {
                            itemId = RowNameOf(ep.Tag?.GenericValue);
                        }
                        else if (ep.Name.Text.StartsWith("Count_", StringComparison.Ordinal))
                        {
                            n = ep.Tag?.GenericValue switch { int i => i, byte b => b, _ => 1 };
                        }
                    }
                    if (!string.IsNullOrEmpty(itemId)) ingredients.Add(new RecipeIngredient(itemId!, n));
                }
            }
            else if (name.StartsWith("BenchesRequired_", StringComparison.Ordinal))
            {
                if (p.Tag?.GenericValue is UScriptArray arr)
                {
                    foreach (var bp in arr.Properties)
                    {
                        var bench = RowNameOf(bp.GenericValue);
                        if (!string.IsNullOrEmpty(bench)) benches.Add(bench!);
                    }
                }
            }
        }
        return new RecipeInfo(id, createsItem, count, category, source, ingredients, benches);
    }

    private static IEnumerable<FStructFallback> StructArray(object? value)
    {
        if (value is not UScriptArray arr) yield break;
        foreach (var p in arr.Properties)
        {
            var v = p.GenericValue;
            if (v is FScriptStruct ss) v = ss.StructType;
            if (v is FStructFallback sf) yield return sf;
        }
    }

    private static string? RowNameOf(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        return sf.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
    }
}
