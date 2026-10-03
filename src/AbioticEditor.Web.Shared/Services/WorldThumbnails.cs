using System.Text.RegularExpressions;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Pictures of the game's doors, buttons, breakable walls, elevators, trams, teleporters, resource
/// nodes, power sockets and player-placed objects, rendered from the game's own models by tools/thumbnails and shipped with the editor
/// (wwwroot/thumbs/&lt;kind&gt;/&lt;class&gt;.webp). One picture per kind: every <c>BlastDoor_C_*</c> shows
/// the <c>BlastDoor_C</c> picture. <see cref="Available"/> (generated next to the pictures) says which
/// exist, so a list never asks for one that is not there.
/// </summary>
public static partial class WorldThumbnails
{
    private const string Root = "_content/AbioticEditor.Web.Shared/thumbs";

    [GeneratedRegex(@"^(?<cls>.+_C)_\d+$")]
    private static partial Regex InstanceSuffix();

    /// <summary>The kind of a level actor, from its path or name: <c>...BlastDoor_C_11</c> is a <c>BlastDoor_C</c>.</summary>
    public static string ClassOf(string actorPath)
    {
        ArgumentNullException.ThrowIfNull(actorPath);
        var name = actorPath[(Math.Max(actorPath.LastIndexOf('.'), actorPath.LastIndexOf(':')) + 1)..];
        var match = InstanceSuffix().Match(name);
        return match.Success ? match.Groups["cls"].Value : name;
    }

    /// <summary>
    /// The picture for a level actor in a list (<paramref name="kind"/>: doors, buttons, destructibles,
    /// elevators, trams, portals, resource-nodes), or null when none was rendered for its kind.
    /// </summary>
    public static string? For(string kind, string? actorPath)
    {
        if (string.IsNullOrEmpty(actorPath)) return null;
        // A level actor drawn on its own (the trams, each painted its own colour) wins over its kind's
        // picture: those live in "<kind>-each", named after the actor.
        // Actor names repeat across levels (every level has an Elevator_ParentBP_C_1), so a picture
        // named after the level too (<map>__<actor>) is looked for first.
        var name = actorPath[(Math.Max(actorPath.LastIndexOf('.'), actorPath.LastIndexOf(':')) + 1)..];
        if (Available.TryGetValue(kind + "-each", out var each))
        {
            var colon = actorPath.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                var package = actorPath[..colon];
                var placed = $"{package[(package.LastIndexOf('.') + 1)..]}__{name}";
                if (each.Contains(placed)) return $"{Root}/{kind}-each/{placed}.webp";
            }
            if (each.Contains(name)) return $"{Root}/{kind}-each/{name}.webp";
        }
        if (!Available.TryGetValue(kind, out var classes)) return null;
        var cls = ClassOf(actorPath);
        if (classes.Contains(cls)) return $"{Root}/{kind}/{cls}.webp";
        var fallback = kind switch
        {
            "power-sockets" => "PowerSocket_ParentBP_C",
            "buttons" when cls.Contains("Keypad", StringComparison.OrdinalIgnoreCase) => "Button_Keypad_C",
            "buttons" when cls.Contains("Light", StringComparison.OrdinalIgnoreCase) => "Button_LightSwitch_C",
            "buttons" => "Button_Generic_C",
            "corpses" => "CharacterCorpse_Human_BP_C",
            _ => null,
        };
        return fallback is not null && classes.Contains(fallback) ? $"{Root}/{kind}/{fallback}.webp" : null;
    }

    /// <summary>
    /// The picture of a player-placed object's class (a teleporter pad, a sconce lamp, a plug strip, a
    /// battery, a locker), from its own model, or null when none was rendered. Accepts the class name
    /// (<c>Deployed_PlugStrip_C</c>) or its full path.
    /// </summary>
    public static string? ForPlaced(string? className)
    {
        if (string.IsNullOrEmpty(className)) return null;
        var cls = className[(className.LastIndexOf('.') + 1)..];
        return For("deployables", cls) ?? For("containers", cls);
    }

    /// <summary>The sconce lamp in its selected switch state.</summary>
    public static string? ForLampState(string? className, bool on)
        => className is null ? null : For("lamp-states", ClassOf(className) + (on ? "_on" : "_off"));

    /// <summary>
    /// The placed device a power outlet belongs to: an outlet on a player-built device (a plug strip,
    /// a battery) is keyed by the device's 32-digit id plus one outlet digit. Null for anything else
    /// (a wall socket in the level is keyed by its actor path).
    /// </summary>
    public static string? PowerOutletOwner(string? key)
    {
        if (key is not { Length: 33 } || !char.IsAsciiDigit(key[32])) return null;
        foreach (var c in key.AsSpan(0, 32)) if (!char.IsAsciiHexDigit(c)) return null;
        return key[..32];
    }

    /// <summary>
    /// A picture of where this particular level actor is (the level around it from above, it outlined
    /// and pinned), or null when none was rendered. Level actors stand in the same place in every
    /// world, so these ship with the editor (tools/thumbnails/places.mjs).
    /// </summary>
    public static string? PlaceOf(string? actorPath)
    {
        if (string.IsNullOrEmpty(actorPath)) return null;
        var colon = actorPath.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return null;
        var package = actorPath[..colon];
        var map = package[(package.LastIndexOf('.') + 1)..];
        var actor = actorPath[(Math.Max(actorPath.LastIndexOf('.'), colon) + 1)..];
        return Places.Contains($"{map}:{actor}") ? $"{Root}/places/{map}/{actor}.webp" : null;
    }

    /// <summary>The picture folder for a world-map feature id, or null for lists without pictures.</summary>
    public static string? KindOfFeature(string featureId) => featureId switch
    {
        "buttons" or "destructibles" or "elevators" or "trams" or "portals" or "resource-nodes" or "power-sockets" or "corpses" => featureId,
        _ => null,
    };

    /// <summary>The creature an NPC spawn point makes, from its name: <c>NPCSpawn_Peccary_C_12</c> spawns a Peccary.</summary>
    public static string? SpawnedCreature(string key)
    {
        var name = ClassOf(key);
        if (name.StartsWith("NPCSpawn_", StringComparison.OrdinalIgnoreCase)) name = name["NPCSpawn_".Length..];
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
        return name.Length == 0 || name.Equals("Generic", StringComparison.OrdinalIgnoreCase) ? null : name;
    }
}
