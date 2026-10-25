using System.Text.Json;
using System.Text.Json.Nodes;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class RecipeLearningRegistryUpdaterTests
{
    [Fact]
    public void Merge_replaces_only_learning_metadata_and_preserves_localized_and_unknown_fields()
    {
        var existing = Registry();
        existing["RecipeLearning"] = new JsonObject { ["SchemaVersion"] = -1, ["OldField"] = true };
        existing["Recipes"]![0]!["FutureRecipeField"] = new JsonObject { ["Keep"] = "localized recipe detail" };
        var before = existing.ToJsonString();
        var result = JsonNode.Parse(Merge(before))!;

        Assert.Equal(RecipeLearningCatalog.CurrentSchemaVersion, result["RecipeLearning"]!["SchemaVersion"]!.GetValue<int>());
        Assert.Equal("recipe", result["RecipeLearning"]!["Recipes"]![0]!["RecipeId"]!.GetValue<string>());
        Assert.Null(result["RecipeLearning"]!["OldField"]);
        result.AsObject().Remove("RecipeLearning");
        var originalWithoutLearning = JsonNode.Parse(before)!.AsObject();
        originalWithoutLearning.Remove("RecipeLearning");
        Assert.True(JsonNode.DeepEquals(originalWithoutLearning, result));
        Assert.Equal(before, existing.ToJsonString());
    }

    [Fact]
    public void Matching_case_aliases_are_compatible_and_the_original_spelling_is_preserved()
    {
        var existing = Registry();
        existing["Recipes"]![0]!["Id"] = "RECIPE";
        existing["Recipes"]![0]!["CreatesItemId"] = "OUTPUT";
        existing["Recipes"]![0]!["Ingredients"]![0]!["ItemId"] = "MATERIAL";
        var merged = JsonNode.Parse(Merge(existing.ToJsonString()))!;

        Assert.Equal("RECIPE", merged["Recipes"]![0]!["Id"]!.GetValue<string>());
        Assert.Equal("OUTPUT", merged["Recipes"]![0]!["CreatesItemId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("CreatesItemId")]
    [InlineData("Count")]
    [InlineData("Category")]
    [InlineData("Source")]
    [InlineData("Ingredients")]
    [InlineData("IngredientCount")]
    [InlineData("Benches")]
    [InlineData("CraftDurationSeconds")]
    public void Changed_crafting_definitions_are_rejected_before_producing_an_updated_payload(string field)
    {
        var existing = Registry();
        var recipe = existing["Recipes"]![0]!;
        switch (field)
        {
            case "CreatesItemId": recipe[field] = "other_output"; break;
            case "Count": recipe[field] = 7; break;
            case "Category": recipe[field] = "other_category"; break;
            case "Source": recipe[field] = "Soup"; break;
            case "Ingredients": recipe[field]![0]!["ItemId"] = "other_material"; break;
            case "IngredientCount": recipe["Ingredients"]![0]!["Count"] = 9; break;
            case "Benches": recipe[field]![0] = "other_bench"; break;
            case "CraftDurationSeconds": recipe[field] = 20.0; break;
        }
        var original = existing.ToJsonString();

        Assert.Throws<InvalidDataException>(() => Merge(original));
        Assert.Equal(original, existing.ToJsonString());
        Assert.Null(existing["RecipeLearning"]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    public void Missing_extra_or_duplicate_recipe_rows_refuse_the_overlay(string mismatch)
    {
        var existing = Registry();
        var recipes = existing["Recipes"]!.AsArray();
        if (mismatch == "missing") recipes.Clear();
        else
        {
            var extra = recipes[0]!.DeepClone();
            extra["Id"] = mismatch == "duplicate" ? "RECIPE" : "other_recipe";
            recipes.Add(extra);
        }
        Assert.Throws<InvalidDataException>(() => Merge(existing.ToJsonString()));
    }

    [Fact]
    public void Wrong_culture_or_unsupported_registry_schema_does_not_receive_an_overlay()
    {
        var existing = Registry();
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge(existing.ToJsonString(),
            [Recipe()], Learning(), "de"));
        existing["SchemaVersion"] = GameDataRegistry.CurrentSchemaVersion + 1;
        Assert.Throws<InvalidDataException>(() => Merge(existing.ToJsonString()));
    }

    [Fact]
    public void Default_culture_metadata_and_unknown_catalogs_are_preserved()
    {
        var existing = Registry();
        existing.Remove("Culture");
        var merged = JsonNode.Parse(RecipeLearningRegistryUpdater.Merge(existing.ToJsonString(), [Recipe()], Learning()))!;

        Assert.Null(merged["Culture"]);
        Assert.Equal("keep this build stamp", merged["GameVersion"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(existing["FutureCatalog"], merged["FutureCatalog"]));
    }

    [Fact]
    public void Incomplete_future_or_unrelated_learning_payloads_are_rejected()
    {
        var existing = Registry().ToJsonString();
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge(existing, [Recipe()],
            Learning() with { SchemaVersion = RecipeLearningCatalog.CurrentSchemaVersion + 1 }, "fr"));
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge(existing, [Recipe()],
            Learning() with { Recipes = [] }, "fr"));
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge(existing, [Recipe()],
            Learning() with { Recipes = [Learning().Recipes[0] with { RecipeId = "unrelated" }] }, "fr"));
        Assert.Throws<InvalidDataException>(() => RecipeLearningRegistryUpdater.Merge(existing, [Recipe()],
            Learning() with { Recipes = [Learning().Recipes[0] with { OutputItemId = "unrelated" }] }, "fr"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"SchemaVersion\":\"wrong type\"}")]
    public void Invalid_json_or_field_types_are_reported_as_invalid_registry_data(string json)
        => Assert.Throws<InvalidDataException>(() => Merge(json));

    private static string Merge(string json) => RecipeLearningRegistryUpdater.Merge(json, [Recipe()], Learning(), "fr");
    private static RecipeInfo Recipe() => new("recipe", "output", 2, "category", "Crafting", [new("material", 3)], ["bench"], 1.5);
    private static RecipeLearningCatalogData Learning() => new(RecipeLearningCatalog.CurrentSchemaVersion,
        [new("recipe", "recipe_table", true, 1, [new("material", "item_table", null, ["material"], false, true)], [], "output")
            { DiscoveryInputsKnown = true }], [], [], []);
    private static JsonObject Registry() => new()
    {
        ["SchemaVersion"] = GameDataRegistry.CurrentSchemaVersion,
        ["Culture"] = "fr",
        ["GameVersion"] = "keep this build stamp",
        ["Items"] = new JsonArray(new JsonObject { ["Id"] = "material", ["DisplayName"] = "Matière localisée", ["Future"] = 17 }),
        ["Recipes"] = JsonSerializer.SerializeToNode(new[] { Recipe() }),
        ["FutureCatalog"] = new JsonObject { ["Nested"] = new JsonArray(1, "unknown", true), ["KeepNull"] = null },
    };
}
