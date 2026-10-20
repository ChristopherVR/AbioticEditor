using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class PlayerProgressionSessionTests
{
    [Theory]
    [InlineData("initial")]
    [InlineData("lazy")]
    [InlineData("bulk")]
    public async Task Recipe_ids_with_different_casing_share_one_editable_row(string vocabularySource)
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var data = PlayerSaveReader.ReadFromFile(source);
        PlayerSaveWriter.ApplyRecipes(data, ["Recipe_238", "recipe_238", "recipe_legacy_unknown"]);
        using var stream = new MemoryStream();
        data.Raw.WriteTo(stream);
        stream.Position = 0;
        data = PlayerSaveReader.ReadFromStream(stream);

        var directory = Path.Combine(Path.GetTempPath(), "abiotic-recipe-casing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(source));
        try
        {
            string[] vocabulary = ["recipe_238", "RECIPE_238", "recipe_new", "RECIPE_NEW"];
            var session = new PlayerSaveSession(data, path,
                recipeVocabulary: vocabularySource == "initial" ? vocabulary : null);
            if (vocabularySource == "lazy") session.EnsureRecipeRows(vocabulary);
            if (vocabularySource == "bulk") session.UnlockAllRecipes(vocabulary);

            // This is the lookup the Recipes tab builds when it opens (issue #49).
            var rows = session.Recipes.ToDictionary(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(3, rows.Count);
            Assert.True(rows["recipe_238"].IsUnlocked);
            Assert.Equal("Recipe_238", rows["recipe_238"].Id);
            Assert.True(rows["recipe_legacy_unknown"].IsUnlocked);
            Assert.Equal(vocabularySource == "bulk", rows["recipe_new"].IsUnlocked);
            Assert.Equal(vocabularySource == "bulk", session.IsDirty);

            await session.SetUnlockedAsync("RECIPE_238", false);
            Assert.False(rows["recipe_238"].IsUnlocked);
            Assert.True(session.IsDirty);
            session.Revert();
            Assert.True(rows["recipe_238"].IsUnlocked);
            Assert.False(session.IsDirty);

            await session.SaveAsync();
            var reloaded = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal(["Recipe_238", "recipe_legacy_unknown"], reloaded.Recipes);

            await session.SetUnlockedAsync("RECIPE_238", false);
            await session.SetUnlockedAsync("RECIPE_NEW", true);
            await session.SaveAsync();
            reloaded = PlayerSaveReader.ReadFromFile(path);
            Assert.DoesNotContain(reloaded.Recipes, id => string.Equals(id, "recipe_238", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("recipe_new", reloaded.Recipes);
            Assert.Contains("recipe_legacy_unknown", reloaded.Recipes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Recipe_vocabulary_and_bulk_unlock_are_staged_and_revertible()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path,
            recipeVocabulary: ["recipe_web_progression_test"]);

        Assert.True(session.HasRecipeVocabulary);
        var added = Assert.Single(session.Recipes, recipe => recipe.Id == "recipe_web_progression_test");
        Assert.False(added.IsUnlocked);

        session.UnlockAllRecipes();
        Assert.True(added.IsUnlocked);
        Assert.True(session.IsDirty);

        session.Revert();
        Assert.False(added.IsUnlocked);
        Assert.False(session.IsDirty);
    }
}
