using AbioticEditor.Core.SaveClasses;
using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// What the registry knows about one save kind: the ABF_SAVE_VERSION range this editor
/// build was built and tested against, and which game build that validation happened on.
/// </summary>
/// <param name="Kind">The save kind this entry describes.</param>
/// <param name="DisplayName">Human label for messages ("World save", ...).</param>
/// <param name="MinKnownVersion">
/// Lowest ABF_SAVE_VERSION observed in validation fixtures. A loaded save below this
/// classifies as <see cref="CompatibilitySeverity.OlderVersion"/>: it may still parse, but
/// no fixture establishes its field layout or defaults, so writes are not supported.
/// </param>
/// <param name="MaxKnownVersion">
/// Highest validated ABF_SAVE_VERSION. A loaded save above this classifies as
/// <see cref="CompatibilitySeverity.NewerVersion"/>. Null when the kind carries no
/// ABF_SAVE_VERSION header at all (customization saves).
/// </param>
/// <param name="ValidatedGameBuild">The game build string the validation fixtures came from.</param>
public sealed record SaveVersionInfo(
    SaveKind Kind,
    string DisplayName,
    int? MinKnownVersion,
    int? MaxKnownVersion,
    string ValidatedGameBuild)
{
    /// <summary>True when this kind has an ABF_SAVE_VERSION custom header to compare.</summary>
    public bool HasVersionedHeader => MaxKnownVersion is not null;
}

/// <summary>
/// THE single place that says which save versions this editor build understands.
///
/// When a game update ships: re-validate the fixtures, bump the affected
/// <see cref="Entries"/> rows and <see cref="ValidatedGameBuild"/> here, and everything
/// downstream (load-time warnings, <see cref="CompatibilityReport"/>s, the legacy
/// <see cref="SaveCompatibility"/> surface) follows automatically.
/// </summary>
public static class SaveVersionRegistry
{
    /// <summary>
    /// The game build the current version table was validated against - taken from the
    /// usmap the mappings were extracted from
    /// (<c>AbioticFactor-5.4.4-1030002+++DF+ABF-01e0a584.usmap</c>).
    /// </summary>
    public const string ValidatedGameBuild = "5.4.4-1030002+++DF+ABF-01e0a584";

    /// <summary>The full version table, one row per known <see cref="SaveKind"/>.</summary>
    public static IReadOnlyList<SaveVersionInfo> Entries { get; } = new[]
    {
        // ABF_SAVE_VERSION 3 observed across every world fixture (Cascade client save,
        // dedicated-server tree, fresh saves).
        new SaveVersionInfo(SaveKind.World, "World save", 3, 3, ValidatedGameBuild),
        // The metadata save shares Abiotic_WorldSave_C's header layout and version.
        new SaveVersionInfo(SaveKind.Metadata, "World metadata save", 3, 3, ValidatedGameBuild),
        // ABF_SAVE_VERSION 1 observed in every Player_*.sav fixture.
        new SaveVersionInfo(SaveKind.Character, "Character save", 1, 1, ValidatedGameBuild),
        // ScientistCustomization_*.sav has no ABF_SAVE_VERSION header; compatibility is
        // judged purely on whether its content is recognized.
        new SaveVersionInfo(SaveKind.Customization, "Scientist customization save", null, null, ValidatedGameBuild),
    };

    private static readonly Dictionary<SaveKind, SaveVersionInfo> ByKind =
        Entries.ToDictionary(e => e.Kind);

    /// <summary>
    /// Engine builds (the changelist stored in every GVAS header) that have been seen in real
    /// fixtures. The header holds the engine version and branch only: it carries neither the
    /// game's own version string nor the build hash, so it can never prove an exact game
    /// build. See <see cref="IdentifyBuild"/>.
    /// </summary>
    public static IReadOnlyList<KnownEngineBuild> KnownEngineBuilds { get; } = new[]
    {
        // The build the mappings and every write path were validated against.
        new KnownEngineBuild(5, 4, 4, 1030002, "++DF+ABF", EngineBuildStatus.Validated,
            "Dedicated-server tree and current Steam client saves."),
        // Older Steam standalone fixtures (tests/fixtures/SteamSaves/Legacy). Byte-exact
        // round-trip is proven by a fixture test; no per-area edit fixtures are recorded.
        new KnownEngineBuild(5, 4, 4, 1030001, "++DF+ABF", EngineBuildStatus.ObservedRoundTripOnly,
            "Steam Legacy/Cascade world and some Chrissie client saves."),
    };

    /// <summary>
    /// Identifies what the header evidence says about the build. Returns
    /// <see cref="BuildIdentification.Unknown"/> when there is no header evidence, and never
    /// consults the installed game.
    /// </summary>
    public static BuildIdentification IdentifyBuild(SaveHeaderEvidence? header, out KnownEngineBuild? match)
    {
        match = null;
        if (header is null) return BuildIdentification.Unknown;
        foreach (var known in KnownEngineBuilds)
        {
            if (known.Matches(header))
            {
                match = known;
                return known.Status == EngineBuildStatus.Validated
                    ? BuildIdentification.ValidatedEngineBuild
                    : BuildIdentification.ObservedEngineBuild;
            }
        }
        return BuildIdentification.UnrecognizedEngineBuild;
    }

    /// <summary>The registry row for <paramref name="kind"/>, or null for <see cref="SaveKind.Unknown"/>.</summary>
    public static SaveVersionInfo? Find(SaveKind kind)
        => ByKind.TryGetValue(kind, out var info) ? info : null;

    /// <summary>
    /// Determines what kind of save <paramref name="save"/> is. Prefers the registered
    /// custom save class (which proves the header parsed), falling back to the raw class
    /// path string for kinds without a registered class (customization).
    /// </summary>
    public static SaveKind KindOf(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.CustomSaveClass switch
        {
            AbioticCharacterSave => SaveKind.Character,
            AbioticWorldSave => KindOfClassPath(save.SaveClass?.Value) is SaveKind.Metadata
                ? SaveKind.Metadata
                : SaveKind.World,
            _ => KindOfClassPath(save.SaveClass?.Value),
        };
    }

    /// <summary>Classifies a raw save class path string (e.g. from the GVAS header).</summary>
    public static SaveKind KindOfClassPath(string? saveClassPath)
    {
        if (string.IsNullOrEmpty(saveClassPath)) return SaveKind.Unknown;
        if (saveClassPath.Contains("Abiotic_WorldMetadataSave", StringComparison.OrdinalIgnoreCase)) return SaveKind.Metadata;
        if (saveClassPath.Contains("Abiotic_WorldSave", StringComparison.OrdinalIgnoreCase)) return SaveKind.World;
        if (saveClassPath.Contains("Abiotic_CharacterSave", StringComparison.OrdinalIgnoreCase)) return SaveKind.Character;
        if (saveClassPath.Contains("Abiotic_CustomizationSave", StringComparison.OrdinalIgnoreCase)) return SaveKind.Customization;
        return SaveKind.Unknown;
    }

    /// <summary>
    /// The ABF_SAVE_VERSION header value of a loaded save, or null when the save class
    /// didn't map to a registered custom save class (no parsed header).
    /// </summary>
    public static int? GetAbfVersion(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.CustomSaveClass switch
        {
            AbioticCharacterSave c => c.Version,
            AbioticWorldSave w => w.Version,
            _ => null,
        };
    }

    /// <summary>
    /// Sets the ABF_SAVE_VERSION header value on a loaded save. Returns false when the
    /// save has no registered versioned header. Exists so tooling/tests can fabricate
    /// future-version saves without reaching into internal save classes.
    /// </summary>
    public static bool TrySetAbfVersion(SaveGame save, int version)
    {
        ArgumentNullException.ThrowIfNull(save);
        switch (save.CustomSaveClass)
        {
            case AbioticCharacterSave c:
                c.Version = version;
                return true;
            case AbioticWorldSave w:
                w.Version = version;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Applies the severity rules documented on <see cref="CompatibilitySeverity"/>.
    /// </summary>
    /// <param name="kind">The save kind (Unknown ⇒ <see cref="CompatibilitySeverity.Unknown"/>).</param>
    /// <param name="versionSeen">The ABF_SAVE_VERSION read from the save, when its kind has one.</param>
    /// <param name="hasUnknownContent">Whether unknown flags/keys/enum values were observed.</param>
    public static CompatibilitySeverity Classify(SaveKind kind, int? versionSeen, bool hasUnknownContent)
    {
        var entry = Find(kind);
        if (entry is null) return CompatibilitySeverity.Unknown;

        if (entry.HasVersionedHeader)
        {
            if (versionSeen is not int v) return CompatibilitySeverity.Unknown;
            if (v > entry.MaxKnownVersion) return CompatibilitySeverity.NewerVersion;
            if (v < entry.MinKnownVersion) return CompatibilitySeverity.OlderVersion;
        }

        return hasUnknownContent ? CompatibilitySeverity.NewerMinor : CompatibilitySeverity.Exact;
    }
}
