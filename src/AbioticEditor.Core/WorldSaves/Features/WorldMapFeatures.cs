using System.Reflection;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// The registry of every <see cref="IWorldMapFeature"/>. Features are <b>discovered by
/// reflection</b> (any non-abstract <see cref="IWorldMapFeature"/> with a public parameterless
/// constructor in this assembly), so adding a new map editor is just dropping in a new class -
/// no central list to edit, no merge conflicts when several are added at once.
/// </summary>
public static class WorldMapFeatures
{
    /// <summary>All registered features, ordered by display name.</summary>
    public static IReadOnlyList<IWorldMapFeature> All { get; } = Discover();

    /// <summary>The feature with the given <see cref="IWorldMapFeature.Id"/>, or null.</summary>
    public static IWorldMapFeature? Find(string id)
        => All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="mapName"/> is owned by some feature (used to mark it modeled).</summary>
    public static bool IsKnownMap(string mapName)
        => All.Any(f => string.Equals(f.MapName, mapName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The features whose map is actually present in <paramref name="save"/>.</summary>
    public static IReadOnlyList<IWorldMapFeature> ApplicableTo(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return All.Where(f => f.AppliesTo(save)).ToArray();
    }

    private static IReadOnlyList<IWorldMapFeature> Discover()
    {
        var contract = typeof(IWorldMapFeature);
        var found = new List<IWorldMapFeature>();
        foreach (var type in contract.Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !contract.IsAssignableFrom(type))
            {
                continue;
            }
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }
            if (Activator.CreateInstance(type) is IWorldMapFeature feature)
            {
                found.Add(feature);
            }
        }
        return found
            .OrderBy(f => f.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }
}
