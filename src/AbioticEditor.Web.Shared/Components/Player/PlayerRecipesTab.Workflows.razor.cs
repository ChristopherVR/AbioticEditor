using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using AbioticEditor.Web.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Components.Player;

public partial class PlayerRecipesTab
{
    private enum RecipeWorkflow { Book, Check, Crafting }
    private RecipeWorkflow _workflow;
    private RecipeWorkflow _craftingReturn;
    private RecipeInfo? _craftingRecipe;
    private RecipeGraph _recipeGraph = new([], []);
    private readonly List<string> _reviewRecipeIds = [];
    private HashSet<string> _ingredientClueIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _newUnlockIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _recipeFeedback = new(StringComparer.OrdinalIgnoreCase);
    private bool _recipeWriteBusy;
    private bool _checkingRecipes;
    private string? _boundRecipeIdentity;
    private RecipeCatalogSnapshot? _catalogSnapshot;
    private bool _catalogRefreshing;
    private long _catalogLoadGeneration;
    private bool _bindingLoading;

    private IPlayerRecipesSession RecipeSource => Session is LivePlayerEditorSession live ? live.RecipesSession ?? Session : Session;

    // A live facade's empty fallback arrays are not evidence that the player has read/caught nothing.
    private IPlayerCodexSession? DiscoverySession => Session is LivePlayerEditorSession live
        ? live.RecipesSession is LivePlayerRecipesSession recipes && live.CodexSession is LivePlayerCodexSession codex
            && codex.HasCurrentHistory && string.Equals(recipes.SelectedPlayerId, codex.SelectedPlayerId, StringComparison.OrdinalIgnoreCase) ? codex : null
        : Session as IPlayerCodexSession;
    private bool HasDiscoveryHistory => DiscoverySession is not null;
    private IReadOnlySet<string>? ReadEmails => DiscoverySession?.Emails.Where(row => row.IsKnown).Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string>? CaughtFish => DiscoverySession?.Fish.Where(row => row.IsKnown).Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private RecipeDiscoveryAssessment AssessRecipe(string id)
        => RecipeDiscoveryAssessment.Assess(id, _catalogSnapshot?.Codex.Emails ?? [], _catalogSnapshot?.Codex.Fish ?? [], _worldFlags, ReadEmails, CaughtFish);

    private bool ConcealedSource(RecipeDiscoveryRoute route)
        => Spoilers.Enabled && route.RecordedSourceState != RecipeDiscoveryState.KnownMet && !Spoilers.IsRevealed(DiscoverySpoilerKey(route));

    private static string DiscoverySpoilerKey(RecipeDiscoveryRoute route) => $"recipe-source:{route.Kind}:{route.SourceId}";

    private void RevealSource(RecipeDiscoveryRoute route) => Modals.Show(new ModalRequest(
        L.Resource("Spoiler_OverrideTitle"), Paragraph(L.Resource("Spoiler_OverrideMessage", L.Resource("RecipeGuide_TheseDiscoveryDetails"))),
        ConfirmText: L.Resource("Spoiler_OverrideAccept"),
        OnConfirm: async () => { Spoilers.Reveal(DiscoverySpoilerKey(route)); ShowOwnDetail(); await InvokeAsync(StateHasChanged); },
        CancelText: L.Resource("Spoiler_OverrideKeepSealed")));

    private bool HasIngredientClue(string id)
        => _ingredientClueIds.Contains(id);

    private async Task<bool> RefreshCatalogAsync()
    {
        if (_catalogRefreshing) return false;
        var session = Session;
        var source = RecipeSource;
        var targetIdentity = source.RecipeTargetIdentity;
        var generation = ++_catalogLoadGeneration;
        _catalogRefreshing = true;
        try
        {
            var snapshot = await Task.Run(() => RecipeVocabulary.EnsureCurrent(Items, CodexVocabulary));
            if (generation != _catalogLoadGeneration || !ReferenceEquals(session, Session) || !ReferenceEquals(source, RecipeSource) || !string.Equals(targetIdentity, source.RecipeTargetIdentity, StringComparison.Ordinal)) return false;
            if (_catalogSnapshot?.Metadata.Revision != snapshot.Metadata.Revision) ApplyCatalog(snapshot);
            else _catalogSnapshot = snapshot;
            return true;
        }
        finally { if (generation == _catalogLoadGeneration) _catalogRefreshing = false; }
    }

    private async Task RefreshRecipeData()
    {
        if (_recipeWriteBusy || _checkingRecipes) return;
        if (!await RefreshCatalogAsync()) return;
        var session = Session;
        var source = RecipeSource;
        var targetIdentity = source.RecipeTargetIdentity;
        var flags = await Gate.ResolveWorldFlagsAsync(SessionPathOrNull(session));
        if (!ReferenceEquals(session, Session) || !ReferenceEquals(source, RecipeSource) || !string.Equals(targetIdentity, source.RecipeTargetIdentity, StringComparison.Ordinal)) return;
        _worldFlags = flags;
        ShowOwnDetail();
    }

    private bool IsLearned(string id)
        => Session.Recipes.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))?.IsUnlocked == true;

    private RecipeRow? RowFor(string id)
        => _rows?.FirstOrDefault(row => string.Equals(row.Info.Id, id, StringComparison.OrdinalIgnoreCase));

    private bool ConcealedInfo(RecipeInfo recipe) => RowFor(recipe.Id) is { } row && Concealed(row);

    private string CraftItemName(string itemId)
    {
        // A hidden producing recipe seals its branch, including item labels used by route choices.
        var producers = _recipeGraph.RecipesProducing(itemId);
        if (producers.Count > 0 && producers.All(ConcealedInfo)) return L.Resource("Spoiler_Redacted");
        return Items.Find(itemId)?.DisplayName ?? PlainNames.Words(itemId);
    }

    private string CraftRouteName(RecipeInfo recipe) => ConcealedInfo(recipe)
        ? L.Resource("Spoiler_Redacted")
        : string.Join(" + ", _recipeGraph.IngredientsFor(recipe).Select(input => $"{input.Count} × {CraftItemName(input.ItemId)}"));

    private IReadOnlyList<RecipeInfo> ReviewRecipes
    {
        get
        {
            var ingredientRecipes = _recipeGraph.FindPossibleMissed([], Session.RecipeItemsPickedUp);
            _ingredientClueIds = ingredientRecipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = ingredientRecipes.Where(recipe => !IsLearned(recipe.Id));
            var discovered = _catalogSnapshot is { } snapshot
                ? RecipeDiscoveryAssessment.FindRecordedMissing(snapshot.Recipes.Select(recipe => recipe.Id), Session.Recipes.Where(row => row.IsUnlocked).Select(row => row.Id), snapshot.Codex.Emails, snapshot.Codex.Fish, ReadEmails, CaughtFish, _worldFlags)
                : [];
            var currentIds = candidates.Select(recipe => recipe.Id).Concat(discovered.Select(assessment => assessment.RecipeId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _reviewRecipeIds.RemoveAll(id => !currentIds.Contains(id) && !(_newUnlockIds.Contains(id) && IsLearned(id)));
            foreach (var recipe in (_catalogSnapshot?.Recipes ?? []).Where(recipe => currentIds.Contains(recipe.Id)).OrderBy(recipe => RecipeName(recipe.Id), StringComparer.CurrentCultureIgnoreCase))
                if (!_reviewRecipeIds.Contains(recipe.Id, StringComparer.OrdinalIgnoreCase)) _reviewRecipeIds.Add(recipe.Id);
            return _reviewRecipeIds.Select(RowFor).Where(row => row is not null).Select(row => row!.Info).ToArray();
        }
    }

    private async Task CheckRecipes()
    {
        if (_checkingRecipes || _recipeWriteBusy) return;
        var session = Session;
        var source = RecipeSource;
        var targetIdentity = source.RecipeTargetIdentity;
        _checkingRecipes = true;
        try
        {
            if (!await RefreshCatalogAsync()) return;
            var flags = await Gate.ResolveWorldFlagsAsync(SessionPathOrNull(session));
            if (!ReferenceEquals(session, Session) || !ReferenceEquals(source, RecipeSource) || !string.Equals(targetIdentity, source.RecipeTargetIdentity, StringComparison.Ordinal)) return;
            _worldFlags = flags;
            _reviewRecipeIds.Clear();
            _workflow = RecipeWorkflow.Check;
        }
        finally { _checkingRecipes = false; }
    }

    private async Task OpenCrafting(string recipeId)
    {
        if (!await RefreshCatalogAsync()) return;
        if (RowFor(recipeId) is not { } row) return;
        if (Concealed(row)) { RequestRevealAfter(row, () => OpenCrafting(recipeId)); return; }
        _craftingReturn = _workflow == RecipeWorkflow.Crafting ? _craftingReturn : _workflow;
        _craftingRecipe = row.Info;
        _workflow = RecipeWorkflow.Crafting;
        _selected = row;
        ShowOwnDetail();
    }

    private void InspectCraftItem(RecipeCraftingSelection selection)
    {
        var itemId = selection.ItemId;
        var producers = _recipeGraph.RecipesProducing(itemId);
        var recipe = selection.RecipeId is { } selectedId && RowFor(selectedId) is { } selectedRoute
            ? selectedRoute.Info
            : producers.FirstOrDefault(info => !ConcealedInfo(info))
            ?? (producers.Count > 0 ? producers[0] : null);
        if (recipe is not null && RowFor(recipe.Id) is { } row)
        {
            if (Concealed(row)) { RequestReveal(row, () => InspectCraftItem(selection)); return; }
            _selected = row;
            ShowOwnDetail();
            return;
        }
        _selected = null;
        Selection.ShowDetail(DetailKeyPrefix + "material:" + itemId, builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "detail-card detail-card--terminal");
            builder.OpenElement(2, "strong");
            builder.AddContent(3, CraftItemName(itemId));
            builder.CloseElement();
            builder.OpenElement(4, "p");
            builder.AddAttribute(5, "class", "detail-value");
            builder.AddContent(6, L.Resource("RecipeGuide_UnknownRecipe"));
            builder.CloseElement();
            builder.CloseElement();
        });
    }

    private void RevealRecipe(string recipeId)
    {
        if (RowFor(recipeId) is { } row) RequestReveal(row);
    }

    private void BackToBook() => _workflow = RecipeWorkflow.Book;
    private void BackFromCrafting() => _workflow = _craftingReturn;
    private void ShowAllUnlearned()
    {
        _workflow = RecipeWorkflow.Book;
        _filter = Show.Locked;
        _selectedCategoryKey = null;
        Search = string.Empty;
    }

    private bool CanUndoRecipe(string id)
        => !Session.AppliesImmediately && Session.CanLock && _newUnlockIds.Contains(id) && IsLearned(id) && Session.IsRecipeUnlockStaged(id);

    private string? RecipeFeedback(string id) => _recipeFeedback.GetValueOrDefault(id);

    private Task UnlockRecipe(string id) => RowFor(id) is { } row ? SetUnlocked(row, true) : Task.CompletedTask;
    private Task UndoRecipe(string id) => CanUndoRecipe(id) && RowFor(id) is { } row ? SetUnlocked(row, false) : Task.CompletedTask;

    private void ResetWorkflow()
    {
        _workflow = RecipeWorkflow.Book;
        _craftingRecipe = null;
        _reviewRecipeIds.Clear();
        _ingredientClueIds.Clear();
        _newUnlockIds.Clear();
        _recipeFeedback.Clear();
    }
}
