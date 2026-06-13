namespace AbioticEditor.Plugins;

/// <summary>
/// The Abiotic Factor save categories a capability can target. Mirrors the kinds the host
/// detects when scanning a save folder. <see cref="Any"/> means a save operation does not
/// care about the category and will be offered for every save.
/// </summary>
public enum SaveKind
{
    /// <summary>A <c>Player_*.sav</c> character save (stats, inventory, skills, recipes).</summary>
    Player,

    /// <summary>A <c>WorldSave_*.sav</c> region/level save (containers, doors, flags, NPCs).</summary>
    World,

    /// <summary>The per-world metadata save (story chapter, traders, containment).</summary>
    Metadata,

    /// <summary>A <c>ScientistCustomization.sav</c> per-account appearance save.</summary>
    Customization,

    /// <summary>Any save file, regardless of category.</summary>
    Any,
}
