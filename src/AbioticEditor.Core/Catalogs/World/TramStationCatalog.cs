namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The little station knowledge the repository has: which named station-unlock world flags exist,
/// and how a saved station reference is spelled. It is NOT a station catalog and is not used to
/// offer destinations.
/// </summary>
/// <remarks>
/// A tram stores its last stop as a reference to a level actor such as
/// <c>PersistentLevel.TramSystem_Station_C_9</c>. The instance numbers are assigned by the level
/// asset and carry no name in any save or catalog. The only station names the game data gives us are
/// the six <c>Tram_*</c> world flags below (plus <c>Pens_OpenTramStation</c>), which gate stations by
/// place name. Nothing links a flag to an instance number without reading the level, so the editor
/// cannot say which <c>TramSystem_Station_C_N</c> is "Containment". Fixture evidence and the
/// extraction work needed are in docs/research/world-and-placed-object-state.md.
/// </remarks>
public static class TramStationCatalog
{
    /// <summary>The level actor class name prefix of every station reference seen in saves.</summary>
    public const string StationActorPrefix = "TramSystem_Station_C_";

    /// <summary>
    /// World flags that unlock a named station, with the place token taken literally from the flag name
    /// (no friendlier label is asserted anywhere in the game data we hold).
    /// </summary>
    public static IReadOnlyList<(string Flag, string Place)> UnlockFlags { get; } =
    [
        ("Pens_OpenTramStation", "Pens"),
        ("Tram_Containment", "Containment"),
        ("Tram_DamOffice", "Dam Office"),
        ("Tram_DF_R4", "DF R4"),
        ("Tram_MFWest_Office1", "MFWest Office1"),
        ("Tram_Mines_Office1", "Mines Office1"),
        ("Tram_Plant", "Plant"),
    ];

    /// <summary>
    /// The instance number of a saved station reference (<c>TramSystem_Station_C_9</c> or
    /// <c>PersistentLevel.TramSystem_Station_C_9</c> gives 9), or null when the text is not one.
    /// </summary>
    public static int? StationNumber(string? subPath)
    {
        if (string.IsNullOrEmpty(subPath)) return null;
        var i = subPath.LastIndexOf(StationActorPrefix, StringComparison.Ordinal);
        if (i < 0) return null;
        return int.TryParse(subPath.AsSpan(i + StationActorPrefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
