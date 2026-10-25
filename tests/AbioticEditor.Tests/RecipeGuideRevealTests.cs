using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AbioticEditor.Tests;

// Exercise OnAfterRender through a renderer without controlling any browser or real focus.
#pragma warning disable BL0006
public sealed class RecipeGuideRevealTests
{
    [Fact]
    public void Bringing_the_editor_into_view_preserves_the_selected_detail()
    {
        var selection = new InventorySelectionService();
        RenderFragment detail = _ => { };
        selection.ShowDetail("selected-recipe", detail);
        var requests = 0;
        selection.EditorFocusRequested += () => requests++;

        selection.RequestEditorFocus();

        Assert.Equal(1, requests);
        Assert.Equal("selected-recipe", selection.DetailKey);
        Assert.Same(detail, selection.DetailContent);
    }

    [Fact]
    public async Task Each_explicit_navigation_reveals_the_tree_once_including_reopening_the_same_recipe()
    {
        var first = Recipe("recipe_first", "first_output");
        var second = Recipe("recipe_second", "second_output");
        var graph = new RecipeGraph([first, second], ["first_output", "second_output", "material"]);
        var js = new RecordingJs();
        await using var provider = Services(js).BuildServiceProvider();
        await using var renderer = new LifecycleRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeCraftingSteps>();
            await renderer.Render(component, Parameters(graph, first, 1));
            Assert.Equal(1, js.RevealCount);

            await renderer.Render(component, Parameters(graph, first, 1));
            Assert.Equal(1, js.RevealCount);

            await renderer.Render(component, Parameters(graph, first, 2));
            Assert.Equal(2, js.RevealCount);

            await renderer.Render(component, Parameters(graph, second, 3));
            Assert.Equal(3, js.RevealCount);
            Assert.Equal(second.Id, renderer.CraftingRoot(component).Id);

            await renderer.Render(component, Parameters(graph, second, 3));
            Assert.Equal(3, js.RevealCount);
            Assert.Equal(1, js.ImportCount);
            Assert.Equal("./_content/AbioticEditor.Web.Shared/recipe-guide.js", js.ImportPath);
        });
    }

    [Fact]
    public async Task Ordinary_updates_without_an_explicit_navigation_never_request_focus()
    {
        var first = Recipe("recipe_first", "first_output");
        var second = Recipe("recipe_second", "second_output");
        var graph = new RecipeGraph([first, second], ["first_output", "second_output", "material"]);
        var js = new RecordingJs();
        await using var provider = Services(js).BuildServiceProvider();
        await using var renderer = new LifecycleRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeCraftingSteps>();
            await renderer.Render(component, Parameters(graph, first, 0));
            await renderer.Render(component, Parameters(graph, first, 0));
            await renderer.Render(component, Parameters(graph, second, 0));

            Assert.Equal(0, js.RevealCount);
            Assert.Equal(0, js.ImportCount);
        });
    }

    [Theory]
    [InlineData("import")]
    [InlineData("reveal")]
    public async Task A_failed_focus_enhancement_keeps_the_guide_and_can_retry_on_the_next_click(string operation)
    {
        var recipe = Recipe("recipe_first", "first_output");
        var graph = new RecipeGraph([recipe], ["first_output", "material"]);
        var js = new RecordingJs { FailOnce = operation };
        await using var provider = Services(js).BuildServiceProvider();
        await using var renderer = new LifecycleRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = renderer.Attach<RecipeCraftingSteps>();
            await renderer.Render(component, Parameters(graph, recipe, 1));
            Assert.Equal(recipe.Id, renderer.CraftingRoot(component).Id);
            await renderer.Render(component, Parameters(graph, recipe, 2));
            Assert.Equal(1, js.RevealCount);
        });
    }

    private static RecipeInfo Recipe(string id, string itemId)
        => new(id, itemId, 1, null, "Crafting", [new("material", 1)]);

    private static ServiceCollection Services(RecordingJs js)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostLanguageService>();
        services.AddSingleton<UserFacingErrorService>();
        services.AddSingleton<ItemCatalogService>();
        services.AddSingleton<IJSRuntime>(js);
        return services;
    }

    private static ParameterView Parameters(RecipeGraph graph, RecipeInfo recipe, long revealRequest)
        => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(RecipeCraftingSteps.Graph)] = graph,
            [nameof(RecipeCraftingSteps.RootRecipe)] = recipe,
            [nameof(RecipeCraftingSteps.ItemName)] = (Func<string, string>)(id => id),
            [nameof(RecipeCraftingSteps.StationName)] = (Func<RecipeInfo, string>)(_ => "Crafting bench"),
            [nameof(RecipeCraftingSteps.RouteName)] = (Func<RecipeInfo, string>)(info => info.Id),
            [nameof(RecipeCraftingSteps.Concealed)] = (Func<RecipeInfo, bool>)(_ => false),
            [nameof(RecipeCraftingSteps.Learned)] = (Func<string, bool>)(_ => false),
            [nameof(RecipeCraftingSteps.RevealRequest)] = revealRequest,
        });

    private sealed class LifecycleRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : Renderer(services, loggerFactory)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach<T>() where T : IComponent => AssignRootComponentId(InstantiateComponent(typeof(T)));
        public Task Render(int componentId, ParameterView parameters) => RenderRootComponentAsync(componentId, parameters);
        public RecipeInfo CraftingRoot(int componentId)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            var branch = Assert.Single(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(RecipeCraftingBranch));
            return ((RecipeCraftingBranch)branch.Component).Node.Recipe!;
        }
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Rendered guide test failed.", exception);
    }

    private sealed class RecordingJs : IJSRuntime, IJSObjectReference
    {
        public int ImportCount { get; private set; }
        public int RevealCount { get; private set; }
        public string? ImportPath { get; private set; }
        public string? FailOnce { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (FailOnce == identifier) { FailOnce = null; throw new JSException("Controlled guide reveal failure."); }
            if (identifier == "import")
            {
                ImportCount++;
                ImportPath = Assert.IsType<string>(Assert.Single(args!));
                return ValueTask.FromResult((TValue)(object)this);
            }
            Assert.Equal("reveal", identifier);
            Assert.IsType<ElementReference>(Assert.Single(args!));
            RevealCount++;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#pragma warning restore BL0006
