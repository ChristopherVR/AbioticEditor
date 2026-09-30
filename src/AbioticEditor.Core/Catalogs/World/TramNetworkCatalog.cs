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
    /// <summary>One line: its stations in the order the rails join them, and the tram that runs on it.</summary>
    /// <param name="Stations">Station numbers from one end of the line to the other.</param>
    /// <param name="Trams">The tram actor(s) on this line (one in the Facility).</param>
    /// <param name="IsLift">The line is the containment lift (<c>Tram_ContainmentLift_C</c>), not a tram.</param>
    public sealed record TramLine(IReadOnlyList<int> Stations, IReadOnlyList<string> Trams, bool IsLift = false);

    /// <summary>
    /// Every line in the Facility, stations in rail order (each rail's <c>Station1</c>/<c>Station2</c>
    /// chained end to end), ordered by their lowest station number.
    /// </summary>
    public static IReadOnlyList<TramLine> Lines { get; } =
    [
        new([0, 4, 6, 7, 8, 9], ["Tram_ParentBP_C_0"], IsLift: true),
        new([5, 1], ["Tram_ParentBP_C_1"]),
        new([2, 3], ["Tram_ParentBP_C_2"]),
        new([15, 17, 10], ["Tram_Default_C_4"]),
        new([11, 14, 12], ["Tram_Default_C_1"]),
        new([13, 20], ["Tram_Default_C_2"]),
        new([16, 18], ["Tram_Default_C_3"]),
        new([27, 19], ["Tram_Default_C_6"]),
        new([24, 21, 28], ["Tram_Default_C_7"]),
        new([23, 26, 22], ["Tram_Default_C_5"]),
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

    /// <summary>The line a station is on, or null for a station the table does not know.</summary>
    public static TramLine? LineOfStation(int station)
        => Lines.FirstOrDefault(line => line.Stations.Contains(station));

    /// <summary>
    /// How a player would name a stop on its line: the level's station name, with the stop number
    /// along the line added only when two stops on the same line share a name ("Cascade
    /// Laboratories (stop 3)"). Unnamed stops (the containment lift's) are "Stop 1", "Stop 2", ...
    /// </summary>
    public static string StopLabel(TramLine line, int station)
    {
        ArgumentNullException.ThrowIfNull(line);
        var index = IndexOf(line, station);
        var stop = index + 1;
        var name = StationName(station);
        if (string.IsNullOrEmpty(name) || index < 0)
            return string.Create(CultureInfo.InvariantCulture, $"Stop {(index < 0 ? station : stop)}");
        var shared = line.Stations.Count(s => string.Equals(StationName(s), name, StringComparison.Ordinal)) > 1;
        return shared ? string.Create(CultureInfo.InvariantCulture, $"{name} (stop {stop})") : name;
    }

    /// <summary>
    /// A station's label without a line in hand: its label on its own line, or "Station N" for a
    /// station the table does not know.
    /// </summary>
    public static string Label(int station)
        => LineOfStation(station) is { } line
            ? StopLabel(line, station)
            : string.Create(CultureInfo.InvariantCulture, $"Station {station}");

    /// <summary>The station on <paramref name="line"/> a <see cref="StopLabel"/> stands for, or null.</summary>
    public static int? StationForLabel(TramLine line, string? label)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (string.IsNullOrWhiteSpace(label)) return null;
        foreach (var station in line.Stations)
        {
            if (string.Equals(StopLabel(line, station), label.Trim(), StringComparison.OrdinalIgnoreCase)) return station;
        }
        return null;
    }

    /// <summary>
    /// What players call a line: the containment lift, or the places it runs between in rail order
    /// ("The Office Sector ↔ Cascade Laboratories"), repeated neighbours collapsed. A line that
    /// touches The Office Sector is written from there, the hub most lines share.
    /// </summary>
    public static string RouteName(TramLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.IsLift) return "Containment lift";
        var names = new List<string>();
        foreach (var name in line.Stations.Select(StationName))
        {
            if (string.IsNullOrEmpty(name) || (names.Count > 0 && names[^1] == name)) continue;
            names.Add(name);
        }
        if (names.Count == 0) return "Unnamed line";
        if (names[^1] == "The Office Sector" && names[0] != "The Office Sector") names.Reverse();
        return string.Join(" ↔ ", names);
    }

    private static int IndexOf(TramLine line, int station)
    {
        for (var i = 0; i < line.Stations.Count; i++)
        {
            if (line.Stations[i] == station) return i;
        }
        return -1;
    }
}
