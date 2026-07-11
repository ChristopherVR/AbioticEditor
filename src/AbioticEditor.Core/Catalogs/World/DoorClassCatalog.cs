using System.Globalization;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Static metadata about a known door blueprint class, used by the UI to render
/// friendly names and hint at why a door might be locked.
/// </summary>
/// <param name="ClassName">
/// Raw UE class name as it appears in <c>WorldDoor.Id</c> (with the trailing
/// <c>_C</c>), e.g. <c>SimpleDoor_ParentBP_C</c>. For static-mesh-based security
/// doors this is the literal <c>StaticMeshActor</c>.
/// </param>
/// <param name="DisplayName">Short human label, e.g. "Hinged Door".</param>
/// <param name="RequiredKeyId">
/// Item id needed to unlock, when known. Most doors in AF gate on a world flag
/// or a generic skill check, not a specific key, so this is usually <c>null</c>.
/// </param>
/// <param name="LockKind">
/// One of <c>Keycard</c>, <c>Key</c>, <c>Part</c>, <c>Flag</c>, <c>None</c>, or
/// <c>Unknown</c>. <c>Flag</c> means the door is gated by a world-flag toggle
/// rather than an inventory item.
/// </param>
public sealed record DoorClassInfo(
    string ClassName,
    string DisplayName,
    string? RequiredKeyId,
    string LockKind);

/// <summary>
/// Curated catalog of every door blueprint class observed in AF world saves so
/// far. Built by surveying the 42 <c>WorldSave_*.sav</c> fixtures across the
/// game's maps plus inspecting <c>AbioticFactor/Content/Blueprints/Doors/</c>
/// blueprints in the paks (see <c>DoorProbeTests</c>).
///
/// Lock kinds are best-effort labels derived from the blueprint family - full
/// blueprint K2Node inspection would be needed to resolve specific keycards or
/// part requirements per instance.
/// </summary>
public static class DoorClassCatalog
{
    private static readonly Dictionary<string, DoorClassInfo> _classes =
        new(StringComparer.Ordinal)
        {
            // SimpleDoor family - the standard hinged door used everywhere. The
            // ParentBP exposes WorldFlagToUnlock, so it can be gated by either a
            // flag or a key check depending on the specific placed instance.
            ["SimpleDoor_ParentBP_C"]            = new("SimpleDoor_ParentBP_C",            "Hinged Door",                 null, "Flag"),
            ["SimpleDoor_Bathroom_C"]            = new("SimpleDoor_Bathroom_C",            "Bathroom Door",               null, "None"),
            ["SimpleDoor_CinematicControlled_C"] = new("SimpleDoor_CinematicControlled_C", "Cinematic Door",              null, "None"),
            ["SimpleHatch_BP_C"]                 = new("SimpleHatch_BP_C",                 "Hatch",                       null, "Flag"),

            // SecurityDoor family - the big sliding security gates.
            ["SecurityDoor_C"]            = new("SecurityDoor_C",            "Security Door",              null, "Keycard"),
            ["SecurityDoor_Animated_C"]   = new("SecurityDoor_Animated_C",   "Animated Security Door",     null, "Keycard"),
            ["SecurityDoor_Small_C"]      = new("SecurityDoor_Small_C",      "Small Security Door",        null, "Keycard"),
            ["SecurityDoor_Shutters_C"]   = new("SecurityDoor_Shutters_C",   "Security Shutters",          null, "Keycard"),
            ["CinematicSecurityDoor_C"]   = new("CinematicSecurityDoor_C",   "Cinematic Security Door",    null, "None"),

            // BlastDoor family - heavy doors typically operated by a switch or
            // installable part, not an inventory key.
            ["BlastDoor_C"]               = new("BlastDoor_C",               "Blast Door",                 null, "Part"),
            ["BlastDoor_DarkwaterCave_C"] = new("BlastDoor_DarkwaterCave_C", "Darkwater Cave Blast Door",  null, "Flag"),
            ["BlastDoor_OfferingBox_C"]   = new("BlastDoor_OfferingBox_C",   "Offering Box",               null, "Part"),
            ["BlastDoor_ORD_Large_C"]     = new("BlastDoor_ORD_Large_C",     "Ordnance Blast Door (Large)",null, "Flag"),
            ["BlastDoor_ORD_Small_C"]     = new("BlastDoor_ORD_Small_C",     "Ordnance Blast Door (Small)",null, "Flag"),

            // Sliding doors and windows.
            ["SlidingDoor_BP_C"]            = new("SlidingDoor_BP_C",            "Sliding Door",          null, "Flag"),
            ["SlidingCellDoor_BP_C"]        = new("SlidingCellDoor_BP_C",        "Cell Door",             null, "Flag"),
            ["SlidingStorageDoor_BP_C"]     = new("SlidingStorageDoor_BP_C",     "Storage Door",          null, "Flag"),
            ["SlidingWindow_Vertical_BP_C"] = new("SlidingWindow_Vertical_BP_C", "Vertical Sliding Window", null, "None"),
            ["SlidingDoor_VOTV_ASO_C"]      = new("SlidingDoor_VOTV_ASO_C",      "VOTV Sliding Door",     null, "Flag"),

            // Specialty.
            ["VacDoor_BP_C"]              = new("VacDoor_BP_C",              "Vacuum Door",                null, "Keycard"),
            ["Hatch_PickupTailgate_BP_C"] = new("Hatch_PickupTailgate_BP_C", "Pickup Tailgate",            null, "None"),
            ["TramRailDoor_C"]            = new("TramRailDoor_C",            "Tram Rail Door",             null, "None"),

            // Security doors are sometimes registered as plain StaticMeshActors
            // (the IsDoorOpen-bool variant in SecurityDoorMap), so this is a
            // common entry - keep it explicit instead of falling through to the
            // "Unknown" default.
            ["StaticMeshActor"] = new("StaticMeshActor", "Security Door (Static)", null, "Keycard"),
        };

    /// <summary>Every door class we have first-party metadata for.</summary>
    public static IReadOnlyDictionary<string, DoorClassInfo> KnownClasses => _classes;

    /// <summary>
    /// Returns the catalog entry for <paramref name="className"/>, or a generic
    /// "Unknown"-lock-kind fallback that echoes the class name as the display.
    /// Never returns <c>null</c>.
    /// </summary>
    public static DoorClassInfo Lookup(string className)
    {
        if (_classes.TryGetValue(className, out var info)) return info;
        return new DoorClassInfo(className, className, null, "Unknown");
    }

    /// <summary>
    /// How a door's <see cref="DoorClassInfo.LockKind"/> works in-game, per the wiki
    /// (abioticfactor.wiki.gg: Keycards / Security Doors / Keys). Frontend-agnostic
    /// reference prose so any UI or the CLI can explain why a door is locked and what
    /// editing its state actually does.
    /// </summary>
    public static string LockExplanation(string lockKind) => lockKind switch
    {
        "Keycard" => "Keycard door: GATE security doors scan a keycard of sufficient " +
            "clearance. In-game these open with a keycard found placed in the world or, " +
            "far more commonly, by hacking the keypad with a Hacking Device of matching " +
            "tier. Forcing it here is the editor equivalent of a successful hack.",
        "Key" => "Keyed lock: opens with one specific physical key found in the world " +
            "(see REQUIRED KEY below for its in-game item and description).",
        "Part" => "Disabled mechanism: this door is missing a machine part (fuse, valve, " +
            "crank…) that must be installed in-game before it operates. Setting it open " +
            "here skips the part hunt.",
        "Flag" => "Story-controlled: the game opens this door itself when the matching " +
            "world flag is set (see the QUEST FLAGS tab). Editing the door state directly " +
            "can desync it from the story until the next trigger.",
        "None" => "Free door: no lock - its saved state only records whether someone left " +
            "it open, closed, or barricaded.",
        _ => "Unrecognized lock type - the saved state can still be edited safely.",
    };
}

/// <summary>
/// Friendly names for AF's <c>E_DoorStates</c> enum. The blueprint enum keeps
/// the UE editor's default <c>NewEnumerator{N}</c> identifiers - the actual
/// semantic labels live only in the blueprint graphs - so the mapping here is
/// curated based on the door state machine implied by SimpleDoor_ParentBP's
/// transition timeline and lock checks.
/// </summary>
public static class DoorStateNames
{
    // Ordered list of friendly names for E_DoorStates::NewEnumerator0..6.
    // The eighth member of the enum (E_MAX) is a UE-reserved sentinel, not a
    // real runtime value.
    private static readonly string[] _friendly =
    {
        "Closed",       // NewEnumerator0
        "Open",         // NewEnumerator1
        "Locked",       // NewEnumerator2
        "Opening",      // NewEnumerator3
        "Closing",      // NewEnumerator4
        "Jammed",       // NewEnumerator5
        "Broken",       // NewEnumerator6
    };

    /// <summary>The seven friendly state names in enum order.</summary>
    public static IReadOnlyList<string> AllFriendlyNames => _friendly;

    /// <summary>
    /// Maps a raw <c>E_DoorStates::NewEnumerator{N}</c> string to a friendly
    /// label like "Closed" or "Locked". Bare numeric strings like "0" are
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

/// <summary>
/// Helpers for unpacking the long UE actor path used as <c>WorldDoor.Id</c>.
/// </summary>
public static class DoorIdParser
{
    /// <summary>
    /// Parses <c>/Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_0</c>
    /// into <c>("Facility", "SimpleDoor_ParentBP_C_0")</c>.
    ///
    /// If the input doesn't follow the conventional UE actor-path layout, the
    /// best-effort fallback is <c>("", id)</c>: an empty map, and the entire
    /// input as the actor name. <c>null</c> is never returned.
    /// </summary>
    public static (string Map, string Actor) Parse(string id)
    {
        if (string.IsNullOrEmpty(id)) return (string.Empty, string.Empty);

        // Map portion: between "/Game/Maps/" and the first '.'.
        string map = string.Empty;
        const string mapsPrefix = "/Game/Maps/";
        var prefixIdx = id.IndexOf(mapsPrefix, StringComparison.Ordinal);
        if (prefixIdx >= 0)
        {
            var after = id[(prefixIdx + mapsPrefix.Length)..];
            var dot = after.IndexOf('.');
            if (dot > 0)
            {
                map = after[..dot];
            }
        }

        // Actor portion: everything after the last '.'.
        var lastDot = id.LastIndexOf('.');
        var actor = (lastDot >= 0 && lastDot < id.Length - 1) ? id[(lastDot + 1)..] : id;

        return (map, actor);
    }

    /// <summary>
    /// Returns just the blueprint class name from an actor id, with the trailing
    /// <c>_&lt;n&gt;</c> instance suffix stripped. E.g.
    /// <c>SimpleDoor_ParentBP_C_12</c> -> <c>SimpleDoor_ParentBP_C</c>.
    /// </summary>
    public static string ClassNameFromActor(string actor)
    {
        if (string.IsNullOrEmpty(actor)) return actor ?? string.Empty;

        var lastUs = actor.LastIndexOf('_');
        if (lastUs > 0 && lastUs < actor.Length - 1)
        {
            var tail = actor[(lastUs + 1)..];
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return actor[..lastUs];
            }
        }
        return actor;
    }
}
