using System.Text;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class RecipeLearningValidationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":1}")]
    [InlineData("{\"SchemaVersion\":1,\"Recipes\":null,\"StartingRecipeIds\":[],\"StartingResearchRecipeIds\":[],\"SkillGrants\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Recipes\":[null],\"StartingRecipeIds\":[],\"StartingResearchRecipeIds\":[],\"SkillGrants\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Recipes\":[],\"StartingRecipeIds\":[],\"StartingResearchRecipeIds\":[],\"SkillGrants\":[],\"SpecialGrants\":null}")]
    [InlineData("{\"SchemaVersion\":1,\"Recipes\":[{\"RecipeId\":\"recipe\",\"DiscoveryInputs\":null,\"LinkedRecipeIds\":[]}],\"StartingRecipeIds\":[],\"StartingResearchRecipeIds\":[],\"SkillGrants\":[]}")]
    public void Malformed_optional_learning_metadata_does_not_discard_other_registry_data_or_throw(string learning)
    {
        var registry = GameDataRegistry.TryRead(Encoding.UTF8.GetBytes(
            "{\"SchemaVersion\":" + GameDataRegistry.CurrentSchemaVersion + ",\"GameVersion\":\"kept\",\"Recipes\":[],\"RecipeLearning\":" + learning + "}"));
        Assert.NotNull(registry);
        Assert.Equal("kept", registry.GameVersion);
        Assert.Null(registry.RecipeLearning);
        Assert.Equal(RecipeDiscoveryState.Unknown, RecipeLearningAssessment.Assess(registry.RecipeLearning, "recipe").KnownRouteState);
    }

    [Fact]
    public void Direct_analysis_and_registry_updates_reject_null_nested_collections()
    {
        var definition = new RecipeLearningDefinition("recipe", "table", true, 0,
            [new("ingredient", "items", null, null!, true, true)], [], "output");
        var malformed = new RecipeLearningCatalogData(RecipeLearningCatalog.CurrentSchemaVersion, [definition], [], [], []);
        Assert.Null(RecipeLearningAssessment.Assess(malformed, "recipe", ["ingredient"]).Definition);
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge("{}", [], malformed));
    }
}
