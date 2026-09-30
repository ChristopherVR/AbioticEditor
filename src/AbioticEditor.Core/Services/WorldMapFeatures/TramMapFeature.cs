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
/// <para><b>Which stations a tram can be re-parked at.</b> Only the stations on that tram's own
/// line. The Facility is ten separate lines with one tram each (see <see cref="TramNetworkCatalog"/>,
/// read from the level's rail actors), so a tram cannot stop at a station on another line.
/// A tram the catalog does not know (a newer game build, a modded map) falls back to the stations
/// the save's trams currently occupy, which may include stations on other lines; the hint says so.
/// The tram's current station is always offered, even if the catalog does not list it, so a save is
/// never shown a choice it cannot represent.</para>
///
/// <para><b>What complete destinations would need (research, no code path yet).</b> Fixture evidence
/// (docs/reference/research/world-and-placed-object-state.md): across the four Facility saves the
/// occupied-station option set differs from world to world (17 distinct
/// <c>TramSystem_Station_C_N</c> numbers overall, only 10 trams per save), so the true station set
/// is larger than any one save shows. The instance numbers are assigned by the level asset and no
/// save or catalog names them. Getting the full list needs the game install: read the
/// <c>Facility</c> level package (<c>AbioticFactor/Content/Maps/Facility.umap</c>), enumerate every
/// <c>TramSystem_Station_C</c> export and its location, read the rail/track actors to learn which
/// stations connect, and label each station from the level or a related <c>Tram_*</c> flag
/// (<see cref="TramStationCatalog.UnlockFlags"/>, the only station names the repository holds).
/// Until then the option set stays the occupied stations, because offering an instance number the
/// running build does not have would write an invalid reference.</para>
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
        "Each tram runs on one route between a few stops. Choose where a tram is parked; only "
        + "the stops on its own route are offered.";

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
        // The stations the save's trams occupy: the fallback option set for a tram whose line
        // the catalog does not know.
        var occupied = GatherStationLabels(save);

        var list = new List<(WorldMapEntry Entry, int Order)>();
        var ordinal = 0;
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            ordinal++;
            var line = TramNetworkCatalog.LineFor(entry.Key);
            list.Add((new WorldMapEntry(
                entry.Key,
                LabelFor(ordinal, entry.Key, entry.Props),
                ReadFieldsWithStations(entry.Props, OptionsFor(entry.Key, entry.Props, occupied), line is not null)),
                // Routes first, then the containment lift, then trams the table does not know.
                line is null ? 2 : line.IsLift ? 1 : 0));
        }
        return list
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Entry.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .ToList();
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
            var options = OptionsFor(entryKey, props, GatherStationLabels(save));
            var check = ResolveChoice(value, options, out var resolvedLabel);
            if (check.IsError)
            {
                return check;
            }
            // Stop names repeat across routes ("The Office Sector" is on six), so a label is turned
            // into a station through this tram's own route, never looked up globally.
            if (TramNetworkCatalog.LineFor(entryKey) is { } line
                && TramNetworkCatalog.StationForLabel(line, resolvedLabel) is { } station)
            {
                return ApplyField(props, fieldId, StationSubPath(station));
            }
            return ApplyField(props, fieldId, resolvedLabel);
        }

        return ApplyField(props, fieldId, value);
    }

    /// <summary>
    /// A tram is named after the route it runs ("The Office Sector ↔ Hydroplant"), the way a player
    /// knows it; there is one tram per route. A tram the table does not know is numbered.
    /// </summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
        => TramNetworkCatalog.LineFor(key) is { } line
            ? TramNetworkCatalog.RouteName(line)
            : $"Tram {ordinal}";

    /// <summary>
    /// The stations this tram may be re-parked at: its own line when the catalog knows the tram,
    /// otherwise <paramref name="occupied"/>. The tram's current station is always included.
    /// </summary>
    private static IReadOnlyList<string> OptionsFor(string tramKey, IList<FPropertyTag> props, IReadOnlyList<string> occupied)
    {
        if (TramNetworkCatalog.LineFor(tramKey) is not { } line) return occupied;
        // In route order, so the list reads like the line itself.
        var labels = line.Stations.Select(s => TramNetworkCatalog.StopLabel(line, s)).ToList();
        var sub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            var current = FriendlyStation(sub);
            if (!labels.Contains(current, StringComparer.OrdinalIgnoreCase)) labels.Add(current);
        }
        return labels;
    }

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
        return ReadFieldsWithStations(props, options, lineKnown: false);
    }

    /// <summary>
    /// Builds the per-entry fields given the shared set of station option <paramref name="labels"/>.
    /// </summary>
    private static WorldMapField[] ReadFieldsWithStations(
        IList<FPropertyTag> props, IReadOnlyList<string> labels, bool lineKnown)
    {
        var sub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        var current = string.IsNullOrWhiteSpace(sub) ? null : FriendlyStation(sub);

        // ContainerInventories_ is an ArrayProperty; surface its element count (0 when absent).
        var inventoryCount = props.FindByPrefix(ContainerInventoriesPrefix)?.Property is ArrayProperty arr
            ? arr.Value?.Length ?? 0
            : 0;

        var fields = new List<WorldMapField>
        {
            WorldMapField.Choice(LastStationFieldId, "Parked at", current, labels,
                hint: lineKnown
                    ? "The stop this tram waits at. A tram only runs on its own route, so only that "
                        + "route's stops are offered."
                    : "The stop this tram waits at. This tram's route is not known to the editor, so "
                        + "the list is every stop the save's trams are parked at, which can include "
                        + "stops on other routes. Pick a stop on this tram's own route."),
        };
        // Only shown when the tram carries storage; an empty count told players nothing.
        if (inventoryCount > 0)
        {
            fields.Add(WorldMapField.ReadOnly("inventories", "Storage on board",
                inventoryCount.ToString(CultureInfo.InvariantCulture),
                hint: "Number of storage containers attached to this tram."));
        }
        return [.. fields];
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
        if (!value.Contains('.', StringComparison.Ordinal)
            && !wantedSubPath.StartsWith(PersistentLevelPrefix + TramStationCatalog.StationActorPrefix, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure($"'{value}' does not name a single stop; several stops share that name.");
        }

        var currentSub = WorldMapAccessor.GetSoftObjectPath(props, LastStationPrefix)?.SubPath;
        if (currentSub is null)
        {
            return WorldEditResult.Failure("the LastStation field is missing from this tram entry.");
        }

        if (string.Equals(currentSub, wantedSubPath, StringComparison.Ordinal))
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
    {
        var bare = subPath.StartsWith(PersistentLevelPrefix, StringComparison.Ordinal)
            ? subPath[PersistentLevelPrefix.Length..]
            : subPath;
        return TramStationCatalog.StationNumber(bare) is { } number && bare.StartsWith(TramStationCatalog.StationActorPrefix, StringComparison.Ordinal)
            ? TramNetworkCatalog.Label(number)
            : bare;
    }

    /// <summary>
    /// Maps a chosen value (a friendly label or an already-full SubPathString) back to the full
    /// SubPathString to persist. Re-adds the <c>PersistentLevel.</c> qualifier when the value is
    /// a bare station name.
    /// </summary>
    private static string StationSubPath(int station)
        => string.Create(CultureInfo.InvariantCulture, $"{PersistentLevelPrefix}{TramStationCatalog.StationActorPrefix}{station}");

    private static string ToSubPath(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal)) return value;
        if (TramStationCatalog.StationNumber(value) is { } number && value.StartsWith(TramStationCatalog.StationActorPrefix, StringComparison.Ordinal))
            return StationSubPath(number);
        // A stop label ("Hydroplant"): the station whose label it is, when only one station has it.
        var matches = TramNetworkCatalog.Lines.SelectMany(l => l.Stations)
            .Where(s => string.Equals(TramNetworkCatalog.Label(s), value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? StationSubPath(matches[0]) : PersistentLevelPrefix + value;
    }
}
