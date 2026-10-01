using System.Globalization;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Friendly names for AF's <c>E_DoorStates</c> enum (simple, hinged doors). The enum keeps the UE
/// editor's default <c>NewEnumerator{N}</c> identifiers, but its asset
/// (<c>/Game/Blueprints/Data/E_DoorStates</c>) carries the designers' display names in
/// <c>DisplayNameMap</c>: Closed, OpenInwards, OpenOutwards, Destroyed, SmashInwards, SmashOutwards,
/// SlammedClosed. There is no "locked" state: locking is the door's key or one-way setting.
/// </summary>
public static class DoorStateNames
{
    // Ordered list of friendly names for E_DoorStates::NewEnumerator0..6.
    // The eighth member of the enum (E_MAX) is a UE-reserved sentinel, not a
    // real runtime value.
    private static readonly string[] _friendly =
    {
        "Closed",          // NewEnumerator0
        "OpenInwards",     // NewEnumerator1
        "OpenOutwards",    // NewEnumerator2
        "Destroyed",       // NewEnumerator3
        "SmashedInwards",  // NewEnumerator4 (the asset says SmashInwards)
        "SmashedOutwards", // NewEnumerator5 (SmashOutwards)
        "SlammedClosed",   // NewEnumerator6
    };

    /// <summary>
    /// True when the doorway is passable in this state: open either way, destroyed, or smashed in or
    /// out. Closed and slammed-closed doors (and unknown states) are not.
    /// </summary>
    public static bool IsOpen(string? rawEnumValue) => rawEnumValue is not null && ParseEnumIndex(rawEnumValue) is >= 1 and <= 5;

    /// <summary>The seven friendly state names in enum order.</summary>
    public static IReadOnlyList<string> AllFriendlyNames => _friendly;

    /// <summary>
    /// Maps a raw <c>E_DoorStates::NewEnumerator{N}</c> string to a friendly
    /// label like "Closed" or "OpenInwards". Bare numeric strings like "0" are
    /// also accepted. Unrecognised values fall back to "State {N}" if the
    /// numeric suffix parses, otherwise the input is echoed unchanged.
    /// </summary>
    public static string Friendly(string? rawEnumValue)
    {
        if (string.IsNullOrEmpty(rawEnumValue)) return "Unknown";

        var idx = ParseEnumIndex(rawEnumValue);
        if (idx is null)
        {
            Diagnostics.EditorLog.UnknownData("DoorState", rawEnumValue, "unparseable enum value");
            return rawEnumValue;
        }

        if (idx.Value >= 0 && idx.Value < _friendly.Length)
        {
            return _friendly[idx.Value];
        }
        Diagnostics.EditorLog.UnknownData("DoorState", rawEnumValue, "enumerator beyond known E_DoorStates - newer game version?");
        return $"State {idx.Value}";
    }

    /// <summary>
    /// The enumerator number behind a raw door-state value, or null when it doesn't
    /// follow any recognized form. Public so the UI can keep unknown (future-version)
    /// states selectable instead of silently overwriting them.
    /// </summary>
    public static int? TryParseIndex(string? raw)
        => string.IsNullOrEmpty(raw) ? null : ParseEnumIndex(raw);

    /// <summary>Number of door states this build knows friendly names for.</summary>
    public static int KnownStateCount => _friendly.Length;

    private static int? ParseEnumIndex(string raw)
    {
        // Accept "E_DoorStates::NewEnumerator3", "NewEnumerator3", or plain "3".
        const string marker = "NewEnumerator";
        var i = raw.IndexOf(marker, StringComparison.Ordinal);
        if (i >= 0)
        {
            var tail = raw[(i + marker.Length)..];
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }
        }
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare))
        {
            return bare;
        }
        return null;
    }
}
