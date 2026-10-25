namespace AbioticEditor.Core.Items;

/// <summary>Why a crafting branch has no further ingredients.</summary>
public enum RecipeTreeStop
{
    None,
    Material,
    UnknownIngredient,
    Cycle,
    NodeLimit,
    DepthLimit,
    InvalidRecipe,
}

/// <summary>
/// One occurrence in a crafting guide. Shared ingredients occur under each consuming recipe;
/// amounts are not an optimized shopping list and do not subtract current inventory.
/// Stations remain requirements on <see cref="RecipeInfo.BenchList"/>, not consumed children.
/// </summary>
public sealed record RecipeTreeNode(
    string Path,
    string ItemId,
    long RequiredCount,
    RecipeInfo? Recipe,
    long CraftCount,
    long ProducedCount,
    IReadOnlyList<RecipeTreeNode> Children,
    IReadOnlyList<RecipeInfo> Alternatives,
    RecipeTreeStop Stop);

/// <summary>
/// Read-only recipe relationships and conservative pickup-history clues. Recipe learning,
/// current stock, acquisition sources and story order are not inferred from ingredient edges.
/// </summary>
public sealed class RecipeGraph
{
    private readonly Dictionary<string, RecipeInfo> _recipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<RecipeInfo>> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownItems;
    private readonly Dictionary<string, IReadOnlyList<RecipeIngredient>> _ingredients = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalid = new(StringComparer.OrdinalIgnoreCase);

    public RecipeGraph(IEnumerable<RecipeInfo> recipes, IEnumerable<string> knownItemIds)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(knownItemIds);
        _knownItems = knownItemIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Preserve the first catalog definition and its spelling, matching the catalog's
        // precedence. Case aliases must not create duplicate routes or dictionary failures.
        foreach (var recipe in recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Id) || !_recipes.TryAdd(recipe.Id, recipe)) continue;
            var inputs = AggregateIngredients(recipe);
            _ingredients[recipe.Id] = inputs;
            if (recipe.Count <= 0 || string.IsNullOrWhiteSpace(recipe.CreatesItemId)
                || inputs.Count == 0)
                _invalid.Add(recipe.Id);
        }

        foreach (var group in _recipes.Values.Where(recipe => !string.IsNullOrWhiteSpace(recipe.CreatesItemId))
                     .GroupBy(recipe => recipe.CreatesItemId!, StringComparer.OrdinalIgnoreCase))
            _outputs[group.Key] = Array.AsReadOnly(group.ToArray());
    }

    /// <summary>Distinct producing recipes in catalog order. Alternatives are choices, not joint requirements.</summary>
    public IReadOnlyList<RecipeInfo> RecipesProducing(string itemId)
        => _outputs.TryGetValue(itemId, out var alternatives) ? alternatives : Array.Empty<RecipeInfo>();

    /// <summary>
    /// Inputs summed by case-insensitive item identity, retaining the first spelling and order.
    /// Empty or invalid input lists return no inputs and are marked InvalidRecipe in a guide.
    /// </summary>
    public IReadOnlyList<RecipeIngredient> IngredientsFor(RecipeInfo recipe)
        => _ingredients.TryGetValue(recipe.Id, out var ingredients)
            ? ingredients
            : AggregateIngredients(recipe);

    /// <summary>
    /// Unlearned recipes whose concrete ingredient types all appear in this player's pickup
    /// history. These are clues to investigate, not proof of a missed unlock or current stock.
    /// Null history means unavailable. Re-evaluating with staged learned ids updates the report.
    /// </summary>
    public IReadOnlyList<RecipeInfo> FindPossibleMissed(IEnumerable<string> learned, IEnumerable<string>? itemsPickedUp)
    {
        ArgumentNullException.ThrowIfNull(learned);
        if (itemsPickedUp is null) return Array.Empty<RecipeInfo>();
        var learnedIds = learned.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = itemsPickedUp.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(_recipes.Values.Where(recipe =>
                !_invalid.Contains(recipe.Id) && !learnedIds.Contains(recipe.Id)
                && _knownItems.Contains(recipe.CreatesItemId!)
                && _ingredients[recipe.Id].All(input => IsConcrete(input.ItemId) && seen.Contains(input.ItemId)))
            .ToArray());
    }

    /// <summary>
    /// Builds a bounded occurrence tree for the requested recipe. Route choices map stable paths
    /// (root, root.0, root.0.1) to recipe ids; only a recipe producing that branch's item is valid.
    /// Invalid choices fall back to the catalog's first route. The requested root recipe wins
    /// over catalog order unless an explicit valid root choice was supplied.
    /// </summary>
    public RecipeTreeNode BuildTree(
        string recipeId,
        long requiredCount = 1,
        IReadOnlyDictionary<string, string>? routeChoices = null,
        int maxNodes = 300,
        int maxDepth = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        if (!_recipes.TryGetValue(recipeId, out var root))
            return new RecipeTreeNode("root", recipeId, Math.Max(0, requiredCount), null, 0, 0,
                Array.Empty<RecipeTreeNode>(), Array.Empty<RecipeInfo>(), RecipeTreeStop.InvalidRecipe);

        var remaining = maxNodes;
        return Build(root.CreatesItemId ?? root.Id, requiredCount, "root", 0,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), root);

        RecipeTreeNode Build(string itemId, long needed, string path, int depth, HashSet<string> ancestors,
            RecipeInfo? requested = null)
        {
            remaining--;
            var alternatives = RecipesProducing(itemId);
            var recipe = requested ?? (alternatives.Count > 0 ? alternatives[0] : null);
            if (routeChoices is not null && routeChoices.TryGetValue(path, out var choice))
                recipe = alternatives.FirstOrDefault(candidate => string.Equals(candidate.Id, choice,
                    StringComparison.OrdinalIgnoreCase)) ?? recipe;

            RecipeTreeNode Stop(RecipeTreeStop stop, long crafts = 0, long produced = 0)
                => new(path, itemId, Math.Max(0, needed), recipe, crafts, produced,
                    Array.Empty<RecipeTreeNode>(), alternatives, stop);

            if (needed <= 0) return Stop(RecipeTreeStop.InvalidRecipe);
            if (!IsConcrete(itemId)) return Stop(RecipeTreeStop.UnknownIngredient);
            if (recipe is null) return Stop(RecipeTreeStop.Material);
            if (_invalid.Contains(recipe.Id)) return Stop(RecipeTreeStop.InvalidRecipe);
            if (ancestors.Contains(itemId)) return Stop(RecipeTreeStop.Cycle);

            var crafts = needed / recipe.Count + (needed % recipe.Count == 0 ? 0 : 1);
            long produced;
            var inputs = _ingredients[recipe.Id];
            try
            {
                produced = checked(crafts * recipe.Count);
                // Validate every quantity before returning a partially plausible branch.
                foreach (var input in inputs) _ = checked(input.Count * crafts);
            }
            catch (OverflowException)
            {
                return Stop(RecipeTreeStop.InvalidRecipe);
            }

            if (depth >= maxDepth) return Stop(RecipeTreeStop.DepthLimit, crafts, produced);
            if (remaining == 0) return Stop(RecipeTreeStop.NodeLimit, crafts, produced);

            var children = new List<RecipeTreeNode>(Math.Min(inputs.Count, remaining));
            var next = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { itemId };
            var stopReason = RecipeTreeStop.None;
            for (var index = 0; index < inputs.Count; index++)
            {
                if (remaining == 0)
                {
                    stopReason = RecipeTreeStop.NodeLimit;
                    break;
                }
                var input = inputs[index];
                children.Add(Build(input.ItemId, input.Count * crafts, $"{path}.{index}", depth + 1, next));
            }
            return new RecipeTreeNode(path, itemId, needed, recipe, crafts, produced,
                children.AsReadOnly(), alternatives, stopReason);
        }
    }

    private bool IsConcrete(string itemId)
        // Requirement-group rows are not concrete substitutes, even if a mod also gives
        // the group itself a display row. Membership needs its own verified data.
        => _knownItems.Contains(itemId) && !itemId.StartsWith("Any", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<RecipeIngredient> AggregateIngredients(RecipeInfo recipe)
    {
        var totals = new Dictionary<string, RecipeIngredient>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in recipe.IngredientList)
        {
            if (string.IsNullOrWhiteSpace(input.ItemId) || input.Count <= 0)
                return Array.Empty<RecipeIngredient>();
            if (!totals.TryGetValue(input.ItemId, out var original))
            {
                totals.Add(input.ItemId, input);
                continue;
            }
            try { totals[input.ItemId] = original with { Count = checked(original.Count + input.Count) }; }
            catch (OverflowException) { return Array.Empty<RecipeIngredient>(); }
        }
        return Array.AsReadOnly(totals.Values.ToArray());
    }
}
