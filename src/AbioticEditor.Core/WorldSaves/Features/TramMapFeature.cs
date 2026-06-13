using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Read-only viewer for <c>TramMap</c> (region saves): each tram actor stores its last
/// parked station and any on-board container inventories. This feature surfaces those values
/// for inspection but does not allow editing them.
///
/// <para><b>Why read-only?</b> The only candidate for editing is <c>LastStation_</c>, which
/// is a <c>SoftObjectPath</c> reference to a station actor. Writing an arbitrary string into
/// a soft-object path risks producing an invalid asset reference that the engine cannot
/// resolve, potentially corrupting the save or causing a crash on load. Until the editor
/// builds a validated station-name catalogue (the same pattern used by
/// <c>PortalMapFeature</c> for portal tags), it is safer to surface the field as read-only
/// and return an explicit error from <see cref="ApplyField"/> rather than silently mutate
/// an unvalidated path.</para>
///
/// <para>Schema: map key = tram actor path (e.g.
/// <c>/Game/Maps/Facility…Tram_ParentBP_C_0</c>); value = StructProperty → PropertiesStruct
/// with the following leaves:</para>
/// <list type="bullet">
///   <item><c>ActorPath_</c> (StructProperty SoftObjectPath) – tram actor path; mirrors the
///   map key and is skipped (the key already identifies the tram).</item>
///   <item><c>LastStation_</c> (StructProperty SoftObjectPath) – station the tram last
///   parked at; the <c>AssetName</c> component is the readable station name.</item>
///   <item><c>ContainerInventories_</c> (ArrayProperty of Struct) – on-board storage;
///   surfaced as an inventory count.</item>
/// </list>
/// </summary>
public sealed class TramMapFeature : WorldMapFeatureBase
{
    /// <summary>Leaf prefix for the last-parked-station soft-object path (StructProperty).</summary>
    private const string LastStationPrefix = "LastStation_";

    /// <summary>Leaf prefix for the on-board container inventory array (ArrayProperty).</summary>
    private const string ContainerInventoriesPrefix = "ContainerInventories_";

    /// <inheritdoc/>
    public override string Id => "trams";

    /// <inheritdoc/>
    public override string MapName => "TramMap";

    /// <inheritdoc/>
    public override string DisplayName => "Trams";

    /// <inheritdoc/>
    public override string Description =>
        "View tram state (last station, on-board inventory count) — currently view-only; no fields can be edited.";

    /// <summary>
    /// Reads the tram entry struct into two read-only display fields.
    /// </summary>
    /// <param name="props">The property list from the tram's StructProperty value.</param>
    /// <returns>
    /// Two fields: <c>lastStation</c> (read-only, string or null) and
    /// <c>inventories</c> count (read-only, integer rendered as a string).
    /// Both have <c>Editable == false</c>.
    /// </returns>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        // LastStation_ is a StructProperty whose underlying IStructData is
        // UeSaveGame.StructData.SoftObjectPathStruct (internal type). That wrapper exposes a
        // public SoftObjectPath Value property (UeSaveGame.DataTypes.SoftObjectPath), whose
        // AssetName.Value is the readable station name.
        //
        // Because SoftObjectPathStruct is declared internal we cannot pattern-match against
        // it directly from this assembly. Instead we read the public Value property via
        // reflection — this is stable: the property has been public since the type was
        // introduced and is unlikely to be renamed. We fall back to null on any reflection
        // failure so the display field simply shows nothing rather than throwing.
        string? lastStation = null;
        if (props.FindByPrefix(LastStationPrefix)?.Property is StructProperty sp && sp.Value is not null)
        {
            try
            {
                // SoftObjectPathStruct.Value : SoftObjectPath (public property)
                var innerProp = sp.Value.GetType().GetProperty("Value");
                if (innerProp?.GetValue(sp.Value) is SoftObjectPath sop)
                {
                    lastStation = sop.AssetName?.Value;
                }
            }
            catch (Exception)
            {
                // Reflection failure is non-fatal; lastStation remains null.
            }
        }

        // ContainerInventories_ is an ArrayProperty; surface its element count (0 when absent).
        var inventoryCount = props.FindByPrefix(ContainerInventoriesPrefix)?.Property is ArrayProperty arr
            ? arr.Value?.Length ?? 0
            : 0;

        return new[]
        {
            WorldMapField.ReadOnly("lastStation", "Last station", lastStation,
                hint: "The station this tram last parked at (SoftObjectPath AssetName component). "
                    + "Read-only: editing soft-object paths requires a validated station catalogue."),
            WorldMapField.ReadOnly("inventories", "Container inventories",
                inventoryCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hint: "Number of on-board storage containers attached to this tram."),
        };
    }

    /// <summary>
    /// Always returns a failure result because all tram fields are read-only in this editor.
    /// </summary>
    /// <remarks>
    /// <para>The tram's mutable leaf (<c>LastStation_</c>) is a soft-object path whose validity
    /// cannot be verified without a station catalogue. Rather than silently corrupt the save,
    /// this method rejects every write until a validated path-picker is available. The
    /// read path is unaffected and works normally.</para>
    /// </remarks>
    /// <param name="props">Unused — no field can be patched.</param>
    /// <param name="fieldId">The field the caller wanted to set.</param>
    /// <param name="value">The value the caller supplied.</param>
    /// <returns>A <see cref="WorldEditResult"/> with <see cref="WorldEditResult.IsError"/> true.</returns>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
        => WorldEditResult.Failure("Tram entries are read-only in this editor.");
}
