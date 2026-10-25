using System.Net;
using System.Text.Json;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

public sealed class RecipeWorkflowContractTests
{
    [Theory]
    [InlineData("unlock")]
    [InlineData("batch")]
    [InlineData("lock")]
    public async Task A_recipe_write_for_the_previous_live_player_cannot_change_the_new_players_rows(string operation)
    {
        await using var channel = new DelayedRecipeChannel();
        var live = await LivePlayerRecipesSession.ConnectAsync(new LivePlayerRecipesChannel(channel), "player-a");
        live.EnsureRecipeRows(["late_unlock"]);
        var originalIdentity = live.RecipeTargetIdentity;
        var notifications = 0;
        live.Changed += () => notifications++;

        channel.WriteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = operation switch
        {
            "batch" => live.SetUnlockedManyAsync(["late_unlock"]),
            "lock" => live.SetUnlockedAsync("a_known", false),
            _ => live.SetUnlockedAsync("late_unlock", true),
        };
        Assert.False(write.IsCompleted);
        Assert.Equal("player-a", channel.LastWrite.GetProperty("playerId").GetString());

        await live.SwitchPlayerAsync("player-b");
        Assert.NotEqual(originalIdentity, live.RecipeTargetIdentity);
        Assert.Equal("b_known", Assert.Single(live.Recipes, recipe => recipe.IsUnlocked).Id);

        channel.WriteCompletion.SetResult(true);
        await write;

        Assert.Equal("b_known", Assert.Single(live.Recipes, recipe => recipe.IsUnlocked).Id);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task An_old_refresh_cannot_replace_a_new_players_recipe_snapshot()
    {
        await using var channel = new DelayedRecipeChannel();
        var live = await LivePlayerRecipesSession.ConnectAsync(new LivePlayerRecipesChannel(channel), "player-a");
        var pendingRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead = pendingRead;
        var oldRefresh = live.RefreshAsync();
        Assert.False(oldRefresh.IsCompleted);

        await live.SwitchPlayerAsync("player-b");
        pendingRead.SetResult("{\"unlockedIds\":[\"stale_a_recipe\"],\"canLock\":true}");
        await oldRefresh;

        Assert.Equal("b_known", Assert.Single(live.Recipes, recipe => recipe.IsUnlocked).Id);
        Assert.DoesNotContain(live.Recipes, recipe => recipe.Id == "stale_a_recipe");
    }

    [Fact]
    public async Task Live_target_identity_changes_even_when_switching_back_to_the_same_player()
    {
        await using var channel = new DelayedRecipeChannel();
        var live = await LivePlayerRecipesSession.ConnectAsync(new LivePlayerRecipesChannel(channel), "player-a");
        var facade = new LivePlayerEditorSession { RecipesSession = live };
        var first = facade.RecipeTargetIdentity;
        await live.SwitchPlayerAsync("player-b");
        await live.SwitchPlayerAsync("player-a");

        Assert.NotEqual(first, facade.RecipeTargetIdentity);
        Assert.Equal(live.RecipeTargetIdentity, facade.RecipeTargetIdentity);
    }

    [Fact]
    public async Task Live_discovery_history_is_unavailable_during_a_player_switch_and_ignores_old_reads()
    {
        await using var channel = new DelayedRecipeChannel();
        var codex = await LivePlayerCodexSession.ConnectAsync(new LivePlayerCodexChannel(channel), "player-a");
        Assert.True(codex.HasCurrentHistory);
        var oldRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead = oldRead;
        var refresh = codex.RefreshAsync();
        var newRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead = newRead;
        var change = codex.SwitchPlayerAsync("player-b");
        Assert.False(codex.HasCurrentHistory);

        oldRead.SetResult("{\"emails\":[\"stale_a_grant\"],\"journals\":[],\"fish\":[],\"compendium\":[]}");
        await refresh;
        Assert.False(codex.HasCurrentHistory);
        newRead.SetResult("{\"emails\":[\"b_grant\"],\"journals\":[],\"fish\":[],\"compendium\":[]}");
        await change;

        Assert.True(codex.HasCurrentHistory);
        Assert.Equal("player-b", codex.SelectedPlayerId);
        Assert.Equal("b_grant", Assert.Single(codex.Emails, row => row.IsKnown).Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Rendered_suggestion_distinguishes_unlock_action_staged_completion_and_saved_completion(bool learned, bool staged)
    {
        var language = new HostLanguageService();
        var html = await RenderReport(language, concealed: false, learned, staged);

        Assert.Contains(language.Resource(learned ? staged ? "RecipeGuide_Staged" : "RecipeGuide_Unlocked" : "RecipeGuide_NotLearned"), html, StringComparison.Ordinal);
        if (!learned)
            Assert.Contains(language.Resource("RecipeGuide_Unlock"), html, StringComparison.Ordinal);
        if (staged)
            Assert.Contains(language.Resource("RecipeGuide_Undo"), html, StringComparison.Ordinal);
        else
            Assert.DoesNotContain(language.Resource("RecipeGuide_Undo"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rendered_sealed_suggestion_discloses_neither_item_names_nor_unlock_controls()
    {
        var language = new HostLanguageService();
        var html = await RenderReport(language, concealed: true, learned: false, staged: false);

        Assert.Contains(language.Resource("Spoiler_Redacted"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-item-name", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"primary recipe-unlock\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("item-artwork", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rendered_report_without_pickup_history_offers_no_invented_suggestions()
    {
        var language = new HostLanguageService();
        var html = await RenderReport(language, concealed: false, learned: false, staged: false, history: false);

        Assert.Contains(language.Resource("RecipeGuide_HistoryUnavailable"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"recipe-suggestion\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-item-name", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorded_grant_is_explained_without_inventing_an_ingredient_clue_when_pickups_are_unavailable()
    {
        var language = new HostLanguageService();
        var html = await RenderReport(language, concealed: false, learned: false, staged: false, history: false, discovery: true);
        Assert.Contains(language.Resource("RecipeGuide_RecordedGrantClue"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeLearning_UnlockAction"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("grant-email-subject", html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeGuide_SeenInputs", 1), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeGuide_HistoryHelp"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_report_links_to_the_selected_learning_tree_without_duplicating_source_details()
    {
        var language = new HostLanguageService();
        var html = await RenderReport(language, concealed: false, learned: false, staged: false, discovery: true, sourceSealed: true);
        Assert.Contains(language.Resource("RecipeLearning_UnlockAction"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("grant-email-subject", html, StringComparison.Ordinal);
        Assert.DoesNotContain("classified-fish-name", html, StringComparison.Ordinal);
        Assert.DoesNotContain("classified-dlc", html, StringComparison.Ordinal);
        Assert.DoesNotContain("classified-bait", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_actions_share_the_canonical_writer_and_keep_presentation_components_read_only()
    {
        var workflow = Source("PlayerRecipesTab.Workflows.razor.cs");
        var tab = Source("PlayerRecipesTab.razor");
        var report = Source("RecipeDiscoveryReport.razor");
        var crafting = Source("RecipeCraftingSteps.razor");
        var branch = Source("RecipeCraftingBranch.razor");

        Assert.Contains("SetUnlocked(row, true)", workflow, StringComparison.Ordinal);
        Assert.Contains("SetUnlocked(row, false)", workflow, StringComparison.Ordinal);
        Assert.Contains("Session.IsRecipeUnlockStaged(id)", workflow, StringComparison.Ordinal);
        Assert.Contains("Gate.ResolveWorldFlagsAsync", tab, StringComparison.Ordinal);
        Assert.Contains("Gate.CheckUnlock", tab, StringComparison.Ordinal);
        Assert.Contains("acknowledged?.IsUnlocked != unlocked", tab, StringComparison.Ordinal);
        Assert.Contains("!Concealed(row)", tab, StringComparison.Ordinal);
        Assert.Contains("if (!concealed)", report, StringComparison.Ordinal);
        foreach (var presentation in new[] { report, crafting, branch })
        {
            Assert.DoesNotContain("SetUnlockedAsync", presentation, StringComparison.Ordinal);
            Assert.DoesNotContain("SaveAsync", presentation, StringComparison.Ordinal);
            Assert.DoesNotContain("PlayerSaveWriter", presentation, StringComparison.Ordinal);
        }
        Assert.Contains("if (Node.Recipe is { } recipe && Concealed(recipe))", branch, StringComparison.Ordinal);
        Assert.Contains("Graph.BuildTree", crafting, StringComparison.Ordinal);
    }

    private static string Source(string file) => UiSource.ReadAllText("Components", "Player", file);

    private static async Task<string> RenderReport(HostLanguageService language, bool concealed, bool learned, bool staged, bool history = true, bool discovery = false, bool sourceSealed = false)
    {
        var recipe = new RecipeInfo("recipe_test", "output", 1, null, "Crafting", [new("ingredient", 1)]);
        var graph = new RecipeGraph([recipe], ["output", "ingredient"]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(language);
        services.AddSingleton<UserFacingErrorService>();
        services.AddSingleton<ItemCatalogService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<RecipeDiscoveryReport>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(RecipeDiscoveryReport.Graph)] = graph,
                [nameof(RecipeDiscoveryReport.Recipes)] = new[] { recipe },
                [nameof(RecipeDiscoveryReport.ItemName)] = (Func<string, string>)(_ => "secret-item-name"),
                [nameof(RecipeDiscoveryReport.Concealed)] = (Func<RecipeInfo, bool>)(_ => concealed),
                [nameof(RecipeDiscoveryReport.Learned)] = (Func<string, bool>)(_ => learned),
                [nameof(RecipeDiscoveryReport.Staged)] = (Func<string, bool>)(_ => staged),
                [nameof(RecipeDiscoveryReport.CanUndo)] = (Func<string, bool>)(_ => learned && staged),
                [nameof(RecipeDiscoveryReport.Feedback)] = (Func<string, string?>)(_ => null),
                [nameof(RecipeDiscoveryReport.CatalogAvailable)] = true,
                [nameof(RecipeDiscoveryReport.HasPickupHistory)] = history,
                [nameof(RecipeDiscoveryReport.HasDiscoveryHistory)] = discovery,
                [nameof(RecipeDiscoveryReport.IngredientClue)] = (Func<string, bool>)(_ => history),
                [nameof(RecipeDiscoveryReport.Assessment)] = (Func<string, RecipeDiscoveryAssessment>)(id => RecipeDiscoveryAssessment.Assess(id,
                    [new EmailEntry("Email_Labs_Test", "grant-email-subject", [], [id], [])],
                    [new FishDefinition("classified-fish-name", "classified-fish-name", false, UnlockRecipeId: id, RequiredDlcId: "classified-dlc", RequiredBaitTag: "classified-bait")],
                    [], discovery && !sourceSealed ? ["Email_Labs_Test"] : [], [])),
            }));
            return WebUtility.HtmlDecode(view.ToHtmlString());
        });
    }

    private sealed class DelayedRecipeChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public TaskCompletionSource<bool>? WriteCompletion { get; set; }
        public TaskCompletionSource<string>? NextRead { get; set; }
        public JsonElement LastWrite { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command is "recipes.get" or "codex.get")
            {
                if (NextRead is { } pending)
                {
                    NextRead = null;
                    return JsonSerializer.Deserialize<TResponse>(await pending.Task, Options)!;
                }
                var data = JsonSerializer.SerializeToElement(payload, Options);
                var player = data.GetProperty("playerId").GetString();
                if (command == "codex.get")
                    return JsonSerializer.Deserialize<TResponse>("{\"emails\":[],\"journals\":[],\"fish\":[],\"compendium\":[]}", Options)!;
                return JsonSerializer.Deserialize<TResponse>(player == "player-b"
                    ? "{\"unlockedIds\":[\"b_known\"],\"canLock\":true}"
                    : "{\"unlockedIds\":[\"a_known\"],\"canLock\":true}", Options)!;
            }
            LastWrite = JsonSerializer.SerializeToElement(payload, Options);
            if (WriteCompletion is { } completion) await completion.Task;
            return default!;
        }
    }
}
