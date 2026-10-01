using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>What a "Show in 3D" link points at.</summary>
public enum WorldLocateKind
{
    /// <summary>A placed object (deployed or level-placed) by its <c>DeployedObjectMap</c> key.</summary>
    PlacedObject,

    /// <summary>A door by its <c>WorldDoor.Id</c>; the view frames its marker.</summary>
    Door,

    /// <summary>A story character or pet by its <c>WorldNpc.Id</c>; the view frames its marker.</summary>
    Character,

    /// <summary>A level actor by its actor path (a button, a breakable wall, a resource node); placed where the level puts it.</summary>
    LevelActor,

    /// <summary>A saved position (a dropped item, a vehicle) in save space (centimetres).</summary>
    Point,
}

/// <summary>
/// Something another tab of the world editor wants shown in the 3D view. <paramref name="At"/> is a
/// save-space position used when the view has nothing better (a point, or a fallback for the others).
/// </summary>
public sealed record WorldLocateTarget(WorldLocateKind Kind, string Id, string Label, PlacedVector? At = null)
{
    public static WorldLocateTarget Placed(string key, string label) => new(WorldLocateKind.PlacedObject, key, label);

    public static WorldLocateTarget Door(string id, string label) => new(WorldLocateKind.Door, id, label);

    public static WorldLocateTarget Character(WorldNpc npc, string label)
        => new(WorldLocateKind.Character, npc.Id, label, npc.X != 0 || npc.Y != 0 || npc.Z != 0 ? new PlacedVector(npc.X, npc.Y, npc.Z) : null);

    public static WorldLocateTarget Point(string id, string label, double x, double y, double z)
        => new(WorldLocateKind.Point, id, label, new PlacedVector(x, y, z));

    /// <summary>
    /// A world-map entry by its key: a placed object's 32-character key, a level actor's path, or
    /// (for an outlet record) the object that owns the outlet. Null when the key names nothing in
    /// the world (a story trigger, a named inventory).
    /// </summary>
    public static WorldLocateTarget? ForMapKey(string key, string label)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.StartsWith("/Game/", StringComparison.Ordinal) && key.Contains(':', StringComparison.Ordinal))
            return new(WorldLocateKind.LevelActor, key, label);
        if (key.Length == 32 && IsHex(key)) return Placed(key, label);
        if (PlacedGroupReferenceAnalyzer.OwnerKeyOf(key) is { Length: 32 } owner) return Placed(owner, label);
        return null;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}

/// <summary>
/// Handed down to every world tab: whether the 3D view can be shown, and how to show something in it.
/// Null (no cascading value) where there is no 3D view, which hides every "Show in 3D" link.
/// </summary>
public sealed class WorldLocator(Func<WorldLocateTarget, Task> show)
{
    public Task ShowAsync(WorldLocateTarget target) => show(target);
}
