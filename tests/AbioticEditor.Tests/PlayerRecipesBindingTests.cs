using System.Reflection;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

public sealed class PlayerRecipesBindingTests
{
    [Fact]
    public async Task Missed_check_uses_verified_pickup_eligibility_and_current_history_instead_of_crafting_inputs_alone()
    {
        RecipeInfo[] recipes = [
            new("allowed", null, 1, null, "Crafting", [new("material", 1)]),
            new("blocked", null, 1, null, "Crafting", [new("material", 1)]),
        ];
        RecipeLearningInput[] inputs = [new("material", "items", null, ["material"], false, true)];
        var learning = new RecipeLearningCatalogData(RecipeLearningCatalog.CurrentSchemaVersion,
        [
            new("allowed", "recipes", true, 0, inputs, [], null) { DiscoveryInputsKnown = true },
            new("blocked", "recipes", false, 0, inputs, [], null) { DiscoveryInputsKnown = true },
        ], [], [], []);
        var catalog = new RecipeCatalogSnapshot(recipes, [], CodexVocabulary.Empty,
            new RecipeCatalogMetadata(1, RecipeCatalogSource.SuppliedSnapshot, DateTimeOffset.UtcNow, null, "en", false))
        { RecipeLearning = learning };
        await using var harness = new BindingHarness(catalog);
        var session = new CountingSession { RecipeItemsPickedUp = ["MATERIAL"] };
        var view = await harness.Begin(session);
        harness.ReleaseLoader();
        await view.QuiescenceTask;

        Assert.Equal("allowed", Assert.Single(await harness.ReviewIds()));
        session.RecipeItemsPickedUp = [];
        Assert.Empty(await harness.ReviewIds());
        session.RecipeItemsPickedUp = null;
        Assert.Empty(await harness.ReviewIds());
    }

    [Fact]
    public async Task Repeated_parent_parameters_while_loading_bind_the_recipe_catalog_once()
    {
        await using var harness = new BindingHarness();
        var session = new CountingSession();
        var view = await harness.Begin(session);
        await harness.LoaderEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await harness.Update(session);
        await harness.Update(session);
        await harness.Update(session);
        Assert.Equal(1, harness.LoadCount);
        Assert.Equal(0, session.BindCount);

        harness.ReleaseLoader();
        await view.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, session.BindCount);
        Assert.Equal(1, harness.InputCheckCount);
        await harness.Update(session);
        Assert.Equal(1, session.BindCount);
        Assert.Equal(1, harness.LoadCount);
        Assert.Equal(1, harness.InputCheckCount);
    }

    [Fact]
    public async Task Switching_sessions_during_a_load_does_not_apply_the_old_binding()
    {
        await using var harness = new BindingHarness();
        var previous = new CountingSession();
        var current = new CountingSession();
        var view = await harness.Begin(previous);
        await harness.LoaderEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await harness.Update(current);
        harness.ReleaseLoader();
        await view.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, previous.BindCount);
        Assert.Equal(1, current.BindCount);
        Assert.Single(current.Recipes);
        await harness.Update(current);
        Assert.Equal(1, current.BindCount);
    }

    [Fact]
    public async Task A_failed_catalog_load_releases_the_binding_for_a_same_session_retry()
    {
        await using var harness = new BindingHarness { FailFirstLoad = true };
        var session = new CountingSession();
        var view = await harness.Begin(session);
        await harness.LoaderEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var originalChild = harness.Host.Child;
        harness.ReleaseLoader();
        await Assert.ThrowsAsync<IOException>(() => view.QuiescenceTask);
        Assert.Equal(0, session.BindCount);

        await harness.Update(session);
        await session.Bound.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(originalChild, harness.Host.Child);
        Assert.Equal(2, harness.LoadCount);
        Assert.Equal(1, session.BindCount);
    }

    private sealed class BindingHarness : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ServiceProvider _provider;
        private readonly HtmlRenderer _renderer;
        private int _loadCount;
        private int _inputCheckCount;
        public TaskCompletionSource LoaderEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailFirstLoad { get; init; }
        public int LoadCount => Volatile.Read(ref _loadCount);
        public int InputCheckCount => Volatile.Read(ref _inputCheckCount);
        public BindingHost Host { get; private set; } = null!;

        public BindingHarness(RecipeCatalogSnapshot? suppliedCatalog = null)
        {
            var recipes = new RecipeVocabularyService(TimeProvider.System, () =>
            {
                Interlocked.Increment(ref _inputCheckCount);
                return (1, null);
            }, (revision, _, at) =>
            {
                var attempt = Interlocked.Increment(ref _loadCount);
                LoaderEntered.TrySetResult();
                _release.Task.GetAwaiter().GetResult();
                if (FailFirstLoad && attempt == 1) throw new IOException("Controlled recipe load failure.");
                return suppliedCatalog ?? new RecipeCatalogSnapshot([new RecipeInfo("recipe_loaded", null, 1, null, "Crafting", [])],
                    [], CodexVocabulary.Empty, new RecipeCatalogMetadata(revision,
                        RecipeCatalogSource.SuppliedSnapshot, at, null, "en", false));
            }, () => { });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(recipes);
            services.AddSingleton<HostLanguageService>();
            services.AddSingleton<UserFacingErrorService>();
            services.AddSingleton<ItemCatalogService>();
            services.AddSingleton<CodexVocabularyService>();
            services.AddSingleton<ProgressionVocabularyService>();
            services.AddSingleton<InventorySelectionService>();
            services.AddSingleton<ModalService>();
            services.AddSingleton<ToastService>();
            services.AddSingleton(new HostSpoilerPreferences(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "spoilers.json")));
            services.AddSingleton<ISaveFileSystem, DesktopSaveFileSystem>();
            services.AddSingleton<SaveWorkspaceSessionService>();
            services.AddSingleton<SiblingWorldBedService>();
            services.AddSingleton<RecipeProgressGateService>();
            // These optional catalogs are unrelated to binding. Keep the rendered test independent
            // of the installed game without adding production injection seams for their loaders.
            services.AddSingleton(WithLazy(new TraderVocabularyService(), "_traders", (IReadOnlyList<TraderInfo>)[]));
            services.AddSingleton(WithLazy(new ItemUpgradeVocabularyService(), "_catalog", ItemUpgradeCatalog.Empty));
            _provider = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_provider, _provider.GetRequiredService<ILoggerFactory>());
        }

        public Task<HtmlRootComponent> Begin(IPlayerRecipesSession session) => _renderer.Dispatcher.InvokeAsync(() =>
            _renderer.BeginRenderingComponent<BindingHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BindingHost.Session)] = session,
                [nameof(BindingHost.Capture)] = (Action<BindingHost>)(host => Host = host),
            })));

        public Task Update(IPlayerRecipesSession session) => _renderer.Dispatcher.InvokeAsync(() => Host.Update(session));
        public Task<string[]> ReviewIds() => _renderer.Dispatcher.InvokeAsync(() =>
            ((IReadOnlyList<RecipeInfo>)typeof(PlayerRecipesTab).GetProperty("ReviewRecipes", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Host.Child)!).Select(recipe => recipe.Id).ToArray());
        public void ReleaseLoader() => _release.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            ReleaseLoader();
            await _renderer.DisposeAsync();
            await _provider.DisposeAsync();
        }

        private static T WithLazy<T, TValue>(T service, string fieldName, TValue value) where T : class
        {
            typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, new Lazy<TValue>(() => value));
            return service;
        }
    }

    public sealed class BindingHost : ComponentBase
    {
        [Parameter] public IPlayerRecipesSession Session { get; set; } = null!;
        [Parameter] public Action<BindingHost> Capture { get; set; } = null!;
        public PlayerRecipesTab Child { get; private set; } = null!;
        protected override void OnInitialized() => Capture(this);
        public void Update(IPlayerRecipesSession session)
        {
            Session = session;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<PlayerRecipesTab>(0);
            builder.AddComponentParameter(1, nameof(PlayerRecipesTab.Session), Session);
            builder.AddComponentReferenceCapture(2, child => Child = (PlayerRecipesTab)child);
            builder.CloseComponent();
        }
    }

    private sealed class CountingSession : IPlayerRecipesSession
    {
        private readonly List<PlayerRecipeEdit> _recipes = [];
        public TaskCompletionSource Bound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BindCount { get; private set; }
        public IReadOnlyList<PlayerRecipeEdit> Recipes => _recipes;
        public int UnlockedRecipeCount => 0;
        public int RecipeCount => _recipes.Count;
        public bool AppliesImmediately => false;
        public bool CanLock => true;
        public IReadOnlyCollection<string>? RecipeItemsPickedUp { get; set; }
        public void EnsureRecipeRows(IEnumerable<string> ids)
        {
            BindCount++;
            foreach (var id in ids) _recipes.Add(new PlayerRecipeEdit(id, false));
            Bound.TrySetResult();
        }
        public Task SetUnlockedAsync(string recipeId, bool unlocked) => throw new NotSupportedException();
        public void MarkChanged() => throw new NotSupportedException();
    }
}
