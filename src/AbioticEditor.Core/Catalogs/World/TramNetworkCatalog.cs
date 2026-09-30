using System.Globalization;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The Facility's tram network as the game's level defines it: ten separate lines, each with one
/// tram, and the stations on each line. A tram can only ever stop at a station on its own line, so
/// this is what limits the "last station" choice for a tram.
/// </summary>
/// <remarks>
/// <para>Read from <c>AbioticFactor/Content/Maps/Facility.umap</c> with the probe
/// <c>tests/AbioticEditor.Probes/TramLevelProbeTests.cs</c>. Each <c>TramSystem_Rail_C</c> actor joins
/// two stations (<c>Station1</c>, <c>Station2</c>); grouping stations by rail gives ten independent
/// lines, and each tram's <c>StatingStation</c> (sic, the game's spelling) puts it on one of them.
/// Station names are the level's own <c>StationName</c> text. Six stations on the first line have
/// none. The instance numbers are the same ones a save stores in <c>LastStation_</c>
/// (<c>PersistentLevel.TramSystem_Station_C_12</c>) and the tram actor names are the same as the
/// <c>TramMap</c> keys, so nothing here is guessed. <c>TramMapFeatureTests</c> checks every fixture
/// save against it: each tram's saved station must be on its own line.</para>
/// <para>A game update that adds a station or a line needs the probe re-run and this table
/// regenerated; a saved station this table does not know is still shown and kept, never dropped.</para>
/// </remarks>
public static class TramNetworkCatalog
{
    /// <summary>One line: the stations its rails connect, and the tram that runs on it.</summary>
    public sealed record TramLine(IReadOnlyList<int> Stations, IReadOnlyList<string> Trams);

    /// <summary>Every line in the Facility, ordered by their lowest station number.</summary>
    public static IReadOnlyList<TramLine> Lines { get; } =
    [
        new([0, 4, 6, 7, 8, 9], ["Tram_ParentBP_C_0"]),
        new([1, 5], ["Tram_ParentBP_C_1"]),
        new([2, 3], ["Tram_ParentBP_C_2"]),
        new([10, 15, 17], ["Tram_Default_C_4"]),
        new([11, 12, 14], ["Tram_Default_C_1"]),
        new([13, 20], ["Tram_Default_C_2"]),
        new([16, 18], ["Tram_Default_C_3"]),
        new([19, 27], ["Tram_Default_C_6"]),
        new([21, 24, 28], ["Tram_Default_C_7"]),
        new([22, 23, 26], ["Tram_Default_C_5"]),
    ];

    private static readonly Dictionary<int, string> StationNames = new()
    {
        [1] = "The Office Sector",
        [2] = "The Mines",
        [3] = "The Office Sector",
        [5] = "Manufacturing West",
        [10] = "Residence Sector",
        [11] = "The Office Sector",
        [12] = "Cascade Laboratories",
        [13] = "Hydroplant",
        [14] = "Cascade Laboratories",
        [15] = "The Office Sector",
        [16] = "Power Services",
        [17] = "Residence Sector",
        [18] = "The Office Sector",
        [19] = "Residence Sector",
        [20] = "The Office Sector",
        [21] = "Cloud Reactor",
        [22] = "Gale Reactor",
        [23] = "Power Services",
        [24] = "Power Services",
        [26] = "Dusk Reactor",
        [27] = "Manufacturing West",
        [28] = "Mist Reactor",
    };

    /// <summary>The actor name of a tram: the part after the last <c>.</c> of a <c>TramMap</c> key
    /// (<c>/Game/Maps/Facility.Facility:PersistentLevel.Tram_Default_C_1</c> gives <c>Tram_Default_C_1</c>).</summary>
    public static string ActorName(string? tramKey)
    {
        if (string.IsNullOrEmpty(tramKey)) return string.Empty;
        var dot = tramKey.LastIndexOf('.');
        return dot >= 0 && dot < tramKey.Length - 1 ? tramKey[(dot + 1)..] : tramKey;
    }

    /// <summary>The line this tram runs on, or null for a tram the table does not know.</summary>
    public static TramLine? LineFor(string? tramKey)
    {
        var actor = ActorName(tramKey);
        return Lines.FirstOrDefault(line => line.Trams.Contains(actor, StringComparer.Ordinal));
    }

    /// <summary>The level's name for a station, or null when it has none or is not known.</summary>
    public static string? StationName(int station)
        => StationNames.TryGetValue(station, out var name) ? name : null;

    /// <summary>
    /// A player-readable label for a station, always ending in its number because several stations
    /// share a name (five are called "The Office Sector"): <c>Cascade Laboratories, station 12</c>.
    /// </summary>
    public static string Label(int station)
        => StationName(station) is { Length: > 0 } name
            ? string.Create(CultureInfo.InvariantCulture, $"{name}, station {station}")
            : string.Create(CultureInfo.InvariantCulture, $"Station {station}");

    /// <summary>A short name for a line, from the distinct place names on it ("Cascade Laboratories / The Office Sector").</summary>
    public static string LineName(TramLine line)
    {
        var names = line.Stations.Select(StationName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? "Containment" : string.Join(" / ", names);
    }
}
