using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

// This event regression intentionally inspects Blazor's rendered event registrations.
#pragma warning disable BL0006
public sealed class RecipeUnlockBindingTests
{
    [Fact]
    public async Task Crafting_root_unlock_click_sends_the_selected_recipe_id()
    {
        var recipe = new RecipeInfo("recipe_selected_root", "crafted_output", 1, null, "Crafting", [new("material", 1)]);
        var graph = new RecipeGraph([recipe], ["crafted_output", "material"]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostLanguageService>();
        services.AddSingleton<UserFacingErrorService>();
        services.AddSingleton<ItemCatalogService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new EventRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        string? unlockedId = null;
        var callbackOwner = new object();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = renderer.Attach<RecipeCraftingSteps>();
            await renderer.Render(root, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(RecipeCraftingSteps.Graph)] = graph,
                [nameof(RecipeCraftingSteps.RootRecipe)] = recipe,
                [nameof(RecipeCraftingSteps.ItemName)] = (Func<string, string>)(id => id),
                [nameof(RecipeCraftingSteps.StationName)] = (Func<RecipeInfo, string>)(_ => "Crafting bench"),
                [nameof(RecipeCraftingSteps.RouteName)] = (Func<RecipeInfo, string>)(info => info.Id),
                [nameof(RecipeCraftingSteps.Concealed)] = (Func<RecipeInfo, bool>)(_ => false),
                [nameof(RecipeCraftingSteps.Learned)] = (Func<string, bool>)(_ => false),
                [nameof(RecipeCraftingSteps.OnUnlock)] = EventCallback.Factory.Create<string>(callbackOwner,
                    (Action<string>)(id => unlockedId = id)),
            }));

            var unlockControls = Assert.Single(renderer.Frames(root), frame =>
                frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(RecipeUnlockControls));
            var click = Assert.Single(renderer.Frames(unlockControls.ComponentId), frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onclick");
            Assert.NotEqual(0UL, click.AttributeEventHandlerId);

            await renderer.DispatchEventAsync(click.AttributeEventHandlerId, null, new MouseEventArgs());
        });

        Assert.Equal(recipe.Id, unlockedId);
    }

    private sealed class EventRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : Renderer(services, loggerFactory)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach<T>() where T : IComponent => AssignRootComponentId(InstantiateComponent(typeof(T)));
        public Task Render(int componentId, ParameterView parameters) => RenderRootComponentAsync(componentId, parameters);
        public RenderTreeFrame[] Frames(int componentId)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            return frames.Array.Take(frames.Count).ToArray();
        }
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Rendered recipe test failed.", exception);
    }
}
#pragma warning restore BL0006
