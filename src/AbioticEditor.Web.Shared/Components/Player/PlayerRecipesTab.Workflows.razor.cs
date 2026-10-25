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
    private readonly HashSet<string> _newUnlockIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _recipeFeedback = new(StringComparer.OrdinalIgnoreCase);
    private bool _recipeWriteBusy;
    private bool _checkingRecipes;
    private string? _boundRecipeIdentity;

    private IPlayerRecipesSession RecipeSource => Session is LivePlayerEditorSession live ? live.RecipesSession ?? Session : Session;

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
            var candidates = _recipeGraph.FindPossibleMissed(Session.Recipes.Where(row => row.IsUnlocked).Select(row => row.Id), Session.RecipeItemsPickedUp);
            var currentIds = candidates.Select(recipe => recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _reviewRecipeIds.RemoveAll(id => !currentIds.Contains(id) && !(_newUnlockIds.Contains(id) && IsLearned(id)));
            foreach (var recipe in candidates.OrderBy(recipe => RecipeName(recipe.Id), StringComparer.CurrentCultureIgnoreCase))
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
            var flags = await Gate.ResolveWorldFlagsAsync(SessionPathOrNull(session));
            if (!ReferenceEquals(session, Session) || !ReferenceEquals(source, RecipeSource) || !string.Equals(targetIdentity, source.RecipeTargetIdentity, StringComparison.Ordinal)) return;
            _worldFlags = flags;
            _reviewRecipeIds.Clear();
            _workflow = RecipeWorkflow.Check;
        }
        finally { _checkingRecipes = false; }
    }

    private void OpenCrafting(string recipeId)
    {
        if (RowFor(recipeId) is not { } row) return;
        if (Concealed(row)) { RequestReveal(row, () => OpenCrafting(recipeId)); return; }
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
        _newUnlockIds.Clear();
        _recipeFeedback.Clear();
    }
}
