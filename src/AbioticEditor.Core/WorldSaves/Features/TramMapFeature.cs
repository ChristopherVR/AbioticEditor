using System.Globalization;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>TramMap</c> (region saves; in practice only <c>WorldSave_Facility.sav</c>):
/// each tram actor stores the station it last parked at plus any on-board container
/// inventories. The last-parked station is editable; the on-board inventory count is shown
/// read-only.
///
/// <para><b>Editing the last station.</b> <c>LastStation_</c> is a <c>SoftObjectPath</c> whose
/// station identity lives in its <c>SubPathString</c> (e.g.
/// <c>PersistentLevel.TramSystem_Station_C_9</c>); its PackageName/AssetName are the constant
/// <c>/Game/Maps/Facility</c> + <c>Facility</c> for every tram, so the AssetName carries no
/// per-tram information. Rather than let the user type an arbitrary (and easily invalid) path,
/// this feature collects the set of stations the save actually references - the
/// <c>SubPathString</c> of every tram's current <c>LastStation_</c> - and offers those as an
/// editable choice. Writing a chosen value only replaces the <c>SubPathString</c> leaf
/// (PackageName/AssetName stay untouched) via
/// <see cref="WorldMapAccessor.SetSoftObjectSubPath"/>.</para>
///
/// <para><b>Known limitation:</b> the option set is the union of stations currently occupied by
/// some tram. Because each tram is parked at a distinct station, this lists only stations that
/// at least one tram is sitting at right now - it cannot enumerate empty stations the level
/// defines but no tram currently occupies (that would require reading the level asset, not the
/// save). In practice the facility's trams cover the reachable stations, so this is enough to
/// re-park a tram at any station another tram has visited.</para>
///
/// <para>Schema: map key = tram actor path (e.g.
/// <c>/Game/Maps/Facility…Tram_ParentBP_C_0</c>); value = StructProperty → PropertiesStruct
/// with the following leaves:</para>
/// <list type="bullet">
///   <item><c>ActorPath_</c> (StructProperty SoftObjectPath) – tram actor path; mirrors the
///   map key and is skipped (the key already identifies the tram).</item>
///   <item><c>LastStation_</c> (StructProperty SoftObjectPath) – station the tram last
///   parked at; the editable value is the <c>SubPathString</c> component.</item>
///   <item><c>ContainerInventories_</c> (ArrayProperty of Struct) – on-board storage;
///   surfaced as a read-only inventory count.</item>
/// </list>
/// </summary>
public sealed class TramMapFeature : WorldMapFeatureBase, IWorldMapFeature
{
    /// <summary>Leaf prefix for the last-parked-station soft-object path (StructProperty).</summary>
    private const string LastStationPrefix = "LastStation_";

    /// <summary>Leaf prefix for the on-board container inventory array (ArrayProperty).</summary>
    private const string ContainerInventoriesPrefix = "ContainerInventories_";

    /// <summary>The field id for the editable last-station choice.</summary>
    private const string LastStationFieldId = "lastStation";

    /// <summary>
    /// A leading <c>PersistentLevel.</c> qualifier on a station SubPathString. Stripped for the
    /// friendly display label and re-added when mapping a chosen label back to the real path.
    /// </summary>
    private const string PersistentLevelPrefix = "PersistentLevel.";

    /// <inheritdoc/>
    public override string Id => "trams";

    /// <inheritdoc/>
    public override string MapName => "TramMap";

    /// <inheritdoc/>
    public override string DisplayName => "Trams";

    /// <inheritdoc/>
    public override string Description =>
        "View each tram's on-board inventory count and re-park it at a different station "
        + "(choose from the stations the save's trams currently occupy).";

    /// <summary>
    /// Trams cannot be removed: deleting a tram's persisted state would strip the tram from the
    /// world rather than do anything useful, so the per-entry remove action is disabled.
    /// </summary>
    public override bool SupportsRemoval => false;

    /// <summary>
    /// Reads every tram entry, first gathering the union of all trams' current
    /// <c>LastStation_</c> SubPathStrings so each entry's <c>lastStation</c> field can be offered
    /// as an editable choice over that set.
    /// </summary>
    /// <remarks>
    /// This shadows <see cref="WorldMapFeatureBase.Read"/> because the choice options depend on
    /// the whole map (the set of occupied stations), not on a single entry, so the per-entry
    /// <see cref="ReadFields"/> hook the base calls is not enough. The interface dispatches here
    /// because <see cref="TramMapFeature"/> re-declares <see cref="IWorldMapFeature"/>.
    /// </remarks>
    public new IReadOnlyList<WorldMapEntry> Read(SaveGame save)
    {
        // First pass: collect the friendly station labels the save references; this becomes the
        // editable choice option set (see the class-level limitation note).
        var labels = GatherStationLabels(save);

        // Second pass: build the rows, threading the shared option set through ReadFields.
        var list = new List<WorldMapEntry>();
        var ordinal = 0;
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            ordinal++;
            list.Add(new WorldMapEntry(
                entry.Key,
                LabelFor(ordinal, entry.Key, entry.Props),
                ReadFieldsWithStations(entry.Props, labels)));
        }
        return list;
    }

    /// <summary>
    /// Validates the chosen station against the save-wide option set before delegating to
    /// <see cref="ApplyField"/>. Shadows <see cref="WorldMapFeatureBase.SetField"/> so the
    /// validation can see every tram's station (the option list), not just the target entry; the
    /// interface dispatches here because <see cref="TramMapFeature"/> re-declares
    /// <see cref="IWorldMapFeature"/>.
    /// </summary>
    public new WorldEditResult SetField(SaveGame save, string entryKey, string fieldId, string? value)
    {
        ArgumentNullException.ThrowIfNull(save);

        var props = WorldMapAccessor.FindEntry(save, MapName, entryKey);
        if (props is null)
        {
            return WorldEditResult.Failure($"no entry '{entryKey}' in {MapName}.");
        }

        // Only the station field needs the save-wide option set; defer anything else to ApplyField.
        if (string.Equals(fieldId, LastStationFieldId, StringComparison.OrdinalIgnoreCase))
        {
            var options = GatherStationLabels(save);
            var check = ResolveChoice(value, options, out var resolvedLabel);
            if (check.IsError)
            {
                return check;
            }
            // resolvedLabel is the canonical friendly label; ApplyField maps it back to a path.
            return ApplyField(props, fieldId, resolvedLabel);
        }

        return ApplyField(props, fieldId, value);
    }

    /// <summary>Trams have no friendly key, so number them for the list.</summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
        => $"Tram {ordinal}";

    /// <summary>
    /// Collects the distinct, friendly station labels referenced by every tram's current
    /// <c>LastStation_</c>, in a stable order. This is the editable choice option set.
    /// </summary>
    private string[] GatherStationLabels(SaveGame save)
    {
        var stationPaths = new List<string>();
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            var sub = WorldMapAccessor.GetSoftObjectPath(entry.Props, LastStationPrefix)?.SubPath;
            if (!string.IsNullOrWhiteSpace(sub) && !stationPaths.Contains(sub, StringComparer.Ordinal))
            {
                stationPaths.Add(sub);
            }
        }
        return stationPaths
            .Select(FriendlyStation)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Not used directly (the <see cref="Read"/> override calls <see cref="ReadFieldsWithStations"/>
    /// so it can supply the shared station option set), but required by the base. Falls back to a
    /// single-option choice built from this entry's own station.
    /// </summary>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var sub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        var options = string.IsNullOrWhiteSpace(sub)
            ? Array.Empty<string>()
            : new[] { FriendlyStation(sub) };
        return ReadFieldsWithStations(props, options);
    }

    /// <summary>
    /// Builds the per-entry fields given the shared set of station option <paramref name="labels"/>.
    /// </summary>
    private static WorldMapField[] ReadFieldsWithStations(
        IList<FPropertyTag> props, IReadOnlyList<string> labels)
    {
        var sub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        var current = string.IsNullOrWhiteSpace(sub) ? null : FriendlyStation(sub);

        // ContainerInventories_ is an ArrayProperty; surface its element count (0 when absent).
        var inventoryCount = props.FindByPrefix(ContainerInventoriesPrefix)?.Property is ArrayProperty arr
            ? arr.Value?.Length ?? 0
            : 0;

        return new[]
        {
            WorldMapField.Choice(LastStationFieldId, "Last station", current, labels,
                hint: "Re-park this tram at the chosen station. Options are the stations the "
                    + "save's trams currently occupy (empty stations the level defines but no "
                    + "tram is parked at can't be listed from the save alone)."),
            WorldMapField.ReadOnly("inventories", "Container inventories",
                inventoryCount.ToString(CultureInfo.InvariantCulture),
                hint: "Number of on-board storage containers attached to this tram."),
        };
    }

    /// <summary>
    /// Rewrites only the <c>SubPathString</c> of the tram's <c>LastStation_</c> soft-object path
    /// (PackageName/AssetName are constant and left alone). The value is expected to have already
    /// been validated against the station option set by <see cref="SetField"/>; the
    /// friendly/full-path round-trip here keeps it correct whether called from that override or
    /// (defensively) the base path.
    /// </summary>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, LastStationFieldId, StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"unknown or read-only field '{fieldId}' (editable: {LastStationFieldId}).");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return WorldEditResult.Failure("a station must be supplied.");
        }

        // Map the chosen label (friendly or full) back to the real SubPathString to write.
        var wantedSubPath = ToSubPath(value.Trim());
        var wantedLabel = FriendlyStation(wantedSubPath);

        var currentSub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        if (currentSub is null)
        {
            return WorldEditResult.Failure("the LastStation field is missing from this tram entry.");
        }

        if (string.Equals(FriendlyStation(currentSub), wantedLabel, StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.NoChange;
        }

        return WorldMapAccessor.SetSoftObjectSubPath(props, LastStationPrefix, wantedSubPath)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the LastStation field is missing from this tram entry.");
    }

    /// <summary>
    /// Turns a station SubPathString (<c>PersistentLevel.TramSystem_Station_C_9</c>) into the
    /// friendly label shown to the user (<c>TramSystem_Station_C_9</c>) by stripping a leading
    /// <c>PersistentLevel.</c> qualifier. Reversible via <see cref="ToSubPath"/>.
    /// </summary>
    private static string FriendlyStation(string subPath)
        => subPath.StartsWith(PersistentLevelPrefix, StringComparison.Ordinal)
            ? subPath[PersistentLevelPrefix.Length..]
            : subPath;

    /// <summary>
    /// Maps a chosen value (a friendly label or an already-full SubPathString) back to the full
    /// SubPathString to persist. Re-adds the <c>PersistentLevel.</c> qualifier when the value is
    /// a bare station name.
    /// </summary>
    private static string ToSubPath(string value)
        => value.Contains('.', StringComparison.Ordinal)
            ? value
            : PersistentLevelPrefix + value;
}
