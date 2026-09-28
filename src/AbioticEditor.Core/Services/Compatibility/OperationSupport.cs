namespace AbioticEditor.Core.Compatibility;

/// <summary>Whether an editing area may write to a given save.</summary>
public enum OperationSupportLevel
{
    /// <summary>
    /// The save matches the build the write path was tested against on fixtures. In-game
    /// reload evidence is recorded separately in <c>docs/reference/compatibility-support-matrix.md</c>.
    /// </summary>
    Supported = 0,

    /// <summary>
    /// The save parsed but nothing proves this area's serialized shape for it (unrecognized or
    /// unidentified build, newer version). A UI may allow the write behind an explicit warning.
    /// </summary>
    Unverified,

    /// <summary>The write is refused; read-only inspection stays available.</summary>
    Unsupported,
}

/// <summary>Editing areas whose write support depends on the save/game combination.</summary>
public enum EditingArea
{
    /// <summary>Reading, browsing and comparing. Never blocked for a save that parsed.</summary>
    Inspection = 0,

    /// <summary>Character vitals, stats and money (<c>Player_*.sav</c>).</summary>
    PlayerStats,

    /// <summary>Character inventory, equipment and hotbar.</summary>
    PlayerInventory,

    /// <summary>Character skills, traits, recipes and codex.</summary>
    PlayerProgression,

    /// <summary>Renaming or re-keying the player's SteamID (filename plus in-file identifier).</summary>
    PlayerIdentity,

    /// <summary>World flags, story chapter and other metadata-save fields.</summary>
    WorldStoryAndFlags,

    /// <summary>Containers and dropped items in a region save.</summary>
    WorldContainers,

    /// <summary>Deployed objects: doors, machines, power, gardens and other placed state.</summary>
    WorldDeployables,

    /// <summary>NPCs, pets and vehicles in a region save.</summary>
    WorldCreatures,

    /// <summary>Appearance/customization save.</summary>
    Customization,

    /// <summary>
    /// Offering items, recipes and other rows from the current game's catalogs as valid
    /// writes. Requires the catalogs to match the save's build.
    /// </summary>
    CatalogWrites,
}

/// <summary>The verdict for one <see cref="EditingArea"/>, with the reason shown to the user.</summary>
/// <param name="Area">The editing area.</param>
/// <param name="Level">Supported, unverified or unsupported.</param>
/// <param name="Reason">Plain-language explanation of the verdict.</param>
public sealed record AreaSupport(EditingArea Area, OperationSupportLevel Level, string Reason)
{
    /// <summary>True unless the area is <see cref="OperationSupportLevel.Unsupported"/>.</summary>
    public bool AllowsWrite => Level != OperationSupportLevel.Unsupported;

    /// <summary>True only for <see cref="OperationSupportLevel.Supported"/> (no warning needed).</summary>
    public bool WriteIsVerified => Level == OperationSupportLevel.Supported;
}

/// <summary>
/// Per-area write support for one analyzed save. Inspection is reported separately and stays
/// available whenever the save parsed, so a UI can block a write without hiding data.
/// </summary>
public sealed class OperationSupport
{
    private readonly Dictionary<EditingArea, AreaSupport> _byArea;

    internal OperationSupport(SaveKind kind, IReadOnlyList<AreaSupport> areas)
    {
        Kind = kind;
        Areas = areas;
        _byArea = areas.ToDictionary(a => a.Area);
    }

    /// <summary>The save kind these verdicts are for.</summary>
    public SaveKind Kind { get; }

    /// <summary>Every area that applies to <see cref="Kind"/>, inspection first.</summary>
    public IReadOnlyList<AreaSupport> Areas { get; }

    /// <summary>The verdict for <paramref name="area"/>, or null when it does not apply to this kind of save.</summary>
    public AreaSupport? Find(EditingArea area) => _byArea.GetValueOrDefault(area);

    /// <summary>
    /// The verdict for <paramref name="area"/>; an area that does not apply to this kind is
    /// reported as unsupported so callers can gate on it without a null check.
    /// </summary>
    public AreaSupport Get(EditingArea area) => Find(area)
        ?? new AreaSupport(area, OperationSupportLevel.Unsupported, $"This area does not apply to a {Kind} save.");

    /// <summary>True when <paramref name="area"/> may be written (supported or unverified).</summary>
    public bool CanWrite(EditingArea area) => Get(area).AllowsWrite;

    /// <summary>True when the save can at least be inspected.</summary>
    public bool CanInspect => Get(EditingArea.Inspection).AllowsWrite;

    /// <summary>The areas that must not be written for this save.</summary>
    public IEnumerable<AreaSupport> BlockedAreas => Areas.Where(a => a.Level == OperationSupportLevel.Unsupported);
}

/// <summary>
/// Turns a <see cref="CompatibilityReport"/> into an <see cref="OperationSupport"/>. The rules,
/// most restrictive first: an unrecognized save class or a version/engine build older than
/// anything in the fixtures is Unsupported for writes; a newer version, an unrecognized or
/// unidentified build, or a build seen only in round-trip fixtures is Unverified; only a save
/// on the validated engine build with a known version is Supported.
/// </summary>
public static class OperationSupportEvaluator
{
    /// <summary>Evaluates <paramref name="report"/>.</summary>
    public static OperationSupport Evaluate(CompatibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var areas = new List<AreaSupport>();
        var parsed = report.Kind != SaveKind.Unknown;
        areas.Add(new AreaSupport(
            EditingArea.Inspection,
            parsed ? OperationSupportLevel.Supported : OperationSupportLevel.Unsupported,
            parsed ? "Reading never rewrites the file." : "This save class is not modeled, so there is nothing to show."));

        var (level, reason) = WriteVerdict(report);
        foreach (var area in WriteAreasFor(report.Kind))
        {
            areas.Add(new AreaSupport(area, level, reason));
        }

        return new OperationSupport(report.Kind, areas);
    }

    private static EditingArea[] WriteAreasFor(SaveKind kind) => kind switch
    {
        SaveKind.Character => new[]
        {
            EditingArea.PlayerStats, EditingArea.PlayerInventory, EditingArea.PlayerProgression,
            EditingArea.PlayerIdentity, EditingArea.CatalogWrites,
        },
        SaveKind.World => new[]
        {
            EditingArea.WorldContainers, EditingArea.WorldDeployables, EditingArea.WorldCreatures,
            EditingArea.CatalogWrites,
        },
        SaveKind.Metadata => new[] { EditingArea.WorldStoryAndFlags },
        SaveKind.Customization => new[] { EditingArea.Customization },
        _ => Array.Empty<EditingArea>(),
    };

    private static (OperationSupportLevel Level, string Reason) WriteVerdict(CompatibilityReport report)
    {
        if (report.Kind == SaveKind.Unknown)
            return (OperationSupportLevel.Unsupported,
                $"The save class '{report.SaveClassName ?? "(none)"}' is not one this editor models.");

        if (report.Severity == CompatibilitySeverity.OlderVersion)
            return (OperationSupportLevel.Unsupported,
                $"This save is version {report.VersionSeen} and the lowest version tested is {report.Known?.MinKnownVersion}. " +
                "Its field layout and defaults are not established, so writes are blocked.");

        if (report.Severity == CompatibilitySeverity.Unknown)
            return (OperationSupportLevel.Unsupported, "The save's version header could not be read.");

        switch (report.BuildIdentification)
        {
            case BuildIdentification.UnrecognizedEngineBuild when IsBelowKnownBuilds(report):
                return (OperationSupportLevel.Unsupported,
                    $"The save was written by engine build {report.Header?.EngineLabel}, older than every fixture. Writes are blocked.");
            case BuildIdentification.UnrecognizedEngineBuild:
                return (OperationSupportLevel.Unverified,
                    $"The save was written by engine build {report.Header?.EngineLabel}, which this editor has not been validated against.");
            case BuildIdentification.ObservedEngineBuild:
                return (OperationSupportLevel.Unverified,
                    $"Engine build {report.Header?.EngineLabel} is only proven to round-trip byte for byte; no edit has been verified against it.");
            case BuildIdentification.Unknown:
                return (OperationSupportLevel.Unverified,
                    "The exact game build cannot be identified from this save (no readable header evidence).");
        }

        if (report.Severity == CompatibilitySeverity.NewerVersion)
            return (OperationSupportLevel.Unverified,
                $"The save is version {report.VersionSeen}, newer than the tested version {report.Known?.MaxKnownVersion}. Newer fields may be lost on edit.");

        return report.Severity == CompatibilitySeverity.NewerMinor
            ? (OperationSupportLevel.Supported, "Validated build. Unknown content is preserved untouched but is not editable.")
            : (OperationSupportLevel.Supported, "Validated build and version.");
    }

    private static bool IsBelowKnownBuilds(CompatibilityReport report)
    {
        if (report.Header is null) return false;
        var lowest = SaveVersionRegistry.KnownEngineBuilds.Min(b => b.Changelist);
        return report.Header.EngineChangelist < lowest;
    }
}
