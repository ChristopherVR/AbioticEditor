using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class BundledRecipeLearningTests
{
    [Fact]
    public void Every_shipped_recipe_has_at_least_one_cataloged_learning_route_in_each_language()
    {
        var directory = Path.Combine(UiSource.RepositoryRoot, "assets", "registry");
        var files = Directory.GetFiles(directory, "registry*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var registry = GameDataRegistry.TryRead(File.ReadAllBytes(file));
            Assert.NotNull(registry);
            var recipes = registry.Recipes!.Select(recipe => recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var learning = registry.RecipeLearning;
            Assert.NotNull(learning);
            Assert.Equal(RecipeLearningCatalog.CurrentSchemaVersion, learning.SchemaVersion);
            Assert.True(recipes.SetEquals(learning.Recipes.Select(recipe => recipe.RecipeId)));

            var knownRoutes = learning.Recipes.Where(recipe => recipe.PickupDiscoveryEnabled == true
                    && recipe.DiscoveryInputsKnown && recipe.DiscoveryInputs.All(input => input.IsResolved))
                .Select(recipe => recipe.RecipeId)
                .Concat(learning.StartingRecipeIds)
                .Concat(learning.SkillGrants.Select(grant => grant.RecipeId))
                .Concat(learning.Recipes.SelectMany(recipe => recipe.LinkedRecipeIds))
                .Concat(learning.SpecialGrants.Select(grant => grant.RecipeId))
                .Concat((registry.Emails ?? []).SelectMany(email => email.AttachmentRecipes))
                .Concat((registry.Fish ?? []).Where(fish => fish.UnlockRecipeId is not null).Select(fish => fish.UnlockRecipeId!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = recipes.Except(knownRoutes, StringComparer.OrdinalIgnoreCase).ToArray();

            // This guards source coverage, not an exhaustive prerequisite map or a promise that
            // any selected player currently has access, materials, passives or finished research.
            Assert.True(missing.Length == 0, $"{Path.GetFileName(file)} has recipes without a cataloged route: {string.Join(", ", missing)}");
        }
    }
}
