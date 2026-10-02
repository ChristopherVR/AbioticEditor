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
    /// <summary>
    /// The region save the target stands in, by file name (<c>WorldSave_Facility_Labs.sav</c>), when it
    /// is not the open save: "Show in 3D" opens that save first. A trader or containment cell listed in
    /// the story save, for example.
    /// </summary>
    public string? SaveFileName { get; init; }

    /// <summary>
    /// The level the target is placed in (<c>Facility_MFWest</c>), when its save is not known by file name:
    /// the save is that level's region save, or the nearest broader one (<c>Facility</c>).
    /// </summary>
    public string? LevelName { get; init; }

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
public sealed class WorldLocator(Func<WorldLocateTarget, Task> show, Func<WorldLocateTarget, bool>? canShow = null)
{
    public Task ShowAsync(WorldLocateTarget target) => show(target);

    /// <summary>Whether "Show in 3D" can show this target (its save is open or can be opened).</summary>
    public bool CanShow(WorldLocateTarget target) => canShow?.Invoke(target) ?? true;
}
