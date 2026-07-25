namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One deployed Leyak Containment Unit, wherever it stands in the world.
///
/// A unit is an ordinary player-placed deployable
/// (<see cref="ContainmentCreatureCatalog.UnitClassName"/>) living in a region save's
/// <c>DeployedObjectMap</c>, keyed by a GUID. Whether it currently holds a creature is
/// <em>not</em> stored on the unit: the metadata save's <c>LeyakContainmentIDs</c> map
/// (creature row -> unit GUID) is the link, which is why an empty unit is simply one whose
/// GUID no entry points at. The unit does keep its own copy of which creature it holds, as the
/// <c>LeyakContainmentData</c> index in <c>EDynamicProperty::Generic3</c>, and its containment
/// stability (0..100) in <c>EDynamicProperty::Generic1</c>.
/// </summary>
/// <param name="Id">The <c>DeployedObjectMap</c> GUID; the value side of a
/// <c>LeyakContainmentIDs</c> entry.</param>
/// <param name="RegionSaveFileName">File name of the region save the unit was found in
/// (e.g. <c>WorldSave_Facility.sav</c>). Empty when read from a single save in isolation.</param>
/// <param name="X">World X of the unit.</param>
/// <param name="Y">World Y of the unit.</param>
/// <param name="Z">World Z of the unit.</param>
/// <param name="Stability">The unit's stored stability, or null when the unit carries no
/// <c>Generic1</c> slot.</param>
/// <param name="StoredCreatureIndex">The unit's own <c>Generic3</c> index, or null when it
/// carries no such slot. -1 is possible on a save that stored an out-of-range value.</param>
/// <param name="Creature">The creature row the metadata save assigns to this unit, or null
/// when the unit is empty. Filled in by <see cref="ContainmentSurvey"/>, not by the reader.</param>
public sealed record WorldContainmentUnit(
    string Id,
    string RegionSaveFileName,
    double X,
    double Y,
    double Z,
    int? Stability,
    int? StoredCreatureIndex,
    string? Creature = null)
{
    /// <summary>True when the metadata save assigns a creature to this unit.</summary>
    public bool IsOccupied => !string.IsNullOrEmpty(Creature);

    /// <summary>
    /// The creature the unit itself thinks it holds, derived from
    /// <see cref="StoredCreatureIndex"/>; null when it has no index or the index is unknown.
    /// </summary>
    public string? StoredCreature =>
        StoredCreatureIndex is { } index ? ContainmentCreatureCatalog.RowAtIndex(index) : null;

    /// <summary>
    /// True when the unit's own stored index disagrees with the creature the metadata save
    /// assigns it. A healthy save never does this; the editor repairs it on write.
    /// </summary>
    public bool StoredCreatureDisagrees =>
        IsOccupied && StoredCreatureIndex is not null
        && StoredCreatureIndex != ContainmentCreatureCatalog.IndexOf(Creature);

    /// <summary>A short, stable label: the unit's region plus the first 8 GUID characters.</summary>
    public string ShortId => Id.Length <= 8 ? Id : Id[..8];
}
