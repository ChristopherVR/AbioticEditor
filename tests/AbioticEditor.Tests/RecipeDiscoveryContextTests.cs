using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class RecipeDiscoveryContextTests
{
    [Fact]
    public void Sessions_without_pickup_history_report_unknown_instead_of_an_empty_history()
    {
        var session = new NoHistorySession();
        Assert.Null(((IPlayerRecipesSession)session).RecipeItemsPickedUp);
        Assert.False(((IPlayerRecipesSession)session).IsRecipeUnlockStaged("recipe_example"));

        // The live aggregate must not infer historical pickups from its inventory view.
        var live = new LivePlayerEditorSession();
        Assert.Null(((IPlayerRecipesSession)live).RecipeItemsPickedUp);
    }

    [SkippableFact]
    public void File_history_follows_staged_discovery_and_revert_without_changing_saved_history()
    {
        var file = FileSession();
        var recipes = file;
        var original = file.ItemsPickedUp.ToHashSet(StringComparer.OrdinalIgnoreCase);
        const string stagedItem = "recipe_discovery_context_staged_item";

        Assert.NotNull(recipes.RecipeItemsPickedUp);
        Assert.False(file.IsDirty);
        Assert.True(original.SetEquals(recipes.RecipeItemsPickedUp));

        file.DiscoverAllItems([stagedItem]);
        Assert.Contains(stagedItem, recipes.RecipeItemsPickedUp);
        Assert.DoesNotContain(stagedItem, file.SavedData.ItemsPickedUp);
        Assert.True(file.IsDirty);

        file.Revert();
        Assert.True(original.SetEquals(recipes.RecipeItemsPickedUp));
        Assert.DoesNotContain(stagedItem, recipes.RecipeItemsPickedUp);
        Assert.False(file.IsDirty);
    }

    [SkippableFact]
    public void An_empty_file_history_is_known_and_does_not_become_unavailable()
    {
        var file = FileSession();
        var recipes = file;

        file.ItemsPickedUp.Clear();

        Assert.NotNull(recipes.RecipeItemsPickedUp);
        Assert.Empty(recipes.RecipeItemsPickedUp);
    }

    [SkippableFact]
    public async Task Recipe_staging_tracks_unlock_relock_and_revert_case_insensitively()
    {
        var file = FileSession();
        var recipes = file;
        const string addedRecipe = "recipe_discovery_staged_unlock";
        var learnedRecipe = recipes.Recipes.First(recipe => recipe.IsUnlocked).Id;

        Assert.False(recipes.IsRecipeUnlockStaged(addedRecipe));
        await recipes.SetUnlockedAsync(addedRecipe, true);
        Assert.True(recipes.IsRecipeUnlockStaged(addedRecipe.ToUpperInvariant()));
        Assert.False(recipes.IsRecipeUnlockStaged(learnedRecipe));

        await recipes.SetUnlockedAsync(learnedRecipe.ToUpperInvariant(), false);
        Assert.True(recipes.IsRecipeUnlockStaged(learnedRecipe));

        file.Revert();
        Assert.False(recipes.IsRecipeUnlockStaged(addedRecipe));
        Assert.False(recipes.IsRecipeUnlockStaged(learnedRecipe));
        Assert.False(file.IsDirty);
    }

    [SkippableFact]
    public async Task Saving_a_fixture_copy_advances_the_recipe_staging_baseline()
    {
        var source = FixturePlayer();
        var directory = Path.Combine(Path.GetTempPath(), "abiotic-recipe-discovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(source));
        const string addedRecipe = "recipe_discovery_saved_unlock";
        try
        {
            File.Copy(source, path);
            var file = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
            var recipes = file;

            await recipes.SetUnlockedAsync(addedRecipe, true);
            Assert.True(recipes.IsRecipeUnlockStaged(addedRecipe));
            await file.SaveAsync();

            Assert.False(recipes.IsRecipeUnlockStaged(addedRecipe.ToUpperInvariant()));
            Assert.False(file.IsDirty);
            Assert.Contains(addedRecipe, PlayerSaveReader.ReadFromFile(path).Recipes);
            Assert.True(File.Exists(path + ".bak"));

            await recipes.SetUnlockedAsync(addedRecipe, false);
            Assert.True(recipes.IsRecipeUnlockStaged(addedRecipe));
            file.Revert();
            Assert.False(recipes.IsRecipeUnlockStaged(addedRecipe));
            Assert.True(recipes.Recipes.Single(recipe => recipe.Id == addedRecipe).IsUnlocked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PlayerSaveSession FileSession()
    {
        var path = FixturePlayer();
        return new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
    }

    private static string FixturePlayer()
    {
        Skip.If(Fixtures.CascadeDir is null, "Player fixture is unavailable.");
        return Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
    }

    private sealed class NoHistorySession : IPlayerRecipesSession
    {
        public IReadOnlyList<PlayerRecipeEdit> Recipes => [];
        public int UnlockedRecipeCount => 0;
        public int RecipeCount => 0;
        public bool AppliesImmediately => true;
        public bool CanLock => false;
        public void EnsureRecipeRows(IEnumerable<string> ids) { }
        public Task SetUnlockedAsync(string recipeId, bool unlocked) => Task.CompletedTask;
        public void MarkChanged() { }
    }
}
