using System.Text.RegularExpressions;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Pictures of the game's doors, buttons, breakable walls, elevators, trams, teleporters and resource
/// nodes, rendered from the game's own models by tools/thumbnails and shipped with the editor
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
        if (string.IsNullOrEmpty(actorPath) || !Available.TryGetValue(kind, out var classes)) return null;
        var cls = ClassOf(actorPath);
        return classes.Contains(cls) ? $"{Root}/{kind}/{cls}.webp" : null;
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
        "buttons" or "destructibles" or "elevators" or "trams" or "portals" or "resource-nodes" or "power-sockets" => featureId,
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
