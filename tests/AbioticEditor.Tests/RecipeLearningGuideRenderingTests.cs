using System.Net;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

public sealed class RecipeLearningGuideRenderingTests
{
    [Fact]
    public async Task Pickup_types_show_one_of_substitutes_and_recorded_missing_unknown_states_without_costs()
    {
        RecipeLearningInput[] inputs =
        [
            new("metal", "items", null, ["metal"], false, true),
            new("AnySecretGroup", "substitutes", "secret-untrusted-group-name", ["wood", "plastic"], true, true),
            new("unresolved-internal-id", null, null, [], true, false),
        ];
        var language = new HostLanguageService();
        var html = await Render(language, Assessment(inputs: inputs, picked: ["metal"]));
        Assert.Contains(language.Resource("RecipeLearning_PickedUp"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_NotPickedUp"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_PickupUnknown"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_OneOf", 2), html, StringComparison.Ordinal);
        Assert.Contains("wood", html, StringComparison.Ordinal);
        Assert.Contains("plastic", html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_TypeCountHelp"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("AnySecretGroup", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-untrusted-group-name", html, StringComparison.Ordinal);
        Assert.DoesNotContain("unresolved-internal-id", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, false, "RecipeLearning_NoResearch")]
    [InlineData(1, false, false, "RecipeLearning_ResearchAfterDiscovery")]
    [InlineData(2, true, true, "RecipeLearning_ResearchPending")]
    [InlineData(1, true, false, "RecipeLearning_ResearchDone")]
    [InlineData(0, true, true, "RecipeLearning_ResearchPending")]
    public async Task Discovery_and_research_are_separate_and_the_actual_pending_queue_takes_precedence(
        int difficulty, bool discovered, bool pending, string expected)
    {
        var language = new HostLanguageService();
        var assessment = Assessment(difficulty: difficulty, discovered: discovered, pending: pending);
        var html = await Render(language, assessment);
        Assert.Contains(language.Resource(expected), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_PickupTypes"), html, StringComparison.Ordinal);
        if (pending)
        {
            Assert.False(assessment.RecipeKnowledgeAvailable);
            Assert.DoesNotContain(language.Resource("RecipeLearning_ResearchDone"), html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Missing_catalog_metadata_keeps_discovery_and_research_unknown()
    {
        var language = new HostLanguageService();
        var assessment = RecipeLearningAssessment.Assess(null, "recipe");
        var html = await Render(language, assessment);
        Assert.Contains(language.Resource("RecipeLearning_MethodUnknown"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_ResearchUnknown"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_DiscoveryStateUnknown"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeLearning_PickupTypes"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_input_schema_explains_the_unknown_requirement_instead_of_an_empty_checklist()
    {
        var language = new HostLanguageService();
        var assessment = Assessment();
        assessment = assessment with { Definition = assessment.Definition! with { DiscoveryInputsKnown = false } };
        var html = await Render(language, assessment);
        Assert.Contains(language.Resource("RecipeLearning_UnresolvedInput"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeLearning_PickupTypes"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeLearning_NoInputs"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sealed_sources_hide_names_locations_and_linked_recipe_names_before_evaluating_labels()
    {
        var language = new HostLanguageService();
        var assessment = Assessment() with { LinkedSources = [new("secret-linked-id", RecipeDiscoveryState.KnownMet)] };
        RecipeSpecialGrant[] sources =
        [
            new("recipe", RecipeSpecialGrantKind.Conversation, "secret-source-id", "secret-npc", "secret-asset"),
            new("recipe", RecipeSpecialGrantKind.Inspectable, "secret-actor-id", "secret-object", "secret-map") { LevelId = "secret-level", SourceObjectPath = "secret-actor-path" },
            new("recipe", RecipeSpecialGrantKind.Trait, "secret-trait-id", "secret-trait", "secret-asset"),
            new("recipe", RecipeSpecialGrantKind.BenchAccess, "secret-bench-id", "secret-bench", "secret-asset"),
        ];
        var html = await Render(language, assessment, sources, sealedSources: true);
        Assert.Contains(language.Resource("RecipeLearning_SourceSealed"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_RevealSource"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Equivalent_dialogue_actions_collapse_but_different_locations_and_counts_remain()
    {
        var language = new HostLanguageService();
        RecipeSpecialGrant[] sources =
        [
            new("recipe", RecipeSpecialGrantKind.Conversation, "hill-phase-1", "Dr. Hill", "table", SectionIndex: 0),
            new("recipe", RecipeSpecialGrantKind.Conversation, "hill-phase-2", "Dr. Hill", "table", SectionIndex: 1),
            new("recipe", RecipeSpecialGrantKind.Inspectable, "actor-a", "Object", "map-a") { LevelId = "Area A" },
            new("recipe", RecipeSpecialGrantKind.Inspectable, "actor-b", "Object", "map-b") { LevelId = "Area B" },
            new("recipe", RecipeSpecialGrantKind.Compendium, "enemy", "Enemy", "table", "Kill", 5),
            new("recipe", RecipeSpecialGrantKind.Compendium, "enemy", "Enemy", "table", "Kill", 10),
        ];
        var html = await Render(language, Assessment(), sources);
        var dialogue = language.Resource("RecipeLearning_Conversation", "Dr. Hill");
        Assert.Equal(1, html.Split(dialogue, StringSplitOptions.None).Length - 1);
        Assert.Contains("Area A", html, StringComparison.Ordinal);
        Assert.Contains("Area B", html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_CompendiumKills", "Enemy", 5), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_CompendiumKills", "Enemy", 10), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspection_without_an_actual_label_uses_a_generic_object_and_friendly_area()
    {
        var language = new HostLanguageService();
        var source = new RecipeSpecialGrant("recipe", RecipeSpecialGrantKind.Inspectable, "internal-actor-id", string.Empty, "internal-map-path") { LevelId = "Friendly area" };
        var html = await Render(language, Assessment(), [source]);
        Assert.Contains(language.Resource("RecipeLearning_Inspect", language.Resource("RecipeLearning_Object")), html, StringComparison.Ordinal);
        Assert.Contains("Friendly area", html, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("finished-output", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Large_source_lists_start_collapsed_with_a_count()
    {
        var language = new HostLanguageService();
        var sources = Enumerable.Range(0, 8).Select(index => new RecipeSpecialGrant("recipe", RecipeSpecialGrantKind.Conversation,
            "source-" + index, "Person " + index, "table")).ToArray();
        var html = await Render(language, Assessment(), sources);
        Assert.Contains(language.Resource("RecipeLearning_MoreSources", 8), html, StringComparison.Ordinal);
        Assert.Matches("<details(?:\\s[^>]*)?>", html);
        Assert.DoesNotMatch("<details\\b[^>]*\\bopen", html);
    }

    [Fact]
    public async Task Existing_email_or_fish_routes_prevent_a_false_no_method_warning()
    {
        var language = new HostLanguageService();
        var html = await Render(language, RecipeLearningAssessment.Assess(null, "recipe"), additionalSources: true);
        Assert.DoesNotContain(language.Resource("RecipeLearning_MethodUnknown"), html, StringComparison.Ordinal);
    }

    private static RecipeLearningAssessment Assessment(int? difficulty = 0, bool discovered = false, bool pending = false,
        RecipeLearningInput[]? inputs = null, string[]? picked = null)
    {
        var definition = new RecipeLearningDefinition("recipe", "table", true, difficulty,
            inputs ?? [new("metal", "items", null, ["metal"], false, true)], [], "finished-output") { DiscoveryInputsKnown = true };
        var catalog = new RecipeLearningCatalogData(RecipeLearningCatalog.CurrentSchemaVersion, [definition], [], [], []);
        return RecipeLearningAssessment.Assess(catalog, "recipe", picked ?? [], discovered ? ["recipe"] : [], pending ? ["recipe"] : []);
    }

    private static async Task<string> Render(HostLanguageService language, RecipeLearningAssessment assessment,
        RecipeSpecialGrant[]? sources = null, bool sealedSources = false, bool additionalSources = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(language);
        services.AddSingleton<UserFacingErrorService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<RecipeLearningGuide>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(RecipeLearningGuide.Assessment)] = assessment,
                [nameof(RecipeLearningGuide.HasAdditionalSources)] = additionalSources,
                [nameof(RecipeLearningGuide.SpecialGrants)] = sources ?? [],
                [nameof(RecipeLearningGuide.ItemName)] = (Func<string, string>)(id => id),
                [nameof(RecipeLearningGuide.BenchName)] = (Func<string, string>)(id => sealedSources ? throw new InvalidOperationException("Sealed bench name evaluated") : id),
                [nameof(RecipeLearningGuide.RecipeName)] = (Func<string, string>)(id => sealedSources ? throw new InvalidOperationException("Sealed linked name evaluated") : id),
                [nameof(RecipeLearningGuide.SkillName)] = (Func<string, string>)(id => id),
                [nameof(RecipeLearningGuide.LevelName)] = (Func<string, string>)(id => sealedSources ? throw new InvalidOperationException("Sealed location evaluated") : id),
                [nameof(RecipeLearningGuide.ConcealedSpecial)] = (Func<RecipeSpecialGrant, bool>)(_ => sealedSources),
                [nameof(RecipeLearningGuide.ConcealedLinked)] = (Func<string, bool>)(_ => sealedSources),
            }));
            return WebUtility.HtmlDecode(view.ToHtmlString());
        });
    }
}
