namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// The kinds of save file Abiotic Factor produces. The registry keys its version
/// knowledge on this, not on raw class-path strings, so the rest of the editor can
/// reason about "a world save" without string matching.
/// </summary>
public enum SaveKind
{
    /// <summary>Save class not recognized by this editor build.</summary>
    Unknown = 0,

    /// <summary>Per-region world save (<c>Abiotic_WorldSave_C</c>, <c>WorldSave_Facility_*.sav</c>).</summary>
    World,

    /// <summary>World metadata save (<c>Abiotic_WorldMetadataSave_C</c>, <c>WorldSave_MetaData.sav</c>).</summary>
    Metadata,

    /// <summary>Player/character save (<c>Abiotic_CharacterSave_C</c>, <c>Player_*.sav</c>).</summary>
    Character,

    /// <summary>Per-account appearance file (<c>Abiotic_CustomizationSave_C</c>, <c>ScientistCustomization_*.sav</c>). Carries no ABF_SAVE_VERSION header.</summary>
    Customization,
}

/// <summary>
/// How compatible a loaded save is with this editor build.
///
/// Severity rules (see <see cref="SaveVersionRegistry.Classify"/>):
/// <list type="bullet">
/// <item><see cref="Exact"/> - the save kind is known, its ABF_SAVE_VERSION is within the
/// validated range (or the kind carries no version header), and no unknown content was
/// observed.</item>
/// <item><see cref="OlderVersion"/> - ABF_SAVE_VERSION is below the lowest version any
/// validation fixture carried. A lower version can mean missing tags, different defaults
/// or a different struct layout, and nothing has been tested against it, so writes are not
/// supported (inspection still is). This used to classify as Exact; that was wrong because
/// the editor has never seen such a file.</item>
/// <item><see cref="NewerMinor"/> - the version is known but the save contains content
/// this build has no model for (unknown quest flags, unmodeled property keys, unknown
/// enum values, an unknown story chapter). Everything unknown is preserved verbatim on
/// save; it just isn't visible or editable in the UI.</item>
/// <item><see cref="NewerVersion"/> - ABF_SAVE_VERSION is above anything this build was
/// validated against. The game bumps this number when it changes a save's field layout, so
/// writes are blocked (see <see cref="SaveWriteGuard"/>); inspection still works.</item>
/// <item><see cref="LayoutChanged"/> - the version header is unremarkable but a large share of the
/// save's properties are unmodeled. The game restructured the save without bumping the
/// version, so writes are blocked exactly as for <see cref="NewerVersion"/>.</item>
/// <item><see cref="Unknown"/> - the save class is not recognized at all (or a versioned
/// kind's version could not be read). Editing is not recommended.</item>
/// </list>
/// </summary>
public enum CompatibilitySeverity
{
    /// <summary>Validated: known kind, known version, no unknown content.</summary>
    Exact = 0,

    /// <summary>Known version but the save carries content this build doesn't model.</summary>
    NewerMinor,

    /// <summary>ABF_SAVE_VERSION above the validated range - writes are blocked, inspection still works.</summary>
    NewerVersion,

    /// <summary>Unrecognized save class (or unreadable version) - editing not recommended.</summary>
    Unknown,

    /// <summary>
    /// ABF_SAVE_VERSION below the lowest validated version - read-only inspection is fine,
    /// writes are not supported because no fixture establishes the older layout.
    /// Appended last so the numeric values of the existing members do not change.
    /// </summary>
    OlderVersion,

    /// <summary>
    /// The version header looks familiar but many top-level properties are ones this build has no
    /// model for, which means the game renamed or restructured its fields. The readers would find
    /// nothing (money, skills and recipes read as empty) and an edit would write fields the game
    /// ignores, so writes are blocked and inspection stays available.
    /// Appended last so the numeric values of the existing members do not change.
    /// </summary>
    LayoutChanged,
}
