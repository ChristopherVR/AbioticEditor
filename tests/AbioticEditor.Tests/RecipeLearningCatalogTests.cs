using AbioticEditor.Core.Items;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Tests;

public sealed class RecipeLearningCatalogTests
{
    [Fact]
    public void Extractor_keeps_missing_or_unrecognized_schema_unknown()
    {
        var row = new FStructFallback([Tag("NotUnlockableByPickup_hash", new IntProperty(0))]);
        var definition = Read(row);

        Assert.Null(definition.PickupDiscoveryEnabled);
        Assert.Null(definition.RequiresResearch);
        Assert.False(definition.DiscoveryInputsKnown);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition, []).PickupIngredientsState);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Pickup_flag_is_read_only_for_the_components_configured_table(bool blocked, bool allowed)
    {
        var row = new FStructFallback([Tag("NotUnlockableByPickup_hash", new BoolProperty(blocked))]);
        Assert.Equal(allowed, Read(row).PickupDiscoveryEnabled);
        Assert.Null(RecipeLearningCatalog.ReadDefinition("recipe", row, "other_table", false,
            new Dictionary<string, RecipeLearningInput>()).PickupDiscoveryEnabled);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, null)]
    public void Research_requires_recognized_difficulty_instead_of_defaulting_unknown_fields(int difficulty, bool? required)
    {
        var research = new FStructFallback([Tag("MinigameDifficulty_hash",
            new NameProperty(new FName($"E_MinigameDifficulty::NewEnumerator{difficulty}")))]);
        var row = new FStructFallback([Tag("ResearchData_hash", new StructProperty(new FScriptStruct(research)))]);
        var definition = Read(row);

        Assert.Equal(difficulty, definition.ResearchDifficulty);
        Assert.Equal(required, definition.RequiresResearch);
    }

    [Fact]
    public void Explicit_empty_discovery_inputs_are_distinguished_from_an_absent_array()
    {
        var row = new FStructFallback([
            Tag("NotUnlockableByPickup_hash", new BoolProperty(false)),
            Tag("RecipeItems_hash", new ArrayProperty(new UScriptArray("StructProperty"))),
        ]);
        var definition = Read(row);

        Assert.True(definition.DiscoveryInputsKnown);
        Assert.Equal(RecipeDiscoveryState.KnownMet, Assess(definition, []).PickupIngredientsState);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition, null).PickupIngredientsState);
    }

    [Fact]
    public void Discovery_uses_historical_types_and_one_alternative_per_group_case_insensitively()
    {
        var definition = Definition(inputs: [Input("metal"), new("BigOfficeCouches", "substitutes", "Large couches",
            ["couch_a", "couch_b"], true, true)]);
        var assessment = Assess(definition, ["METAL", "COUCH_B"]);

        Assert.Equal(RecipeDiscoveryState.KnownMet, assessment.PickupIngredientsState);
        Assert.All(assessment.Inputs, input => Assert.Equal(RecipeDiscoveryState.KnownMet, input.State));
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assess(definition, ["metal"]).PickupIngredientsState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assess(definition, ["metal", "BigOfficeCouches"]).PickupIngredientsState);
    }

    [Fact]
    public void Unknown_history_and_unresolved_groups_never_become_proven_pickup_routes()
    {
        var definition = Definition(inputs: [Input("metal"), new("AnyFutureGroup", "substitutes", null, [], true, false)]);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition, ["metal"]).PickupIngredientsState);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition, null).PickupIngredientsState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assess(definition, []).PickupIngredientsState);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition with { PickupDiscoveryEnabled = false }, ["metal"]).PickupIngredientsState);
    }

    [Fact]
    public void A_crafted_output_is_not_assumed_to_grant_its_recipe_without_separate_rule_evidence()
    {
        var definition = Definition(inputs: [Input("metal")]);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(definition, ["output"]).OutputPickupState);
        var enabled = definition with { OutputPickupDiscoveryEnabled = true };
        Assert.Equal(RecipeDiscoveryState.KnownMet, Assess(enabled, ["OUTPUT"]).OutputPickupState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assess(enabled, []).OutputPickupState);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assess(enabled, null).OutputPickupState);
    }

    [Fact]
    public void Discovered_but_queued_is_distinct_from_researched_and_unknown_live_research_state()
    {
        var catalog = Catalog(Definition() with { ResearchDifficulty = 1 });
        var queued = RecipeLearningAssessment.Assess(catalog, "recipe", discoveredRecipeIds: ["RECIPE"], researchPendingIds: ["recipe"]);
        Assert.True(queued.IsDiscovered);
        Assert.True(queued.IsResearchPending);
        Assert.False(queued.RecipeKnowledgeAvailable);

        var completed = RecipeLearningAssessment.Assess(catalog, "recipe", discoveredRecipeIds: ["recipe"], researchPendingIds: []);
        Assert.True(completed.RecipeKnowledgeAvailable);
        var unavailableHistory = RecipeLearningAssessment.Assess(catalog, "recipe", discoveredRecipeIds: ["recipe"]);
        Assert.Null(unavailableHistory.IsResearchPending);
        Assert.Null(unavailableHistory.RecipeKnowledgeAvailable);
    }

    [Fact]
    public void Starting_defaults_remain_independent_of_the_current_players_discovery_state()
    {
        var catalog = Catalog(Definition()) with
        {
            StartingRecipeIds = ["RECIPE"], StartingResearchRecipeIds = ["recipe"],
            StartingRecipesKnown = true, StartingResearchKnown = true,
        };
        var assessment = RecipeLearningAssessment.Assess(catalog, "recipe", discoveredRecipeIds: [], researchPendingIds: []);
        Assert.Equal(RecipeDiscoveryState.KnownMet, assessment.StartingState);
        Assert.False(assessment.IsDiscovered);
        Assert.False(assessment.IsResearchPending);
        Assert.False(assessment.RecipeKnowledgeAvailable);
        Assert.Equal(RecipeDiscoveryState.Unknown,
            RecipeLearningAssessment.Assess(catalog with { StartingRecipesKnown = false }, "recipe").StartingState);
    }

    [Fact]
    public void Linked_and_skill_grants_are_alternatives_and_do_not_require_ingredient_discovery()
    {
        var parent = Definition() with { RecipeId = "parent", LinkedRecipeIds = ["recipe"] };
        var catalog = Catalog(Definition() with { PickupDiscoveryEnabled = false }, parent) with
        {
            SkillGrants = [new("RECIPE", "Accuracy", 7)],
        };
        var assessment = RecipeLearningAssessment.Assess(catalog, "recipe", [], ["PARENT"], [],
            new Dictionary<string, int> { ["accuracy"] = 6 });
        Assert.Equal(RecipeDiscoveryState.KnownMet, Assert.Single(assessment.LinkedSources).State);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assert.Single(assessment.SkillSources).State);
        Assert.Equal(RecipeDiscoveryState.KnownMet, assessment.KnownRouteState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, Assert.Single(RecipeLearningAssessment.Assess(catalog, "recipe",
            skillLevels: new Dictionary<string, int> { ["Accuracy"] = 7 }).SkillSources).State);
        Assert.Equal(RecipeDiscoveryState.Unknown, Assert.Single(RecipeLearningAssessment.Assess(catalog, "recipe").SkillSources).State);
    }

    [Fact]
    public void Missing_or_future_snapshot_schema_keeps_requirements_unknown_without_losing_current_history()
    {
        var future = Catalog(Definition()) with { SchemaVersion = RecipeLearningCatalog.CurrentSchemaVersion + 1 };
        foreach (var catalog in new RecipeLearningCatalogData?[] { null, future })
        {
            var assessment = RecipeLearningAssessment.Assess(catalog, "recipe", ["metal"], ["recipe"], []);
            Assert.Null(assessment.Definition);
            Assert.Equal(RecipeDiscoveryState.Unknown, assessment.PickupIngredientsState);
            Assert.Equal(RecipeDiscoveryState.Unknown, assessment.KnownRouteState);
            Assert.True(assessment.IsDiscovered);
            Assert.True(assessment.RecipeKnowledgeAvailable);
        }
    }

    [Fact]
    public void Linked_grants_require_a_loaded_recipe_table_and_an_actual_target_definition()
    {
        var source = Definition() with { RecipeId = "source" };
        var target = Definition() with { RecipeId = "target" };
        var other = Definition() with { RecipeId = "other", TablePath = "soup_table" };
        var validated = RecipeLearningCatalog.ValidateLinks([source, target, other],
        [
            new("source", "TARGET", "CONFIGURED_TABLE"),
            new("SOURCE", "target", "configured_table"),
            new("source", "target", "item_table"),
            new("source", "missing", "configured_table"),
            new("source", "other", "configured_table"),
            new("source", "target", null),
        ], out var rejected);

        Assert.Equal("TARGET", Assert.Single(validated[0].LinkedRecipeIds));
        Assert.Empty(validated[1].LinkedRecipeIds);
        Assert.Equal(4, rejected.Count);
        Assert.Contains(rejected, link => link.Reason == "target-not-defined");
        Assert.Contains(rejected, link => link.Reason == "target-table-mismatch");
        Assert.Equal(2, rejected.Count(link => link.Reason == "target-table-not-loaded"));
    }

    [Fact]
    public void Cyclic_linked_data_is_preserved_as_direct_alternatives_without_recursive_expansion()
    {
        var first = Definition() with { RecipeId = "first" };
        var second = Definition() with { RecipeId = "second" };
        var recipes = RecipeLearningCatalog.ValidateLinks([first, second],
            [new("first", "second", "configured_table"), new("second", "first", "configured_table")], out var rejected);
        var catalog = Catalog(recipes.ToArray());

        Assert.Empty(rejected);
        var assessment = RecipeLearningAssessment.Assess(catalog, "second", discoveredRecipeIds: ["first"]);
        Assert.Equal("first", Assert.Single(assessment.LinkedSources).SourceRecipeId);
        Assert.Equal(RecipeDiscoveryState.KnownMet, assessment.LinkedSources[0].State);
    }

    private static RecipeLearningDefinition Read(FStructFallback row)
        => RecipeLearningCatalog.ReadDefinition("recipe", row, "configured_table", true, new Dictionary<string, RecipeLearningInput>());
    private static FPropertyTag Tag(string name, FPropertyTagType value) => new() { Name = new FName(name), Tag = value };
    private static RecipeLearningInput Input(string id) => new(id, "items", null, [id], false, true);
    private static RecipeLearningDefinition Definition(IReadOnlyList<RecipeLearningInput>? inputs = null)
        => new("recipe", "configured_table", true, 0, inputs ?? [Input("metal")], [], "output") { DiscoveryInputsKnown = true };
    private static RecipeLearningCatalogData Catalog(params RecipeLearningDefinition[] definitions)
        => new(RecipeLearningCatalog.CurrentSchemaVersion, definitions, [], [], []);
    private static RecipeLearningAssessment Assess(RecipeLearningDefinition definition, IEnumerable<string>? pickups)
        => RecipeLearningAssessment.Assess(Catalog(definition), "recipe", pickups);
}
