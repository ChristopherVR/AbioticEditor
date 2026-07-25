using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class PlayerProgressionSessionTests
{
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
