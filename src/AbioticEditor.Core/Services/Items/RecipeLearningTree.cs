namespace AbioticEditor.Core.Items;

public enum RecipeLearningNodeKind
{
    Recipe, Discovery, PickupAll, PickupAny, PickupItem, Skill, Starting, Special,
    Email, Fish, Research, Unknown, Cycle, DepthLimit, NodeLimit,
}

/// <summary>A selected recipe's learning route. Labels and spoiler reveals belong to the caller.</summary>
public sealed record RecipeLearningNode(string Path, RecipeLearningNodeKind Kind, string RecipeId,
    IReadOnlyList<RecipeLearningNode> Children)
{
    public string? ItemId { get; init; }
    public RecipeLearningAssessment? Assessment { get; init; }
    public RecipeLearningInputAssessment? Input { get; init; }
    public RecipeSkillLearningAssessment? Skill { get; init; }
    public RecipeSpecialGrant? Special { get; init; }
    public RecipeDiscoveryRoute? Source { get; init; }
    public bool IsRoot { get; init; }
    public bool Concealed { get; init; }
    public RecipeDiscoveryState State { get; init; }
}

/// <summary>
/// Builds only the selected recipe and its linked discovery predecessors. Discovery methods
/// are alternatives; an all-input pickup method contains one required condition per item/group.
/// Research is a separate root phase, never a prerequisite fabricated for linked discovery.
/// </summary>
public static class RecipeLearningTree
{
    public static RecipeLearningNode Build(string recipeId, RecipeLearningCatalogData? catalog,
        Func<string, RecipeLearningAssessment> assess, Func<string, RecipeDiscoveryAssessment> discovery,
        Func<string, bool>? recipeConcealed = null, Func<RecipeSpecialGrant, bool>? specialConcealed = null,
        Func<RecipeDiscoveryRoute, bool>? sourceConcealed = null,
        Func<string, RecipeDiscoveryState>? pickupState = null, int maxDepth = 8, int maxNodes = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        return new Builder(RecipeLearningCatalog.Validated(catalog), assess, discovery,
            recipeConcealed, specialConcealed, sourceConcealed, pickupState, maxDepth, maxNodes)
            .Recipe(recipeId, "root", 0, true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class Builder(RecipeLearningCatalogData? catalog,
        Func<string, RecipeLearningAssessment> assess, Func<string, RecipeDiscoveryAssessment> discovery,
        Func<string, bool>? recipeConcealed, Func<RecipeSpecialGrant, bool>? specialConcealed,
        Func<RecipeDiscoveryRoute, bool>? sourceConcealed, Func<string, RecipeDiscoveryState>? pickupState,
        int maxDepth, int maxNodes)
    {
        private int _count;
        private int _activeLimit = maxNodes;
        private bool Full => _count >= _activeLimit;

        private RecipeLearningNode Make(string path, RecipeLearningNodeKind kind, string id, int depth,
            Func<RecipeLearningNode> content, bool expandable = false, bool allowLast = false)
        {
            _count++;
            if (!(allowLast && kind == RecipeLearningNodeKind.Research) && _count == _activeLimit)
                return new(path, RecipeLearningNodeKind.NodeLimit, id, []);
            if (depth > maxDepth || (depth == maxDepth && expandable)) return new(path, RecipeLearningNodeKind.DepthLimit, id, []);
            return content();
        }

        public RecipeLearningNode Recipe(string id, string path, int depth, bool isRoot,
            HashSet<string> ancestors, RecipeDiscoveryState? linkedState = null)
            => Make(path, RecipeLearningNodeKind.Recipe, id, depth, () =>
            {
                if (recipeConcealed?.Invoke(id) == true)
                    return new(path, RecipeLearningNodeKind.Recipe, id, []) { Concealed = true };
                if (ancestors.Contains(id)) return new(path, RecipeLearningNodeKind.Cycle, id, []);
                var assessment = assess(id);
                var nextAncestors = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { id };
                var children = new List<RecipeLearningNode>();
                // A wide first phase cannot consume the root's separate research phase.
                var previousLimit = _activeLimit;
                if (isRoot && previousLimit >= 3) _activeLimit = previousLimit - 1;
                try
                {
                    if (!Full) children.Add(Discovery(id, path + ".0", depth + 1, assessment, nextAncestors));
                }
                finally { _activeLimit = previousLimit; }
                if (isRoot && !Full)
                    children.Add(Make(path + ".1", RecipeLearningNodeKind.Research, id, depth + 1,
                        () => new(path + ".1", RecipeLearningNodeKind.Research, id, [])
                        {
                            Assessment = assessment,
                            State = ResearchState(assessment),
                        }, allowLast: true));
                return new(path, RecipeLearningNodeKind.Recipe, id, children.AsReadOnly())
                {
                    Assessment = assessment,
                    ItemId = assessment.Definition?.OutputItemId,
                    State = linkedState ?? (assessment.IsDiscovered is true ? RecipeDiscoveryState.KnownMet
                        : assessment.IsDiscovered is false ? RecipeDiscoveryState.KnownUnmet : RecipeDiscoveryState.Unknown),
                };
            }, expandable: true) with { IsRoot = isRoot };

        private RecipeLearningNode Discovery(string id, string path, int depth, RecipeLearningAssessment assessment,
            HashSet<string> ancestors) => Make(path, RecipeLearningNodeKind.Discovery, id, depth, () =>
        {
            var methods = new List<RecipeLearningNode>();
            void Add(Func<string, RecipeLearningNode> create)
            {
                if (!Full) methods.Add(create(path + "." + methods.Count));
            }
            if (assessment.Definition is { PickupDiscoveryEnabled: true })
                Add(p => PickupInputs(id, p, depth + 1, assessment));
            if (assessment.Definition is { OutputPickupDiscoveryEnabled: true, OutputItemId: { Length: > 0 } itemId })
                Add(p => Make(p, RecipeLearningNodeKind.PickupItem, id, depth + 1,
                    () => new(p, RecipeLearningNodeKind.PickupItem, id, [])
                    { ItemId = itemId, Assessment = assessment, State = assessment.OutputPickupState }));
            if (assessment.StartingState == RecipeDiscoveryState.KnownMet)
                Add(p => Make(p, RecipeLearningNodeKind.Starting, id, depth + 1,
                    () => new(p, RecipeLearningNodeKind.Starting, id, []) { State = assessment.StartingState }));

            var skills = new HashSet<(string, int)>();
            foreach (var skill in assessment.SkillSources)
            {
                if (Full) break;
                if (!skills.Add((skill.Grant.SkillId.ToUpperInvariant(), skill.Grant.Level))) continue;
                Add(p => Make(p, RecipeLearningNodeKind.Skill, id, depth + 1,
                    () => new(p, RecipeLearningNodeKind.Skill, id, []) { Skill = skill, State = skill.State }));
            }
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in assessment.LinkedSources)
            {
                if (Full) break;
                if (!linked.Add(source.SourceRecipeId)) continue;
                Add(p => Recipe(source.SourceRecipeId, p, depth + 1, false, ancestors, source.State));
            }
            var specials = new HashSet<(RecipeSpecialGrantKind, string, string, int?)>();
            foreach (var special in catalog?.SpecialGrants ?? [])
            {
                if (Full) break;
                if (!special.RecipeId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                var label = string.IsNullOrWhiteSpace(special.SourceLabel) ? special.SourceId : special.SourceLabel;
                if (!specials.Add((special.Kind, label.ToUpperInvariant(), special.LevelId?.ToUpperInvariant() ?? string.Empty, special.RequiredCount))) continue;
                Add(p => Make(p, RecipeLearningNodeKind.Special, id, depth + 1,
                    () => new(p, RecipeLearningNodeKind.Special, id, [])
                    { Special = special, Concealed = specialConcealed?.Invoke(special) == true }));
            }
            if (!Full)
            {
                var sources = new HashSet<(RecipeDiscoverySourceKind, string)>();
                foreach (var source in discovery(id).Routes)
                {
                    if (Full) break;
                    if (!sources.Add((source.Kind, source.SourceId.ToUpperInvariant()))) continue;
                    var kind = source.Kind == RecipeDiscoverySourceKind.Email ? RecipeLearningNodeKind.Email : RecipeLearningNodeKind.Fish;
                    Add(p => Make(p, kind, id, depth + 1, () =>
                    {
                        var concealed = sourceConcealed?.Invoke(source) == true;
                        return new(p, kind, id, [])
                        { Source = source, Concealed = concealed, State = concealed ? RecipeDiscoveryState.Unknown : source.RecordedSourceState };
                    }));
                }
            }
            if (methods.Count == 0) Add(p => Make(p, RecipeLearningNodeKind.Unknown, id, depth + 1,
                () => new(p, RecipeLearningNodeKind.Unknown, id, [])));
            return new(path, RecipeLearningNodeKind.Discovery, id, methods.AsReadOnly())
            { Assessment = assessment, State = Any(methods.Select(method => method.State)) };
        }, expandable: true);

        private RecipeLearningNode PickupInputs(string id, string path, int depth, RecipeLearningAssessment assessment)
            => Make(path, RecipeLearningNodeKind.PickupAll, id, depth, () =>
            {
                var inputs = new List<RecipeLearningNode>();
                if (assessment.Definition?.DiscoveryInputsKnown != true)
                    inputs.Add(Make(path + ".0", RecipeLearningNodeKind.Unknown, id, depth + 1,
                        () => new(path + ".0", RecipeLearningNodeKind.Unknown, id, []) { Assessment = assessment }));
                else
                    foreach (var input in assessment.Inputs)
                    {
                        if (Full) break;
                        var inputPath = path + "." + inputs.Count;
                        var group = input.Input.IsSubstitute || input.Input.AlternativeItemIds.Count != 1;
                        if (!group)
                            inputs.Add(PickupItem(id, inputPath, depth + 1, input.Input.AlternativeItemIds[0], input));
                        else
                            inputs.Add(Make(inputPath, RecipeLearningNodeKind.PickupAny, id, depth + 1, () =>
                            {
                                var alternatives = new List<RecipeLearningNode>();
                                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var item in input.Input.AlternativeItemIds)
                                {
                                    if (Full) break;
                                    if (!seen.Add(item)) continue;
                                    alternatives.Add(PickupItem(id, inputPath + "." + alternatives.Count, depth + 2, item, input));
                                }
                                if (alternatives.Count == 0 && !Full)
                                    alternatives.Add(Make(inputPath + ".0", RecipeLearningNodeKind.Unknown, id, depth + 2,
                                        () => new(inputPath + ".0", RecipeLearningNodeKind.Unknown, id, []) { Input = input }));
                                return new(inputPath, RecipeLearningNodeKind.PickupAny, id, alternatives.AsReadOnly())
                                { Input = input, State = input.State };
                            }, expandable: true));
                    }
                return new(path, RecipeLearningNodeKind.PickupAll, id, inputs.AsReadOnly())
                { Assessment = assessment, State = assessment.PickupIngredientsState };
            }, expandable: true);

        private RecipeLearningNode PickupItem(string id, string path, int depth, string itemId, RecipeLearningInputAssessment input)
            => Make(path, RecipeLearningNodeKind.PickupItem, id, depth,
                () => new(path, RecipeLearningNodeKind.PickupItem, id, [])
                {
                    ItemId = itemId,
                    Input = input,
                    State = !input.Input.IsResolved ? RecipeDiscoveryState.Unknown
                        : pickupState?.Invoke(itemId) ?? (input.Input.AlternativeItemIds.Count == 1 ? input.State : RecipeDiscoveryState.Unknown),
                });

        private static RecipeDiscoveryState ResearchState(RecipeLearningAssessment assessment)
            => assessment.IsResearchPending == true ? RecipeDiscoveryState.KnownUnmet
                : assessment.Definition?.RequiresResearch == false
                    || (assessment.IsDiscovered == true && assessment.IsResearchPending == false) ? RecipeDiscoveryState.KnownMet
                : assessment.IsDiscovered == false && assessment.Definition?.RequiresResearch == true ? RecipeDiscoveryState.KnownUnmet
                : RecipeDiscoveryState.Unknown;
        private static RecipeDiscoveryState Any(IEnumerable<RecipeDiscoveryState> states)
        {
            var values = states.ToArray();
            return values.Contains(RecipeDiscoveryState.KnownMet) ? RecipeDiscoveryState.KnownMet
                : values.Length == 0 || values.Contains(RecipeDiscoveryState.Unknown) ? RecipeDiscoveryState.Unknown : RecipeDiscoveryState.KnownUnmet;
        }
    }
}
