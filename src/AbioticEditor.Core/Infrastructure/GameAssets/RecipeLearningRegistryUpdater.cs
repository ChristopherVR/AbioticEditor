using System.Text.Json;
using System.Text.Json.Nodes;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Adds current learning evidence to a compatible existing registry without re-dumping its
/// localized catalogs or losing unknown JSON fields. Validation is pure and precedes any write.
/// </summary>
public static class RecipeLearningRegistryUpdater
{
    public static string Merge(string existingJson, IReadOnlyList<RecipeInfo> currentRecipes,
        RecipeLearningCatalogData learning, string? expectedCulture = null)
    {
        ArgumentNullException.ThrowIfNull(currentRecipes);
        ArgumentNullException.ThrowIfNull(learning);
        if (currentRecipes.Count == 0 || RecipeLearningCatalog.Validated(learning) is null)
            throw new InvalidDataException("Current recipe definitions and a supported learning catalog are required.");
        JsonObject root;
        try
        {
            root = JsonNode.Parse(existingJson) as JsonObject
                ?? throw new InvalidDataException("The existing registry must be a JSON object.");
            if (root["SchemaVersion"]?.GetValue<int>() != GameDataRegistry.CurrentSchemaVersion)
                throw new InvalidDataException("The existing registry has an unsupported schema.");
            var culture = root["Culture"]?.GetValue<string>();
            if (!Same(Culture(culture), Culture(expectedCulture)))
                throw new InvalidDataException($"The existing registry culture '{culture ?? "default"}' does not match '{expectedCulture ?? "default"}'.");
            if (root["Recipes"] is not JsonArray recipes)
                throw new InvalidDataException("The existing registry has no recipe definitions to validate.");
            var existing = new Dictionary<string, RecipeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in recipes)
            {
                var recipe = node?.Deserialize(GameDataRegistryJsonContext.Default.RecipeInfo);
                if (recipe is null || string.IsNullOrWhiteSpace(recipe.Id) || !existing.TryAdd(recipe.Id, recipe))
                    throw new InvalidDataException("The existing registry has missing or duplicate recipe identities.");
            }
            var current = new Dictionary<string, RecipeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var recipe in currentRecipes)
                if (string.IsNullOrWhiteSpace(recipe.Id) || !current.TryAdd(recipe.Id, recipe))
                    throw new InvalidDataException("The current recipe catalog has missing or duplicate identities.");
            if (existing.Count != current.Count)
                throw new InvalidDataException("Recipe definitions differ from the installed game. Perform a complete registry dump instead.");
            foreach (var (id, recipe) in current)
                if (!existing.TryGetValue(id, out var previous) || !SameRecipe(previous, recipe))
                    throw new InvalidDataException($"Recipe '{id}' differs from the installed game. Perform a complete registry dump instead.");
            var learningIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in learning.Recipes)
                if (!learningIds.Add(definition.RecipeId) || !current.TryGetValue(definition.RecipeId, out var recipe)
                    || !Same(definition.OutputItemId, recipe.CreatesItemId))
                    throw new InvalidDataException("The learning catalog does not match the current recipe identities.");
            if (learningIds.Count != current.Count)
                throw new InvalidDataException("The learning catalog is incomplete for the current recipe definitions.");

            root["RecipeLearning"] = JsonSerializer.SerializeToNode(learning,
                GameDataRegistryJsonContext.Default.RecipeLearningCatalogData);
            return root.ToJsonString(GameDataRegistryJsonContext.Default.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The existing registry contains invalid JSON or recipe data.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException("The existing registry contains invalid field types.", ex);
        }
    }

    private static bool SameRecipe(RecipeInfo left, RecipeInfo right)
        => Same(left.CreatesItemId, right.CreatesItemId) && left.Count == right.Count
            && Same(left.Category, right.Category) && Same(left.Source, right.Source)
            && left.CraftDurationSeconds == right.CraftDurationSeconds
            && left.BenchList.SequenceEqual(right.BenchList, StringComparer.OrdinalIgnoreCase)
            && left.IngredientList.Count == right.IngredientList.Count
            && left.IngredientList.Zip(right.IngredientList)
                .All(pair => Same(pair.First.ItemId, pair.Second.ItemId) && pair.First.Count == pair.Second.Count);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string? Culture(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
