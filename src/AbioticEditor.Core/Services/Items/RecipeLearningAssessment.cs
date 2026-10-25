namespace AbioticEditor.Core.Items;

public sealed record RecipeLearningInputAssessment(RecipeLearningInput Input, RecipeDiscoveryState State);
public sealed record RecipeLinkedLearningAssessment(string SourceRecipeId, RecipeDiscoveryState State);
public sealed record RecipeSkillLearningAssessment(RecipeSkillGrant Grant, RecipeDiscoveryState State);

/// <summary>
/// Pure evidence for known learning routes. A met discovery route is not a claim of current
/// crafting stock or completed research. Explicit special grants are assessed separately.
/// </summary>
public sealed record RecipeLearningAssessment(string RecipeId, RecipeLearningDefinition? Definition,
    IReadOnlyList<RecipeLearningInputAssessment> Inputs, RecipeDiscoveryState PickupIngredientsState,
    RecipeDiscoveryState OutputPickupState, RecipeDiscoveryState StartingState,
    IReadOnlyList<RecipeLinkedLearningAssessment> LinkedSources, IReadOnlyList<RecipeSkillLearningAssessment> SkillSources,
    bool? IsDiscovered, bool? IsResearchPending)
{
    /// <summary>Knowledge needed to craft, independent of possession of materials or stations.</summary>
    public bool? RecipeKnowledgeAvailable => IsDiscovered switch
    {
        false => false,
        true when IsResearchPending == true => false,
        true when IsResearchPending == false || Definition?.RequiresResearch == false => true,
        _ => null,
    };

    /// <summary>OR over these cataloged routes only, not an exhaustive unlock requirement.</summary>
    public RecipeDiscoveryState KnownRouteState => CombineAny(
        new[] { PickupIngredientsState, OutputPickupState, StartingState }
            .Concat(LinkedSources.Select(source => source.State)).Concat(SkillSources.Select(source => source.State)));

    public static RecipeLearningAssessment Assess(RecipeLearningCatalogData? catalog, string recipeId,
        IEnumerable<string>? itemsPickedUp = null, IEnumerable<string>? discoveredRecipeIds = null,
        IEnumerable<string>? researchPendingIds = null, IReadOnlyDictionary<string, int>? skillLevels = null)
    {
        var pickedUp = AsSet(itemsPickedUp);
        var discovered = AsSet(discoveredRecipeIds);
        var pending = AsSet(researchPendingIds);
        var compatible = RecipeLearningCatalog.Validated(catalog);
        var definition = compatible?.Recipes.FirstOrDefault(recipe => recipe.RecipeId.Equals(recipeId, StringComparison.OrdinalIgnoreCase));
        var inputs = definition?.DiscoveryInputs.Select(input => new RecipeLearningInputAssessment(input,
            !input.IsResolved || input.AlternativeItemIds.Count == 0 || pickedUp is null ? RecipeDiscoveryState.Unknown
                : input.AlternativeItemIds.Any(pickedUp.Contains) ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet)).ToArray() ?? [];
        var ingredients = definition is { PickupDiscoveryEnabled: true, DiscoveryInputsKnown: true } && pickedUp is not null
            ? CombineAll(inputs.Select(input => input.State)) : RecipeDiscoveryState.Unknown;
        var output = definition is { OutputPickupDiscoveryEnabled: true, OutputItemId: { Length: > 0 } outputId }
            ? Recorded(outputId, pickedUp) : RecipeDiscoveryState.Unknown;
        var starting = compatible?.StartingRecipesKnown == true
            ? compatible.StartingRecipeIds.Contains(recipeId, StringComparer.OrdinalIgnoreCase) ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet
            : RecipeDiscoveryState.Unknown;
        var linked = compatible?.Recipes.Where(recipe => recipe.LinkedRecipeIds.Contains(recipeId, StringComparer.OrdinalIgnoreCase))
            .Select(recipe => new RecipeLinkedLearningAssessment(recipe.RecipeId, Recorded(recipe.RecipeId, discovered))).ToArray() ?? [];
        Dictionary<string, int>? levels = null;
        if (skillLevels is not null)
        {
            levels = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (skillId, level) in skillLevels) levels.TryAdd(skillId, level);
        }
        var skills = compatible?.SkillGrants.Where(grant => grant.RecipeId.Equals(recipeId, StringComparison.OrdinalIgnoreCase))
            .Select(grant => new RecipeSkillLearningAssessment(grant, levels is not null && levels.TryGetValue(grant.SkillId, out var level)
                ? level >= grant.Level ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet : RecipeDiscoveryState.Unknown)).ToArray() ?? [];
        return new(recipeId, definition, inputs, ingredients, output, starting, linked, skills,
            discovered?.Contains(recipeId), pending?.Contains(recipeId));
    }

    private static HashSet<string>? AsSet(IEnumerable<string>? values) => values?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static RecipeDiscoveryState Recorded(string id, HashSet<string>? history)
        => history is null ? RecipeDiscoveryState.Unknown : history.Contains(id) ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet;
    private static RecipeDiscoveryState CombineAll(IEnumerable<RecipeDiscoveryState> states)
    {
        var results = states.ToArray();
        return results.Contains(RecipeDiscoveryState.KnownUnmet) ? RecipeDiscoveryState.KnownUnmet
            : results.Contains(RecipeDiscoveryState.Unknown) ? RecipeDiscoveryState.Unknown : RecipeDiscoveryState.KnownMet;
    }
    private static RecipeDiscoveryState CombineAny(IEnumerable<RecipeDiscoveryState> states)
    {
        var results = states.ToArray();
        return results.Contains(RecipeDiscoveryState.KnownMet) ? RecipeDiscoveryState.KnownMet
            : results.Length == 0 || results.Contains(RecipeDiscoveryState.Unknown) ? RecipeDiscoveryState.Unknown : RecipeDiscoveryState.KnownUnmet;
    }
}
