using AbioticEditor.Core.Codex;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.Items;

/// <summary>The result for one specified piece of evidence, not a universal recipe prerequisite.</summary>
public enum RecipeDiscoveryState
{
    Unknown,
    KnownUnmet,
    KnownMet,
}

/// <summary>An explicit recipe grant encoded in the loaded game data.</summary>
public enum RecipeDiscoverySourceKind
{
    Email,
    Fish,
}

/// <summary>
/// A known way to discover a recipe. WorldAccessState assesses only the email's established
/// region mapping or the fish's explicit world flag. KnownMet never guarantees a catch,
/// possession of DLC/bait, or that this is the recipe's only source. RecordedSourceState
/// separately reports this player's read-email/caught-fish history. Labels are catalog data;
/// callers must apply their spoiler policy before displaying them.
/// </summary>
public sealed record RecipeDiscoveryRoute(
    RecipeDiscoverySourceKind Kind,
    string SourceId,
    string? EmailSubject,
    string? FishItemId,
    string? RequiredWorldFlag,
    string? ChapterTitle,
    string? RequiredDlcId,
    string? RequiredBaitTag,
    bool HasTimePreference,
    RecipeDiscoveryState WorldAccessState,
    RecipeDiscoveryState RecordedSourceState)
{
    /// <summary>Catch conditions whose current satisfaction cannot be checked from these inputs.</summary>
    public bool HasUnverifiedConditions => !string.IsNullOrWhiteSpace(RequiredDlcId)
        || !string.IsNullOrWhiteSpace(RequiredBaitTag) || HasTimePreference;

    /// <summary>
    /// Assessment of this known source, not overall recipe eligibility. Recorded history
    /// demonstrates prior access even after a story rewind. Otherwise a missing known world
    /// condition is unmet, while unevaluated catch conditions keep access unknown.
    /// </summary>
    public RecipeDiscoveryState SourceAccessState => RecordedSourceState == RecipeDiscoveryState.KnownMet
        ? RecipeDiscoveryState.KnownMet
        : WorldAccessState == RecipeDiscoveryState.KnownUnmet
            ? RecipeDiscoveryState.KnownUnmet
            : HasUnverifiedConditions ? RecipeDiscoveryState.Unknown : WorldAccessState;
}

/// <summary>
/// Pure assessment of the loaded catalogs' known email/fish recipe grants. Routes are
/// alternatives: one met route suffices for that evidence. Even when every known route is
/// unmet, an unmodeled acquisition method may exist; this report is not an unlock gate.
/// Missing catalogs, unmapped email regions and absent world/player context remain unknown.
/// </summary>
public sealed record RecipeDiscoveryAssessment(string RecipeId, IReadOnlyList<RecipeDiscoveryRoute> Routes)
{
    public bool HasKnownSources => Routes.Count > 0;

    /// <summary>OR across the known routes' limited world-access criteria.</summary>
    public RecipeDiscoveryState WorldAccessState => Combine(Routes.Select(route => route.WorldAccessState));

    /// <summary>Whether any known granting email or fish is recorded in this player's history.</summary>
    public RecipeDiscoveryState RecordedSourceState => Combine(Routes.Select(route => route.RecordedSourceState));

    /// <summary>
    /// OR across known source assessments, accounting for unresolved catch conditions.
    /// This does not prove that a recipe must be learned or may be safely force-unlocked.
    /// </summary>
    public RecipeDiscoveryState SourceAccessState => Combine(Routes.Select(route => route.SourceAccessState));

    public static RecipeDiscoveryAssessment Assess(
        string recipeId,
        IEnumerable<EmailEntry>? emails,
        IEnumerable<FishDefinition>? fish,
        IEnumerable<string>? worldFlags,
        IEnumerable<string>? emailsRead = null,
        IEnumerable<string>? fishCaught = null)
    {
        var flags = AsSet(worldFlags);
        var read = AsSet(emailsRead);
        var caught = AsSet(fishCaught);
        var routes = new List<RecipeDiscoveryRoute>();
        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in emails ?? [])
        {
            if (!email.AttachmentRecipes.Contains(recipeId, StringComparer.OrdinalIgnoreCase)
                || !sourceKeys.Add($"email:{email.Id}")) continue;
            var chapter = FlagGate.RegionChapterForRowId(email.Id);
            var flag = chapter?.TriggerFlag;
            // No mapping means unknown. It never becomes an invented Office/start gate.
            var access = flags is null || string.IsNullOrWhiteSpace(flag)
                ? RecipeDiscoveryState.Unknown
                : flags.Contains(flag) ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet;
            routes.Add(new RecipeDiscoveryRoute(RecipeDiscoverySourceKind.Email, email.Id, email.Subject,
                null, flag, chapter?.Title, null, null, false, access, Recorded(email.Id, read)));
        }

        foreach (var entry in fish ?? [])
        {
            if (!string.Equals(entry.UnlockRecipeId, recipeId, StringComparison.OrdinalIgnoreCase)
                || !sourceKeys.Add($"fish:{entry.Id}")) continue;
            var access = flags is null ? RecipeDiscoveryState.Unknown
                : string.IsNullOrWhiteSpace(entry.RequiredWorldFlag) || flags.Contains(entry.RequiredWorldFlag)
                    ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet;
            routes.Add(new RecipeDiscoveryRoute(RecipeDiscoverySourceKind.Fish, entry.Id, null, entry.ItemId,
                entry.RequiredWorldFlag, null, entry.RequiredDlcId, entry.RequiredBaitTag,
                entry.HasTimePreference, access, Recorded(entry.Id, caught)));
        }

        return new RecipeDiscoveryAssessment(recipeId, routes.AsReadOnly());
    }

    /// <summary>
    /// Unlearned catalog recipes with a granting email or fish recorded in player history.
    /// This stronger clue does not require ingredient pickup history. Unknown history is not
    /// treated as empty or as completion, and unknown saved recipe ids are never fabricated.
    /// Results retain the supplied recipe vocabulary's order and spelling, ignoring aliases.
    /// </summary>
    public static IReadOnlyList<RecipeDiscoveryAssessment> FindRecordedMissing(
        IEnumerable<string> recipeIds,
        IEnumerable<string> learned,
        IEnumerable<EmailEntry>? emails,
        IEnumerable<FishDefinition>? fish,
        IEnumerable<string>? emailsRead,
        IEnumerable<string>? fishCaught,
        IEnumerable<string>? worldFlags = null)
    {
        ArgumentNullException.ThrowIfNull(recipeIds);
        ArgumentNullException.ThrowIfNull(learned);
        var emailRows = emails?.ToArray() ?? [];
        var fishRows = fish?.ToArray() ?? [];
        var read = AsSet(emailsRead);
        var caught = AsSet(fishCaught);
        var known = learned.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in emailRows)
        {
            if (read?.Contains(email.Id) != true) continue;
            grants.UnionWith(email.AttachmentRecipes.Where(id => !string.IsNullOrWhiteSpace(id)));
        }
        foreach (var entry in fishRows)
        {
            if (caught?.Contains(entry.Id) == true && !string.IsNullOrWhiteSpace(entry.UnlockRecipeId))
                grants.Add(entry.UnlockRecipeId);
        }

        var flags = AsSet(worldFlags);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RecipeDiscoveryAssessment>();
        foreach (var id in recipeIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id) || known.Contains(id) || !grants.Contains(id)) continue;
            result.Add(Assess(id, emailRows, fishRows, flags, read, caught));
        }
        return result.AsReadOnly();
    }

    private static HashSet<string>? AsSet(IEnumerable<string>? ids)
        => ids?.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static RecipeDiscoveryState Recorded(string id, HashSet<string>? history)
        => history is null ? RecipeDiscoveryState.Unknown
            : history.Contains(id) ? RecipeDiscoveryState.KnownMet : RecipeDiscoveryState.KnownUnmet;

    private static RecipeDiscoveryState Combine(IEnumerable<RecipeDiscoveryState> states)
    {
        var any = false;
        var unknown = false;
        foreach (var state in states)
        {
            any = true;
            if (state == RecipeDiscoveryState.KnownMet) return RecipeDiscoveryState.KnownMet;
            if (state == RecipeDiscoveryState.Unknown) unknown = true;
        }
        return !any || unknown ? RecipeDiscoveryState.Unknown : RecipeDiscoveryState.KnownUnmet;
    }
}
