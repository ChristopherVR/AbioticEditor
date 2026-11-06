using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Issue #53: items the player can craft count as found when checking for missed recipes, and
/// recipes inferred that way can make further inputs craftable.
/// </summary>
public sealed class RecipePickupInferenceTests(ITestOutputHelper output)
{
    private static readonly RecipeInfo[] Recipes =
    [
        Recipe("recipe_wire", "wire", "ore"),
        Recipe("recipe_coil", "coil", "wire", "tape"),
        Recipe("recipe_motor", "motor", "coil", "scrap"),
        Recipe("recipe_glue", "glue", "resin"),
    ];

    [Fact]
    public void A_known_recipe_output_satisfies_another_recipes_input_without_a_pickup()
    {
        var inference = Infer(["tape"], ["RECIPE_WIRE"]);

        Assert.True(inference.IsMet("recipe_coil"));
        var crafted = Assert.Single(inference.CraftedInputsFor("RECIPE_COIL"));
        Assert.Equal("wire", crafted.ItemId);
        Assert.Equal("recipe_wire", crafted.RecipeId);
        Assert.False(crafted.Inferred);
    }

    [Fact]
    public void Without_crafting_knowledge_only_actual_pickups_count()
    {
        var inference = Infer(["tape"], []);
        Assert.False(inference.IsMet("recipe_coil"));
        Assert.Empty(inference.CraftableItems);
    }

    [Fact]
    public void Newly_suggested_recipes_chain_until_nothing_else_changes()
    {
        // Ore unlocks wire (a suggestion), wire plus tape unlocks coil, coil plus scrap unlocks motor.
        var inference = Infer(["ore", "tape", "scrap"], []);

        Assert.True(inference.IsMet("recipe_wire"));
        Assert.Empty(inference.CraftedInputsFor("recipe_wire"));
        Assert.True(inference.IsMet("recipe_coil"));
        Assert.True(Assert.Single(inference.CraftedInputsFor("recipe_coil")).Inferred);
        var motor = Assert.Single(inference.CraftedInputsFor("recipe_motor"));
        Assert.Equal(("coil", "recipe_coil", true), (motor.ItemId, motor.RecipeId, motor.Inferred));
        Assert.False(inference.IsMet("recipe_glue"));
    }

    [Fact]
    public void Picked_up_inputs_are_not_explained_as_crafted_and_unavailable_history_stays_empty()
    {
        var inference = Infer(["wire", "tape"], ["recipe_wire"]);
        Assert.True(inference.IsMet("recipe_coil"));
        Assert.Empty(inference.CraftedInputsFor("recipe_coil"));
        Assert.DoesNotContain("wire", inference.CraftableItems.Keys);

        Assert.Same(RecipePickupInference.Empty,
            RecipePickupInference.Infer(Rules(), Recipes, null, ["recipe_wire"]));
    }

    [Fact]
    public void A_substitute_group_is_met_by_any_craftable_member()
    {
        var rules = new[] { new RecipePickupRule("recipe_lamp", [["bulb_a", "bulb_b"], ["wire"]]) };
        var recipes = new[] { Recipe("recipe_bulb_b", "bulb_b", "glass"), Recipe("recipe_lamp", "lamp", "bulb_a", "wire") };
        var inference = RecipePickupInference.Infer(rules, recipes, ["wire"], ["recipe_bulb_b"]);

        Assert.True(inference.IsMet("recipe_lamp"));
        Assert.Equal("bulb_b", Assert.Single(inference.CraftedInputsFor("recipe_lamp")).ItemId);
    }

    [Fact]
    public void Graph_rules_skip_unresolved_groups_and_unknown_items()
    {
        var graph = new RecipeGraph([Recipe("recipe_wire", "wire", "ore"), Recipe("recipe_kit", "kit", "AnyGlue", "wire")],
            ["wire", "ore", "kit", "AnyGlue"]);
        Assert.Equal("recipe_wire", Assert.Single(graph.PickupRules()).RecipeId);
    }

    [Fact]
    public void Real_player_saves_only_gain_suggestions_and_every_crafted_input_is_explained()
    {
        var registryPath = Path.Combine(UiSource.RepositoryRoot, "assets", "registry", "registry.en.json");
        var registry = GameDataRegistry.TryRead(File.ReadAllBytes(registryPath));
        Assert.NotNull(registry);
        var recipes = registry.Recipes!;
        var rules = RecipePickupInference.RulesFrom(registry.RecipeLearning);
        Assert.NotEmpty(rules);
        var ruleInputs = rules.ToDictionary(rule => rule.RecipeId, rule => rule.Inputs, StringComparer.OrdinalIgnoreCase);
        var outputs = recipes.Where(recipe => recipe.CreatesItemId is not null)
            .GroupBy(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().CreatesItemId!, StringComparer.OrdinalIgnoreCase);

        var players = AllFixtureSaves.PlayerSaves().ToList();
        if (players.Count == 0) return; // fixtures absent
        var widenedTotal = 0;
        foreach (var (path, player) in players)
        {
            var pending = player.RecipesRequiringResearch.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var craftable = player.Recipes.Where(id => !pending.Contains(id)).ToArray();
            var pickedUp = player.ItemsPickedUp.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baseline = RecipePickupInference.Infer(rules, recipes, player.ItemsPickedUp, []);
            var widened = RecipePickupInference.Infer(rules, recipes, player.ItemsPickedUp, craftable);

            // Every pickup-only match is kept, and anything beyond it is explained by a craftable input.
            foreach (var rule in rules)
            {
                var pickupOnly = RecipeLearningAssessment.Assess(registry.RecipeLearning, rule.RecipeId, player.ItemsPickedUp)
                    .PickupIngredientsState == RecipeDiscoveryState.KnownMet;
                if (pickupOnly) Assert.True(baseline.IsMet(rule.RecipeId) && baseline.CraftedInputsFor(rule.RecipeId).Count == 0);
                else if (baseline.IsMet(rule.RecipeId)) Assert.NotEmpty(baseline.CraftedInputsFor(rule.RecipeId));
                if (widened.IsMet(rule.RecipeId) && !pickupOnly) Assert.NotEmpty(widened.CraftedInputsFor(rule.RecipeId));
            }

            Assert.Superset(new HashSet<string>(baseline.MetRecipeIds, StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(widened.MetRecipeIds, StringComparer.OrdinalIgnoreCase));
            widenedTotal += widened.MetRecipeIds.Count - baseline.MetRecipeIds.Count;
            output.WriteLine($"{Path.GetFileName(path)}: {baseline.MetRecipeIds.Count} matched from pickups and suggestions, {widened.MetRecipeIds.Count} once known recipes count");

            foreach (var recipeId in widened.MetRecipeIds)
            {
                foreach (var alternatives in ruleInputs[recipeId])
                    Assert.True(alternatives.Any(item => pickedUp.Contains(item) || widened.CraftableItems.ContainsKey(item)),
                        $"{Path.GetFileName(path)}: {recipeId} has an input that is neither picked up nor craftable");
                foreach (var crafted in widened.CraftedInputsFor(recipeId))
                {
                    Assert.DoesNotContain(crafted.ItemId, pickedUp);
                    Assert.Equal(crafted.ItemId, outputs[crafted.RecipeId], StringComparer.OrdinalIgnoreCase);
                    if (crafted.Inferred) Assert.True(widened.IsMet(crafted.RecipeId) && !craftable.Contains(crafted.RecipeId, StringComparer.OrdinalIgnoreCase));
                    else Assert.Contains(crafted.RecipeId, craftable, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        // Real players have learned recipes for items they never picked up, so this must widen something.
        Assert.True(widenedTotal > 0, "Crafting knowledge did not add a single suggestion across the fixture players.");
    }

    private static RecipePickupInference Infer(IEnumerable<string> pickups, IEnumerable<string> known)
        => RecipePickupInference.Infer(Rules(), Recipes, pickups, known);

    private static RecipePickupRule[] Rules()
        => Recipes.Select(recipe => new RecipePickupRule(recipe.Id,
            recipe.IngredientList.Select(input => (IReadOnlyList<string>)[input.ItemId]).ToArray())).ToArray();

    private static RecipeInfo Recipe(string id, string output, params string[] inputs)
        => new(id, output, 1, null, "Crafting", inputs.Select(input => new RecipeIngredient(input, 1)).ToArray());
}
