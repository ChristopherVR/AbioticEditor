namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// What we learned about a save's compatibility with this editor build when opening it:
/// version seen vs. known, plus counts of content this build has no model for. Produced
/// by <see cref="CompatibilityAnalyzer"/>.
/// </summary>
public sealed class CompatibilityReport
{
    /// <summary>The save kind, per <see cref="SaveVersionRegistry.KindOf"/>.</summary>
    public required SaveKind Kind { get; init; }

    /// <summary>The raw save class string from the file header.</summary>
    public string? SaveClassName { get; init; }

    /// <summary>ABF_SAVE_VERSION read from the save's custom header, when its kind has one.</summary>
    public int? VersionSeen { get; init; }

    /// <summary>The registry's knowledge for this kind, or null when the kind is unknown.</summary>
    public SaveVersionInfo? Known { get; init; }

    /// <summary>Overall verdict - see <see cref="CompatibilitySeverity"/> for the rules.</summary>
    public required CompatibilitySeverity Severity { get; init; }

    /// <summary>World flags present in the save but absent from <c>QuestFlagCatalog.KnownFlags</c>.</summary>
    public IReadOnlyList<string> UnknownFlags { get; init; } = Array.Empty<string>();

    /// <summary>Top-level save properties the readers have no model for ("Area: Key").</summary>
    public IReadOnlyList<string> UnknownPropertyKeys { get; init; } = Array.Empty<string>();

    /// <summary>Enum/vocabulary values outside this build's catalogs ("Area: Value").</summary>
    public IReadOnlyList<string> UnknownEnumValues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The metadata save's <c>StoryProgressionRow</c> when it isn't a chapter this build
    /// knows (<c>StoryProgressionCatalog</c>); null when absent or known.
    /// </summary>
    public string? UnknownStoryChapter { get; init; }

    /// <summary>Total count of unknown-content observations across all categories.</summary>
    public int UnknownContentCount
        => UnknownFlags.Count + UnknownPropertyKeys.Count + UnknownEnumValues.Count
           + (UnknownStoryChapter is null ? 0 : 1);

    /// <summary>True when the save carries anything this build has no model for.</summary>
    public bool HasUnknownContent => UnknownContentCount > 0;

    /// <summary>
    /// User-facing warning for this save, or null when nothing needs saying
    /// (<see cref="CompatibilitySeverity.Exact"/>).
    /// </summary>
    public string? Warning => Severity switch
    {
        CompatibilitySeverity.Unknown => CompatibilityMessages.UnknownClassWarning(SaveClassName),
        CompatibilitySeverity.NewerVersion => CompatibilityMessages.NewerVersionWarning(
            VersionSeen ?? 0, Known?.MaxKnownVersion ?? 0),
        CompatibilitySeverity.NewerMinor => CompatibilityMessages.NewerMinorWarning(this),
        _ => null,
    };

    /// <summary>Always-present one-line status, suitable for an info bar or log line.</summary>
    public string Summary
    {
        get
        {
            var kindLabel = Known?.DisplayName ?? $"Unrecognized save ({SaveClassName ?? "no class"})";
            var version = VersionSeen is int v ? $" v{v}" : string.Empty;
            var unknowns = HasUnknownContent ? $", {UnknownContentCount} unknown item(s)" : string.Empty;
            return $"{kindLabel}{version} - {Severity}{unknowns} (validated against game build {SaveVersionRegistry.ValidatedGameBuild})";
        }
    }
}

/// <summary>
/// The shared user-facing message texts, used both by <see cref="CompatibilityReport"/>
/// and the legacy <see cref="SaveCompatibility"/> surface so the wording
/// never drifts apart.
/// </summary>
internal static class CompatibilityMessages
{
    internal static string UnknownClassWarning(string? saveClassName)
        => $"This save's class '{saveClassName ?? "(none)"}' is not one this editor recognizes. " +
           "Its custom header could not be interpreted; editing it is not recommended - a .bak backup is always kept.";

    internal static string NewerVersionWarning(int versionSeen, int knownGoodVersion)
        => $"This save is version {versionSeen}; this editor was built against version {knownGoodVersion} " +
           $"(game build {SaveVersionRegistry.ValidatedGameBuild}). " +
           "Newer fields may be invisible and could be lost on edit - a .bak backup is always kept.";

    internal static string NewerMinorWarning(CompatibilityReport report)
    {
        var parts = new List<string>(4);
        if (report.UnknownFlags.Count > 0) parts.Add($"{report.UnknownFlags.Count} unknown quest flag(s)");
        if (report.UnknownStoryChapter is not null) parts.Add($"an unknown story chapter ('{report.UnknownStoryChapter}')");
        if (report.UnknownPropertyKeys.Count > 0) parts.Add($"{report.UnknownPropertyKeys.Count} unmodeled propert(y/ies)");
        if (report.UnknownEnumValues.Count > 0) parts.Add($"{report.UnknownEnumValues.Count} unknown value(s)");

        var detail = parts.Count > 0 ? string.Join(", ", parts) : "content this editor doesn't model";
        return $"This save contains {detail} - likely from a newer game update. " +
               "Unknown content is preserved untouched when saving, but is not visible or editable here.";
    }
}
