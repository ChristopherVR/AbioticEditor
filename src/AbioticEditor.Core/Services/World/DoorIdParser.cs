using System.Globalization;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Helpers for unpacking the long UE actor path used as <c>WorldDoor.Id</c>.
/// </summary>
public static class DoorIdParser
{
    /// <summary>
    /// Parses <c>/Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_0</c>
    /// into <c>("Facility", "SimpleDoor_ParentBP_C_0")</c>.
    ///
    /// Also accepts a live-editing id, the game's own <c>GetFullName()</c> form:
    /// <c>"SimpleDoor_ParentBP_C /Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_9"</c>
    /// (class name, a space, then the same actor-path layout as above) - the leading class-name
    /// token is stripped before parsing so both id shapes land on the same (map, actor) pair.
    ///
    /// If the input doesn't follow the conventional UE actor-path layout, the
    /// best-effort fallback is <c>("", id)</c>: an empty map, and the entire
    /// input as the actor name. <c>null</c> is never returned.
    /// </summary>
    public static (string Map, string Actor) Parse(string id)
    {
        if (string.IsNullOrEmpty(id)) return (string.Empty, string.Empty);

        // Live full-name form: "<ClassName> <ObjectPath>". A saved file's id never contains a
        // space, so seeing one before the path is unambiguous.
        var spaceIdx = id.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var afterSpace = id[(spaceIdx + 1)..];
            if (afterSpace.StartsWith('/')) id = afterSpace;
        }

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
