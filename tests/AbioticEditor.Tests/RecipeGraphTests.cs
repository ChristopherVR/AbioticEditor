using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public class RecipeGraphTests
{
    [Fact]
    public void Nested_yields_duplicate_inputs_and_shared_materials_keep_correct_branch_amounts()
    {
        var target = Recipe("target", "gadget", 1, [Input("wire", 3), Input("WIRE", 2), Input("ore", 1)], ["bench"]);
        var wire = Recipe("wire_recipe", "wire", 2, [Input("ore", 3)]);
        var graph = new RecipeGraph([target, wire], ["gadget", "wire", "ore", "bench"]);

        var root = graph.BuildTree("TARGET");

        Assert.Equal(2, root.Children.Count);
        var wireNode = root.Children[0];
        Assert.Equal("wire", wireNode.ItemId);
        Assert.Equal(5, wireNode.RequiredCount);
        Assert.Equal(3, wireNode.CraftCount);
        Assert.Equal(6, wireNode.ProducedCount);
        Assert.Equal(9, Assert.Single(wireNode.Children).RequiredCount);
        Assert.Equal(RecipeTreeStop.Material, wireNode.Children[0].Stop);
        Assert.Equal(1, root.Children[1].RequiredCount);
        Assert.DoesNotContain(root.Children, node => node.ItemId == "bench");
        Assert.Equal("bench", Assert.Single(root.Recipe!.BenchList));
        Assert.Equal("root.0.0", wireNode.Children[0].Path);
        Assert.Equal(RecipeTreeStop.None, root.Stop);
    }

    [Fact]
    public void Alternate_routes_are_choices_and_root_keeps_the_requested_recipe()
    {
        var first = Recipe("first", "flask", 1, [Input("a", 1)]);
        var second = Recipe("second", "FLASK", 1, [Input("b", 3)]);
        var target = Recipe("target", "target", 1, [Input("flask", 2)]);
        var graph = new RecipeGraph([first, second, target], ["flask", "a", "b", "target"]);

        Assert.Equal(2, graph.RecipesProducing("fLaSk").Count);
        Assert.Equal("second", graph.BuildTree("second").Recipe!.Id);
        var rootChoice = graph.BuildTree("first", routeChoices: new Dictionary<string, string> { ["root"] = "SECOND" });
        Assert.Equal("b", Assert.Single(rootChoice.Children).ItemId);

        var nested = graph.BuildTree("target", routeChoices: new Dictionary<string, string> { ["root.0"] = "second" });
        Assert.Equal("second", nested.Children[0].Recipe!.Id);
        Assert.Equal(6, Assert.Single(nested.Children[0].Children).RequiredCount);
        Assert.Equal("a", graph.BuildTree("target", routeChoices:
            new Dictionary<string, string> { ["root.0"] = "target" }).Children[0].Children[0].ItemId);
    }

    [Fact]
    public void Mixed_case_recipe_aliases_do_not_duplicate_routes()
    {
        var original = Recipe("Recipe_Wire", "Wire", 1, [Input("ore", 1)]);
        var alias = Recipe("recipe_wire", "wire", 1, [Input("other", 1)]);
        var graph = new RecipeGraph([original, alias], ["wire", "ore", "other"]);

        Assert.Same(original, Assert.Single(graph.RecipesProducing("WIRE")));
        Assert.Same(original, graph.BuildTree("RECIPE_WIRE").Recipe);
        Assert.Empty(graph.FindPossibleMissed(["recipe_WIRE"], ["ORE"]));
    }

    [Fact]
    public void Missed_recipe_clues_require_concrete_complete_pickup_history_and_update_after_unlocks()
    {
        var target = Recipe("target", "gadget", 1, [Input("wire", 2), Input("ore", 1)]);
        var unresolved = Recipe("unknown", "gadget", 1, [Input("missing", 1)]);
        var group = Recipe("group", "gadget", 1, [Input("AnyGlue", 1)]);
        var empty = Recipe("empty", "gadget", 1, []);
        var bad = Recipe("invalid", "gadget", 1, [Input("ore", 0)]);
        var noOutput = Recipe("unknown_output", "missing", 1, [Input("ore", 1)]);
        var graph = new RecipeGraph([target, unresolved, group, empty, bad, noOutput], ["gadget", "wire", "ore", "AnyGlue"]);

        Assert.Empty(graph.FindPossibleMissed([], null));
        Assert.Empty(graph.FindPossibleMissed([], ["ore"]));
        Assert.Equal("target", Assert.Single(graph.FindPossibleMissed([], ["WIRE", "ORE", "missing", "AnyGlue"])).Id);
        Assert.Empty(graph.FindPossibleMissed(["TARGET"], ["wire", "ore"]));
        Assert.Equal("target", Assert.Single(graph.FindPossibleMissed([], ["wire", "ore"])).Id);
    }

    [Fact]
    public void Unknown_materials_and_requirement_groups_are_not_claimed_as_craftable()
    {
        var graph = new RecipeGraph([Recipe("target", "gadget", 1,
            [Input("ore", 1), Input("unknown", 1), Input("AnyGlue", 1)])], ["gadget", "ore", "AnyGlue"]);

        var root = graph.BuildTree("target");

        Assert.Equal(RecipeTreeStop.Material, root.Children[0].Stop);
        Assert.Equal(RecipeTreeStop.UnknownIngredient, root.Children[1].Stop);
        Assert.Equal(RecipeTreeStop.UnknownIngredient, root.Children[2].Stop);
        Assert.All(root.Children, node => Assert.Null(node.Recipe));
    }

    [Fact]
    public void Cycles_are_bounded_per_path_without_hiding_shared_sibling_recipes()
    {
        var a = Recipe("a", "a", 1, [Input("b", 1)]);
        var b = Recipe("b", "b", 1, [Input("A", 1)]);
        var rootRecipe = Recipe("root", "root", 1, [Input("a", 1), Input("b", 1)]);
        var graph = new RecipeGraph([a, b, rootRecipe], ["a", "b", "root"]);

        var root = graph.BuildTree("root");

        Assert.Equal(2, root.Children.Count);
        Assert.Equal(RecipeTreeStop.Cycle, root.Children[0].Children[0].Children[0].Stop);
        Assert.Equal(RecipeTreeStop.Cycle, root.Children[1].Children[0].Children[0].Stop);
        Assert.Equal(RecipeTreeStop.None, root.Children[1].Stop);
    }

    [Fact]
    public void Node_budget_caps_total_allocations_and_signals_omitted_siblings()
    {
        var graph = new RecipeGraph([Recipe("root", "root", 1,
            Enumerable.Range(0, 100).Select(index => Input($"material{index}", 1)).ToArray())],
            Enumerable.Range(0, 100).Select(index => $"material{index}").Prepend("root"));

        var root = graph.BuildTree("root", maxNodes: 4);

        Assert.Equal(4, Flatten(root).Count());
        Assert.Equal(RecipeTreeStop.NodeLimit, root.Stop);
        Assert.Equal(3, root.Children.Count);
        var oneNode = graph.BuildTree("root", maxNodes: 1);
        Assert.Equal(RecipeTreeStop.NodeLimit, oneNode.Stop);
        Assert.Empty(oneNode.Children);
    }

    [Fact]
    public void Depth_budget_is_explicit_and_does_not_invent_material_leaves()
    {
        var graph = new RecipeGraph([
            Recipe("a", "a", 1, [Input("b", 1)]),
            Recipe("b", "b", 1, [Input("c", 1)]),
            Recipe("c", "c", 1, [Input("ore", 1)])], ["a", "b", "c", "ore"]);

        var root = graph.BuildTree("a", maxDepth: 1);

        Assert.Equal(RecipeTreeStop.DepthLimit, Assert.Single(root.Children).Stop);
        Assert.NotNull(root.Children[0].Recipe);
        Assert.Empty(root.Children[0].Children);
    }

    [Fact]
    public void Invalid_recipes_and_overflow_never_return_wrapped_or_clamped_quantities()
    {
        var recipes = new[]
        {
            Recipe("zero_yield", "a", 0, [Input("ore", 1)]),
            Recipe("negative_input", "b", 1, [Input("ore", -1)]),
            Recipe("sum_overflow", "c", 1, [Input("ore", int.MaxValue), Input("ORE", 1)]),
            Recipe("multiply_overflow", "d", 1, [Input("ore", 2)]),
            Recipe("yield_overflow", "e", 2, [Input("ore", 1)]),
        };
        var graph = new RecipeGraph(recipes, ["a", "b", "c", "d", "e", "ore"]);

        foreach (var id in new[] { "zero_yield", "negative_input", "sum_overflow" })
            Assert.Equal(RecipeTreeStop.InvalidRecipe, graph.BuildTree(id).Stop);
        Assert.Equal(RecipeTreeStop.InvalidRecipe, graph.BuildTree("multiply_overflow", long.MaxValue).Stop);
        Assert.Equal(RecipeTreeStop.InvalidRecipe, graph.BuildTree("yield_overflow", long.MaxValue).Stop);
        Assert.Equal(RecipeTreeStop.InvalidRecipe, graph.BuildTree("multiply_overflow", 0).Stop);
        Assert.Equal(0, graph.BuildTree("multiply_overflow", -1).RequiredCount);
        Assert.DoesNotContain(graph.FindPossibleMissed([], ["ore"]),
            recipe => recipe.Id is "zero_yield" or "negative_input" or "sum_overflow");
    }

    [Fact]
    public void Unknown_saved_recipe_ids_remain_an_explicit_unresolved_root()
    {
        var graph = new RecipeGraph([], []);

        var root = graph.BuildTree("Recipe_Legacy");

        Assert.Equal("Recipe_Legacy", root.ItemId);
        Assert.Equal(RecipeTreeStop.InvalidRecipe, root.Stop);
        Assert.Null(root.Recipe);
        Assert.Empty(root.Children);
    }

    private static RecipeInfo Recipe(string id, string output, int count,
        IReadOnlyList<RecipeIngredient> inputs, IReadOnlyList<string>? benches = null)
        => new(id, output, count, null, "Crafting", inputs, benches);

    private static RecipeIngredient Input(string item, int count) => new(item, count);

    private static IEnumerable<RecipeTreeNode> Flatten(RecipeTreeNode node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
