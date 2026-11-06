namespace AbioticEditor.Core.Items;

/// <summary>
/// One recipe's pickup-discovery condition: every input needs at least one of its alternative
/// item types in the player's history. An empty input list is met by any known history.
/// </summary>
public sealed record RecipePickupRule(string RecipeId, IReadOnlyList<IReadOnlyList<string>> Inputs);

/// <summary>
/// An item type the player has not picked up but could make with a recipe. <see cref="Inferred"/>
/// is false when the player already knows <see cref="RecipeId"/>, and true when that recipe is
/// itself only a missed-recipe suggestion (its own pickup condition is met).
/// </summary>
public sealed record RecipeCraftableItem(string ItemId, string RecipeId, bool Inferred);

/// <summary>
/// Pickup-discovery evidence widened by crafting knowledge. The game learns a recipe when every
/// input type has been picked up, and crafting an item then picking it up records it, so an
/// item the player can craft is treated as obtainable even if it was never made. The expansion
/// iterates to a fixed point because a newly suggested recipe can make another input craftable.
/// These remain clues, not proof of a missed unlock.
/// </summary>
public sealed class RecipePickupInference
{
    private static readonly IReadOnlyList<RecipeCraftableItem> NoCraftedInputs = Array.Empty<RecipeCraftableItem>();
    private readonly HashSet<string> _met;
    private readonly Dictionary<string, IReadOnlyList<RecipeCraftableItem>> _craftedInputs;

    private RecipePickupInference(HashSet<string> met, Dictionary<string, IReadOnlyList<RecipeCraftableItem>> craftedInputs,
        Dictionary<string, RecipeCraftableItem> craftable)
    {
        _met = met;
        _craftedInputs = craftedInputs;
        CraftableItems = craftable;
    }

    /// <summary>An inference with no evidence (pickup history unavailable).</summary>
    public static RecipePickupInference Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase),
        new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Items not in the pickup history that count as obtainable, keyed by item id.</summary>
    public IReadOnlyDictionary<string, RecipeCraftableItem> CraftableItems { get; }

    /// <summary>Recipe ids whose pickup condition is met once craftable items are included.</summary>
    public IReadOnlyCollection<string> MetRecipeIds => _met;

    /// <summary>Whether this recipe's pickup condition is met, directly or through craftable inputs.</summary>
    public bool IsMet(string recipeId) => _met.Contains(recipeId);

    /// <summary>
    /// Inputs that were satisfied only because the player can craft them. Empty when every input
    /// was actually picked up, or when the recipe's condition is not met.
    /// </summary>
    public IReadOnlyList<RecipeCraftableItem> CraftedInputsFor(string recipeId)
        => _craftedInputs.TryGetValue(recipeId, out var inputs) ? inputs : NoCraftedInputs;

    /// <summary>
    /// Pickup rules from the verified learning catalog, mirroring
    /// <see cref="RecipeLearningAssessment"/>: only pickup-enabled rows with known, resolved inputs.
    /// </summary>
    public static IReadOnlyList<RecipePickupRule> RulesFrom(RecipeLearningCatalogData? catalog)
    {
        if (RecipeLearningCatalog.Validated(catalog) is not { } compatible) return [];
        var rules = new List<RecipePickupRule>(compatible.Recipes.Count);
        foreach (var definition in compatible.Recipes)
        {
            if (definition is not { PickupDiscoveryEnabled: true, DiscoveryInputsKnown: true }) continue;
            if (definition.DiscoveryInputs.Any(input => !input.IsResolved || input.AlternativeItemIds.Count == 0)) continue;
            rules.Add(new(definition.RecipeId, definition.DiscoveryInputs.Select(input => input.AlternativeItemIds).ToArray()));
        }
        return rules;
    }

    /// <summary>
    /// Evaluates <paramref name="rules"/> against the pickup history plus the outputs of
    /// <paramref name="craftableRecipeIds"/> (recipes the player knows and can craft now), then
    /// repeatedly adds the outputs of newly met, not-yet-known recipes until nothing changes.
    /// Null history means unavailable and yields <see cref="Empty"/>.
    /// </summary>
    public static RecipePickupInference Infer(IEnumerable<RecipePickupRule> rules, IEnumerable<RecipeInfo> recipes,
        IEnumerable<string>? itemsPickedUp, IEnumerable<string> craftableRecipeIds)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(craftableRecipeIds);
        if (itemsPickedUp is null) return Empty;

        var pickedUp = itemsPickedUp.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in recipes)
            if (!string.IsNullOrWhiteSpace(recipe.Id) && !string.IsNullOrWhiteSpace(recipe.CreatesItemId))
                outputs.TryAdd(recipe.Id, recipe.CreatesItemId);

        var craftable = new Dictionary<string, RecipeCraftableItem>(StringComparer.OrdinalIgnoreCase);
        var known = craftableRecipeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canonicalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in recipes)
            if (!string.IsNullOrWhiteSpace(recipe.Id)) canonicalIds.TryAdd(recipe.Id, recipe.Id);
        foreach (var recipeId in known)
            if (outputs.TryGetValue(recipeId, out var item) && !pickedUp.Contains(item))
                craftable.TryAdd(item, new(item, canonicalIds[recipeId], false));

        bool Obtainable(string item) => pickedUp.Contains(item) || craftable.ContainsKey(item);

        // Deduplicate by id (first rule wins, matching catalog precedence) and keep catalog order.
        var pending = new List<RecipePickupRule>();
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            if (rule is not null && !string.IsNullOrWhiteSpace(rule.RecipeId) && ruleIds.Add(rule.RecipeId)) pending.Add(rule);

        var met = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metRules = new List<RecipePickupRule>();
        bool changed;
        do
        {
            changed = false;
            for (var index = 0; index < pending.Count; index++)
            {
                var rule = pending[index];
                if (!rule.Inputs.All(alternatives => alternatives.Any(Obtainable))) continue;
                met.Add(rule.RecipeId);
                metRules.Add(rule);
                pending.RemoveAt(index--);
                // A suggested recipe would have been learned in game, so its output can feed others.
                if (!known.Contains(rule.RecipeId) && outputs.TryGetValue(rule.RecipeId, out var item)
                    && !Obtainable(item))
                {
                    craftable.Add(item, new(item, rule.RecipeId, true));
                    changed = true;
                }
            }
        }
        while (changed && pending.Count > 0);

        var craftedInputs = new Dictionary<string, IReadOnlyList<RecipeCraftableItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in metRules)
        {
            List<RecipeCraftableItem>? viaCrafting = null;
            foreach (var alternatives in rule.Inputs)
            {
                if (alternatives.Any(pickedUp.Contains)) continue;
                // Prefer a recipe the player already knows over a chained suggestion.
                var source = alternatives.Select(item => craftable.GetValueOrDefault(item))
                    .Where(candidate => candidate is not null && !candidate.RecipeId.Equals(rule.RecipeId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate!.Inferred).First()!;
                if (viaCrafting?.Any(existing => existing.ItemId.Equals(source.ItemId, StringComparison.OrdinalIgnoreCase)) == true) continue;
                (viaCrafting ??= []).Add(source);
            }
            if (viaCrafting is not null) craftedInputs[rule.RecipeId] = viaCrafting.AsReadOnly();
        }
        return new(met, craftedInputs, craftable);
    }
}
