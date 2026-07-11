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
/// observed. Versions <em>below</em> the validated range also classify as Exact: the game
/// migrates old saves forward and a lower version means strictly fewer fields, not
/// different ones.</item>
/// <item><see cref="NewerMinor"/> - the version is known but the save contains content
/// this build has no model for (unknown quest flags, unmodeled property keys, unknown
/// enum values, an unknown story chapter). Everything unknown is preserved verbatim on
/// save; it just isn't visible or editable in the UI.</item>
/// <item><see cref="NewerVersion"/> - ABF_SAVE_VERSION is above anything this build was
/// validated against. The save still parsed, so edits are possible but at the user's own
/// risk; writers do not refuse (a structurally unreadable save already throws at parse
/// time, which is the only hard refusal).</item>
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

    /// <summary>ABF_SAVE_VERSION above the validated range - edit at your own risk.</summary>
    NewerVersion,

    /// <summary>Unrecognized save class (or unreadable version) - editing not recommended.</summary>
    Unknown,
}
