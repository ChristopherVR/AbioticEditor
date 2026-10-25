using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class RecipeLearningTreeTests
{
    [Fact]
    public void Selected_recipe_has_alternative_discovery_methods_and_all_inputs_with_any_group_members()
    {
        var group = new RecipeLearningInput("BigOfficeCouches", "substitutes", "Large couch", ["couch_a", "couch_b"], true, true);
        var definition = Definition("target") with { DiscoveryInputs = [Input("ore"), group] };
        var unrelated = Definition("unrelated");
        var catalog = Catalog(definition, unrelated) with
        {
            StartingRecipesKnown = true, StartingRecipeIds = ["target"],
            SkillGrants = [new("target", "Accuracy", 7)],
        };
        var assessed = new List<string>();
        var tree = RecipeLearningTree.Build("target", catalog, id =>
        {
            assessed.Add(id);
            return RecipeLearningAssessment.Assess(catalog, id, ["ore", "couch_b"], [], [],
                new Dictionary<string, int> { ["Accuracy"] = 6 });
        }, EmptyDiscovery, pickupState: item => item == "couch_a" ? RecipeDiscoveryState.KnownUnmet : RecipeDiscoveryState.KnownMet);

        Assert.Equal("target", Assert.Single(assessed));
        Assert.True(tree.IsRoot);
        Assert.Equal(RecipeLearningNodeKind.Recipe, tree.Kind);
        var discovery = Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Discovery);
        Assert.Equal(3, discovery.Children.Count);
        Assert.Contains(discovery.Children, node => node.Kind == RecipeLearningNodeKind.Starting);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assert.Single(discovery.Children, node => node.Kind == RecipeLearningNodeKind.Skill).State);
        var all = Assert.Single(discovery.Children, node => node.Kind == RecipeLearningNodeKind.PickupAll);
        Assert.Equal(2, all.Children.Count);
        Assert.Equal("ore", all.Children[0].ItemId);
        var any = all.Children[1];
        Assert.Equal(RecipeLearningNodeKind.PickupAny, any.Kind);
        Assert.Equal(RecipeDiscoveryState.KnownMet, any.Input!.State);
        Assert.Collection(any.Children, node => Assert.Equal("couch_a", node.ItemId), node => Assert.Equal("couch_b", node.ItemId));
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, any.Children[0].State);
        Assert.Equal(RecipeDiscoveryState.KnownMet, any.Children[1].State);
        Assert.Equal(RecipeDiscoveryState.KnownMet, Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Research).State);
    }

    [Fact]
    public void Linked_discovery_recurses_case_insensitively_and_never_requires_the_predecessors_research()
    {
        var first = Definition("first", false) with { LinkedRecipeIds = ["SECOND"], ResearchDifficulty = 1 };
        var second = Definition("second", false) with { LinkedRecipeIds = ["FIRST"], ResearchDifficulty = 1 };
        var catalog = Catalog(first, second);
        var tree = RecipeLearningTree.Build("first", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id, [], ["second"], ["second"]), EmptyDiscovery);
        var nodes = Nodes(tree).ToArray();

        Assert.Single(nodes, node => node.Kind == RecipeLearningNodeKind.Research);
        var predecessor = Assert.Single(nodes, node => node.Kind == RecipeLearningNodeKind.Recipe && !node.IsRoot);
        Assert.Equal("second", predecessor.RecipeId);
        Assert.True(predecessor.Assessment!.IsResearchPending);
        Assert.Equal(RecipeDiscoveryState.KnownMet, predecessor.State);
        Assert.Equal(RecipeLearningNodeKind.Discovery, Assert.Single(predecessor.Children).Kind);
        var cycle = Assert.Single(nodes, node => node.Kind == RecipeLearningNodeKind.Cycle);
        Assert.Equal("first", cycle.RecipeId, ignoreCase: true);
        Assert.Empty(cycle.Children);
    }

    [Fact]
    public void A_concealed_recipe_is_a_leaf_before_assessment_or_source_callbacks_are_called()
    {
        var tree = RecipeLearningTree.Build("secret", null,
            _ => throw new InvalidOperationException("Concealed assessment must not run."),
            _ => throw new InvalidOperationException("Concealed source lookup must not run."),
            recipeConcealed: _ => true);

        Assert.True(tree.Concealed);
        Assert.True(tree.IsRoot);
        Assert.Null(tree.Assessment);
        Assert.Null(tree.ItemId);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void A_concealed_linked_recipe_does_not_evaluate_or_expose_its_descendants()
    {
        var catalog = Catalog(Definition("target", false), Definition("secret", false) with { LinkedRecipeIds = ["target"] });
        var assessed = new List<string>();
        var lookedUp = new List<string>();
        var tree = RecipeLearningTree.Build("target", catalog, id =>
        {
            assessed.Add(id);
            return RecipeLearningAssessment.Assess(catalog, id);
        }, id => { lookedUp.Add(id); return EmptyDiscovery(id); }, recipeConcealed: id => id == "secret");
        var sealedNode = Assert.Single(Nodes(tree), node => node.Concealed);

        Assert.Equal("secret", sealedNode.RecipeId);
        Assert.Null(sealedNode.Assessment);
        Assert.Empty(sealedNode.Children);
        Assert.Equal("target", Assert.Single(assessed));
        Assert.Equal("target", Assert.Single(lookedUp));
    }

    [Fact]
    public void Explicit_email_and_fish_sources_survive_legacy_metadata_and_do_not_claim_world_access_means_completion()
    {
        var email = Source(RecipeDiscoverySourceKind.Email, "email", RecipeDiscoveryState.KnownUnmet);
        var fish = Source(RecipeDiscoverySourceKind.Fish, "fish", RecipeDiscoveryState.KnownMet);
        var tree = RecipeLearningTree.Build("target", null,
            id => RecipeLearningAssessment.Assess(null, id), id => new(id, [email, fish]));
        var discovery = Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Discovery);

        Assert.Equal(2, discovery.Children.Count);
        Assert.Equal(RecipeLearningNodeKind.Email, discovery.Children[0].Kind);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, discovery.Children[0].State);
        Assert.Equal(RecipeDiscoveryState.KnownMet, discovery.Children[0].Source!.WorldAccessState);
        Assert.Equal(RecipeLearningNodeKind.Fish, discovery.Children[1].Kind);
        Assert.Equal(RecipeDiscoveryState.KnownMet, discovery.Children[1].State);
    }

    [Fact]
    public void Concealed_special_email_and_fish_methods_are_leaves_with_unknown_visible_completion()
    {
        var special = Special("target", "secret_source", "Secret place");
        var catalog = Catalog(Definition("target", false)) with { SpecialGrants = [special] };
        var tree = RecipeLearningTree.Build("target", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id),
            id => new(id, [Source(RecipeDiscoverySourceKind.Email, "secret_email", RecipeDiscoveryState.KnownMet),
                Source(RecipeDiscoverySourceKind.Fish, "secret_fish", RecipeDiscoveryState.KnownMet)]),
            specialConcealed: _ => true, sourceConcealed: _ => true);
        var sealedNodes = Nodes(tree).Where(node => node.Concealed).ToArray();

        Assert.Equal(3, sealedNodes.Length);
        Assert.All(sealedNodes, node =>
        {
            Assert.Empty(node.Children);
            Assert.Equal(RecipeDiscoveryState.Unknown, node.State);
        });
    }

    [Fact]
    public void Equivalent_special_actions_consolidate_but_different_kinds_levels_and_counts_remain_alternatives()
    {
        var original = Special("target", "line_a", "Person") with { LevelId = "level_a", RequiredCount = 2 };
        var catalog = Catalog(Definition("target", false)) with
        {
            SpecialGrants = [original, original with { SourceId = "line_b", SourceLabel = "PERSON" },
                original with { SourceId = "line_c", LevelId = "level_b" },
                original with { SourceId = "line_d", RequiredCount = 3 },
                original with { SourceId = "line_e", Kind = RecipeSpecialGrantKind.Inspectable },
                Special("unrelated", "other", "Other")],
        };
        var tree = RecipeLearningTree.Build("target", catalog, id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery);

        Assert.Equal(4, Nodes(tree).Count(node => node.Kind == RecipeLearningNodeKind.Special));
        Assert.DoesNotContain(Nodes(tree), node => node.RecipeId == "unrelated");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(20)]
    public void Every_node_counts_toward_the_budget_and_root_research_keeps_its_reserved_slot(int budget)
    {
        var group = new RecipeLearningInput("group", "substitutes", "Group",
            Enumerable.Range(0, 1000).Select(index => "item_" + index).ToArray(), true, true);
        var catalog = Catalog(Definition("target") with { DiscoveryInputs = [group] });
        var tree = RecipeLearningTree.Build("target", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery, maxNodes: budget);
        var nodes = Nodes(tree).ToArray();

        Assert.InRange(nodes.Length, 1, budget);
        Assert.Contains(nodes, node => node.Kind == RecipeLearningNodeKind.NodeLimit);
        Assert.Equal(nodes.Length, nodes.Select(node => node.Path).Distinct(StringComparer.Ordinal).Count());
        if (budget >= 3) Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Research);
    }

    [Fact]
    public void Deep_linked_chains_stop_visibly_before_assessing_beyond_the_depth_limit()
    {
        var definitions = Enumerable.Range(0, 30).Select(index => Definition("recipe_" + index, false) with
            { LinkedRecipeIds = index == 0 ? [] : ["recipe_" + (index - 1)] }).ToArray();
        var catalog = Catalog(definitions);
        var assessed = new List<string>();
        var tree = RecipeLearningTree.Build("recipe_0", catalog, id =>
        {
            assessed.Add(id);
            return RecipeLearningAssessment.Assess(catalog, id);
        }, EmptyDiscovery, maxDepth: 4);

        Assert.Contains(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.DepthLimit);
        Assert.Equal(2, assessed.Count);
        Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Research);
    }

    [Fact]
    public void Pending_research_wins_over_the_usual_no_minigame_difficulty_and_unknown_methods_stay_explicit()
    {
        var catalog = Catalog(Definition("target", false));
        var tree = RecipeLearningTree.Build("target", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id, discoveredRecipeIds: [id], researchPendingIds: [id]), EmptyDiscovery);

        Assert.Equal(RecipeDiscoveryState.KnownUnmet, Assert.Single(tree.Children, node => node.Kind == RecipeLearningNodeKind.Research).State);
        Assert.Single(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.Unknown);
    }

    [Fact]
    public void Unresolved_groups_and_absent_input_schema_never_become_literal_pickup_requirements()
    {
        var unresolved = new RecipeLearningInput("AnyFuture", "substitutes", null, [], true, false);
        var catalog = Catalog(Definition("target") with { DiscoveryInputs = [unresolved] });
        var tree = RecipeLearningTree.Build("target", catalog, id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery);
        Assert.DoesNotContain(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.PickupItem);
        Assert.Single(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.Unknown);

        catalog = Catalog(Definition("target") with { DiscoveryInputsKnown = false });
        tree = RecipeLearningTree.Build("target", catalog, id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery);
        Assert.DoesNotContain(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.PickupItem);
        Assert.Single(Nodes(tree), node => node.Kind == RecipeLearningNodeKind.Unknown);
    }

    [Fact]
    public void Stable_inputs_produce_stable_paths_and_invalid_limits_are_refused()
    {
        var catalog = Catalog(Definition("target"));
        RecipeLearningNode Build() => RecipeLearningTree.Build("target", catalog, id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery);
        Assert.Equal(Nodes(Build()).Select(node => (node.Path, node.Kind, node.RecipeId)),
            Nodes(Build()).Select(node => (node.Path, node.Kind, node.RecipeId)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeLearningTree.Build("target", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery, maxNodes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeLearningTree.Build("target", catalog,
            id => RecipeLearningAssessment.Assess(catalog, id), EmptyDiscovery, maxDepth: -1));
    }

    private static IEnumerable<RecipeLearningNode> Nodes(RecipeLearningNode root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var node in Nodes(child)) yield return node;
    }
    private static RecipeDiscoveryAssessment EmptyDiscovery(string id) => new(id, []);
    private static RecipeLearningInput Input(string id) => new(id, "items", null, [id], false, true);
    private static RecipeLearningDefinition Definition(string id, bool pickup = true)
        => new(id, "table", pickup, 0, [Input("ore")], [], "output_" + id) { DiscoveryInputsKnown = true };
    private static RecipeLearningCatalogData Catalog(params RecipeLearningDefinition[] definitions)
        => new(RecipeLearningCatalog.CurrentSchemaVersion, definitions, [], [], []);
    private static RecipeSpecialGrant Special(string recipe, string id, string label)
        => new(recipe, RecipeSpecialGrantKind.Conversation, id, label, "source_asset");
    private static RecipeDiscoveryRoute Source(RecipeDiscoverySourceKind kind, string id, RecipeDiscoveryState recorded)
        => new(kind, id, kind == RecipeDiscoverySourceKind.Email ? "Subject" : null,
            kind == RecipeDiscoverySourceKind.Fish ? "fish_item" : null, "world_flag", null, null, null, false,
            RecipeDiscoveryState.KnownMet, recorded);
}
