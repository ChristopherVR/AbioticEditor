using System.Globalization;
using System.Net;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AbioticEditor.Tests;

#pragma warning disable BL0006
public sealed class RecipeLearningTreeViewTests
{
    [Fact]
    public async Task Initial_render_has_connected_nested_lists_and_distinguishes_all_inputs_from_any_substitute()
    {
        var language = new HostLanguageService();
        var html = await Html(language, Tree());
        Assert.Contains(language.Resource("RecipeLearningTree_Discovery"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearningTree_PickupAll"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearningTree_PickupAny"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearningTree_Alternatives"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearningTree_FinishResearch"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_PendingResearch"), html, StringComparison.Ordinal);
        Assert.True(html.Split("class=\"learning-tree-children\"", StringSplitOptions.None).Length >= 4);
        Assert.DoesNotContain("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Wood item", html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearningTree_InspectItem"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_discovered_live_recipe_with_unknown_research_state_does_not_instruct_the_player_to_finish_a_minigame()
    {
        var language = new HostLanguageService();
        var assessment = Assessment(pending: null);
        var root = Tree(assessment: assessment);
        var html = await Html(language, root);
        Assert.Contains(language.Resource("RecipeLearning_ResearchStateUnknown"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeLearningTree_FinishResearch"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeLearning_ResearchPending"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sealed_recipe_special_and_email_nodes_never_evaluate_secret_names_or_render_descendants()
    {
        var language = new HostLanguageService();
        var secretSource = new RecipeDiscoveryRoute(RecipeDiscoverySourceKind.Email, "secret-email-id", "secret email title", null,
            "secret-world-flag", null, "secret-dlc", "secret-bait", true, RecipeDiscoveryState.Unknown, RecipeDiscoveryState.Unknown);
        var special = new RecipeSpecialGrant("target", RecipeSpecialGrantKind.Inspectable, "secret-actor", "secret object", "secret asset") { LevelId = "secret-level" };
        var discovery = new RecipeLearningNode("root.0", RecipeLearningNodeKind.Discovery, "target",
        [
            new("root.0.0", RecipeLearningNodeKind.Recipe, "secret-recipe", [new("root.0.0.0", RecipeLearningNodeKind.PickupItem, "secret-recipe", []) { ItemId = "secret-item" }]) { Concealed = true },
            new("root.0.1", RecipeLearningNodeKind.Special, "target", []) { Concealed = true, Special = special },
            new("root.0.2", RecipeLearningNodeKind.Email, "target", []) { Concealed = true, Source = secretSource },
        ]);
        var root = new RecipeLearningNode("root", RecipeLearningNodeKind.Recipe, "target", [discovery]) { IsRoot = true };
        var html = await Html(language, root);
        Assert.Contains(language.Resource("RecipeLearning_SourceSealed"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("root.0.0.0", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecipeLearningNodeKind.Cycle, "RecipeLearningTree_Cycle")]
    [InlineData(RecipeLearningNodeKind.DepthLimit, "RecipeLearningTree_DepthLimit")]
    [InlineData(RecipeLearningNodeKind.NodeLimit, "RecipeLearningTree_NodeLimit")]
    public async Task Guard_nodes_explain_why_a_branch_stops_without_exposing_internal_recipe_ids(RecipeLearningNodeKind kind, string message)
    {
        var language = new HostLanguageService();
        var stop = new RecipeLearningNode("root.0", kind, "secret-internal-recipe", []);
        var html = await Html(language, new("root", RecipeLearningNodeKind.Recipe, "target", [stop]) { IsRoot = true });
        Assert.Contains(language.Resource(message), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unresolved_pickup_nodes_use_the_specific_unknown_input_explanation()
    {
        var language = new HostLanguageService();
        var unknown = new RecipeLearningNode("root.0", RecipeLearningNodeKind.Unknown, "target", [])
        {
            Input = new(new("secret-group", null, null, [], true, false), RecipeDiscoveryState.Unknown),
        };
        var html = await Html(language, new("root", RecipeLearningNodeKind.Recipe, "target", [unknown]) { IsRoot = true });
        Assert.Contains(language.Resource("RecipeLearning_UnresolvedInput"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-group", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fish_conditions_are_context_under_one_leaf_and_do_not_become_an_all_required_branch()
    {
        var language = new HostLanguageService();
        var source = new RecipeDiscoveryRoute(RecipeDiscoverySourceKind.Fish, "fish-id", null, "fish", "worldflag", null,
            "dlc", "bait", true, RecipeDiscoveryState.KnownUnmet, RecipeDiscoveryState.KnownMet);
        var fish = new RecipeLearningNode("root.0", RecipeLearningNodeKind.Fish, "target", []) { Source = source, State = RecipeDiscoveryState.KnownMet };
        var html = await Html(language, new("root", RecipeLearningNodeKind.Recipe, "target", [fish]) { IsRoot = true });
        Assert.Contains(language.Resource("RecipeGuide_FishRecorded"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_StoryUnmet"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_SourceBait", "Bait"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_SourceDlc", "Dlc"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_SourceTime"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-node-kind=\"PickupAll\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expand_collapse_and_toggle_events_preserve_expansion_across_new_objects_for_the_same_selected_recipe()
    {
        await using var provider = Services(new RecordingJs()).BuildServiceProvider();
        await using var renderer = new EventRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeLearningTreeView>();
            await renderer.Render(component, Parameters(Tree()));
            Assert.False(Button(renderer, component, "toggle", "root.0.0.1").Expanded);
            await Click(renderer, component, "toggle", "root.0.0.1");
            Assert.True(Button(renderer, component, "toggle", "root.0.0.1").Expanded);
            Assert.Contains("Wood item", Text(renderer, component), StringComparison.Ordinal);

            await renderer.Render(component, Parameters(Tree() with { State = RecipeDiscoveryState.KnownMet }));
            Assert.True(Button(renderer, component, "toggle", "root.0.0.1").Expanded);
            await Click(renderer, component, "collapse-all");
            Assert.DoesNotContain("Metal item", Text(renderer, component), StringComparison.Ordinal);
            Assert.False(Button(renderer, component, "toggle", "root.0").Expanded);
            await Click(renderer, component, "expand-all");
            Assert.Contains("Wood item", Text(renderer, component), StringComparison.Ordinal);
            Assert.Contains("Crafting", Text(renderer, component), StringComparison.Ordinal);

            await renderer.Render(component, Parameters(Tree("second")));
            Assert.False(Button(renderer, component, "toggle", "root.0.0.1").Expanded);
        });
    }

    [Fact]
    public async Task Inspect_item_and_each_reveal_event_send_the_actual_selected_payload()
    {
        await using var provider = Services(new RecordingJs()).BuildServiceProvider();
        await using var renderer = new EventRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        string? inspected = null, revealedRecipe = null;
        RecipeSpecialGrant? revealedSpecial = null;
        RecipeDiscoveryRoute? revealedSource = null;
        var owner = new object();
        var special = new RecipeSpecialGrant("target", RecipeSpecialGrantKind.Conversation, "secret-row", "secret person", "asset");
        var source = new RecipeDiscoveryRoute(RecipeDiscoverySourceKind.Email, "secret-email", "secret title", null, null, null, null, null, false, RecipeDiscoveryState.Unknown, RecipeDiscoveryState.Unknown);
        var root = new RecipeLearningNode("root", RecipeLearningNodeKind.Recipe, "target",
        [
            new("root.0", RecipeLearningNodeKind.PickupItem, "target", []) { ItemId = "metal" },
            new("root.1", RecipeLearningNodeKind.Recipe, "secret-recipe", []) { Concealed = true },
            new("root.2", RecipeLearningNodeKind.Special, "target", []) { Concealed = true, Special = special },
            new("root.3", RecipeLearningNodeKind.Email, "target", []) { Concealed = true, Source = source },
        ]) { IsRoot = true };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeLearningTreeView>();
            var parameters = ParameterDictionary(root);
            parameters[nameof(RecipeLearningTreeView.OnInspectItem)] = EventCallback.Factory.Create<string>(owner, (Action<string>)(id => inspected = id));
            parameters[nameof(RecipeLearningTreeView.OnRevealRecipe)] = EventCallback.Factory.Create<string>(owner, (Action<string>)(id => revealedRecipe = id));
            parameters[nameof(RecipeLearningTreeView.OnRevealSpecial)] = EventCallback.Factory.Create<RecipeSpecialGrant>(owner, (Action<RecipeSpecialGrant>)(grant => revealedSpecial = grant));
            parameters[nameof(RecipeLearningTreeView.OnRevealSource)] = EventCallback.Factory.Create<RecipeDiscoveryRoute>(owner, (Action<RecipeDiscoveryRoute>)(grant => revealedSource = grant));
            await renderer.Render(component, ParameterView.FromDictionary(parameters));
            await Click(renderer, component, "inspect-item", "root.0");
            await Click(renderer, component, "reveal", "root.1");
            await Click(renderer, component, "reveal", "root.2");
            await Click(renderer, component, "reveal", "root.3");
        });
        Assert.Equal("metal", inspected);
        Assert.Equal("secret-recipe", revealedRecipe);
        Assert.Same(special, revealedSpecial);
        Assert.Same(source, revealedSource);
    }

    [Fact]
    public async Task Focus_runs_only_for_explicit_navigation_including_reopening_the_same_recipe()
    {
        var js = new RecordingJs();
        await using var provider = Services(js).BuildServiceProvider();
        await using var renderer = new EventRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeLearningTreeView>();
            await renderer.Render(component, Parameters(Tree()));
            Assert.Equal(0, js.Reveals);
            await renderer.Render(component, Parameters(Tree(), 1));
            Assert.Equal(1, js.Reveals);
            await Click(renderer, component, "collapse-all");
            await renderer.Render(component, Parameters(Tree(), 1));
            Assert.Equal(1, js.Reveals);
            await renderer.Render(component, Parameters(Tree(), 2));
            Assert.Equal(2, js.Reveals);
            Assert.Equal(1, js.Imports);
        });
    }

    private static RecipeLearningNode Tree(string id = "target", RecipeLearningAssessment? assessment = null)
    {
        assessment ??= Assessment();
        var any = new RecipeLearningNode("root.0.0.1", RecipeLearningNodeKind.PickupAny, id,
        [
            new("root.0.0.1.0", RecipeLearningNodeKind.PickupItem, id, []) { ItemId = "wood", State = RecipeDiscoveryState.KnownUnmet },
            new("root.0.0.1.1", RecipeLearningNodeKind.PickupItem, id, []) { ItemId = "plastic", State = RecipeDiscoveryState.Unknown },
        ]);
        var all = new RecipeLearningNode("root.0.0", RecipeLearningNodeKind.PickupAll, id,
        [new("root.0.0.0", RecipeLearningNodeKind.PickupItem, id, []) { ItemId = "metal", State = RecipeDiscoveryState.KnownMet }, any]);
        var linked = new RecipeLearningNode("root.0.1", RecipeLearningNodeKind.Recipe, "prerequisite",
        [new("root.0.1.0", RecipeLearningNodeKind.Discovery, "prerequisite",
            [new("root.0.1.0.0", RecipeLearningNodeKind.Skill, "prerequisite", []) { Skill = new(new("prerequisite", "Crafting", 7), RecipeDiscoveryState.KnownUnmet) }])]);
        var discovery = new RecipeLearningNode("root.0", RecipeLearningNodeKind.Discovery, id, [all, linked]);
        return new("root", RecipeLearningNodeKind.Recipe, id,
            [discovery, new("root.1", RecipeLearningNodeKind.Research, id, []) { Assessment = assessment }])
            { IsRoot = true, Assessment = assessment, State = RecipeDiscoveryState.KnownMet };
    }
    private static RecipeLearningAssessment Assessment(bool? pending = true)
    {
        var definition = new RecipeLearningDefinition("target", "table", true, 1, [], [], "output");
        return new("target", definition, [], RecipeDiscoveryState.Unknown, RecipeDiscoveryState.Unknown,
            RecipeDiscoveryState.Unknown, [], [], true, pending);
    }
    private static ServiceCollection Services(RecordingJs js)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostLanguageService>();
        services.AddSingleton<UserFacingErrorService>();
        services.AddSingleton<IJSRuntime>(js);
        return services;
    }
    private static Dictionary<string, object?> ParameterDictionary(RecipeLearningNode root, long reveal = 0) => new()
    {
        [nameof(RecipeLearningTreeView.Root)] = root,
        [nameof(RecipeLearningTreeView.RevealRequest)] = reveal,
        [nameof(RecipeLearningTreeView.ItemName)] = (Func<string, string>)(id => id.Contains("secret", StringComparison.Ordinal) ? throw new InvalidOperationException("Sealed item label evaluated") : id switch { "metal" => "Metal item", "wood" => "Wood item", _ => "Plastic item" }),
        [nameof(RecipeLearningTreeView.RecipeName)] = (Func<string, string>)(id => id.Contains("secret", StringComparison.Ordinal) ? throw new InvalidOperationException("Sealed recipe label evaluated") : "Recipe " + id),
        [nameof(RecipeLearningTreeView.SkillName)] = (Func<string, string>)(id => id),
        [nameof(RecipeLearningTreeView.LevelName)] = (Func<string, string>)(id => id.Contains("secret", StringComparison.Ordinal) ? throw new InvalidOperationException("Sealed location evaluated") : id),
        [nameof(RecipeLearningTreeView.BenchName)] = (Func<string, string>)(id => id),
    };
    private static ParameterView Parameters(RecipeLearningNode root, long reveal = 0) => ParameterView.FromDictionary(ParameterDictionary(root, reveal));
    private static async Task<string> Html(HostLanguageService language, RecipeLearningNode root)
    {
        var services = Services(new RecordingJs());
        services.AddSingleton(language);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<RecipeLearningTreeView>(Parameters(root))).ToHtmlString()));
    }
    private static string Text(EventRenderer renderer, int component)
        => string.Join(" ", renderer.Frames(component).Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
    private sealed record ButtonInfo(ulong EventId, bool? Expanded);
    private static ButtonInfo Button(EventRenderer renderer, int component, string action, string? path = null)
    {
        var frames = renderer.Frames(component);
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index].FrameType != RenderTreeFrameType.Element || frames[index].ElementName != "button") continue;
            var attributes = frames.Skip(index + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute).ToArray();
            if (!attributes.Any(frame => frame.AttributeName == "data-action" && Equals(frame.AttributeValue, action))
                || path is not null && !attributes.Any(frame => frame.AttributeName == "data-path" && Equals(frame.AttributeValue, path))) continue;
            var click = Assert.Single(attributes, frame => frame.AttributeName == "onclick");
            var expanded = attributes.FirstOrDefault(frame => frame.AttributeName == "aria-expanded");
            return new(click.AttributeEventHandlerId, expanded.AttributeValue is null ? null : Convert.ToBoolean(expanded.AttributeValue, CultureInfo.InvariantCulture));
        }
        throw new InvalidOperationException("Rendered button not found: " + action + " " + path);
    }
    private static Task Click(EventRenderer renderer, int component, string action, string? path = null)
        => renderer.DispatchEventAsync(Button(renderer, component, action, path).EventId, null, new MouseEventArgs());
    private sealed class EventRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : Renderer(services, loggerFactory)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach<T>() where T : IComponent => AssignRootComponentId(InstantiateComponent(typeof(T)));
        public Task Render(int component, ParameterView parameters) => RenderRootComponentAsync(component, parameters);
        public RenderTreeFrame[] Frames(int component) { var frames = GetCurrentRenderTreeFrames(component); return frames.Array.Take(frames.Count).ToArray(); }
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Learning tree render failed.", exception);
    }
    private sealed class RecordingJs : IJSRuntime, IJSObjectReference
    {
        public int Imports { get; private set; }
        public int Reveals { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "import") { Imports++; return ValueTask.FromResult((TValue)(object)this); }
            Assert.Equal("reveal", identifier);
            Reveals++;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#pragma warning restore BL0006
