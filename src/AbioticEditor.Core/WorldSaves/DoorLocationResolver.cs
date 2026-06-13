using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Objects.Core.Math;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>An actor's world position, read from its cooked level package.</summary>
public sealed record DoorWorldLocation(double X, double Y, double Z);

/// <summary>
/// Resolves door world positions from the game's cooked sub-level packages. The save
/// stores door STATE only; the placement lives in the .umap the actor is baked into,
/// so the exact in-game spot can be read from the paks on demand. Results are cached
/// per map for the session (a sub-level load takes a moment the first time).
/// </summary>
public static class DoorLocationResolver
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, DoorWorldLocation>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// World positions of every actor in <paramref name="mapName"/> that carries a
    /// positioned component, keyed by actor instance name
    /// (e.g. <c>SimpleDoor_ParentBP_C_12</c>). Empty map name means the persistent
    /// Facility level. Returns an empty dictionary when the package can't be loaded
    /// (no game install, or a map renamed by a future game version - logged once).
    /// </summary>
    public static IReadOnlyDictionary<string, DoorWorldLocation> ForMap(GameAssetProvider provider, string? mapName)
    {
        var name = string.IsNullOrEmpty(mapName) ? "Facility" : mapName;
        return Cache.GetOrAdd(name, key => Load(provider, key));
    }

    /// <summary>The position of one actor, or null when the map or actor is unknown.</summary>
    public static DoorWorldLocation? Resolve(GameAssetProvider provider, string? mapName, string actorName)
        => ForMap(provider, mapName).TryGetValue(actorName, out var loc) ? loc : null;

    private static IReadOnlyDictionary<string, DoorWorldLocation> Load(GameAssetProvider provider, string mapName)
    {
        var result = new Dictionary<string, DoorWorldLocation>(StringComparer.OrdinalIgnoreCase);
        // Level packages need the explicit .umap extension; the .uasset default that
        // works for data tables does not resolve them.
        var packagePath = $"AbioticFactor/Content/Maps/{mapName}.umap";
        try
        {
            var pkg = provider.LoadPackageInternal(packagePath);

            // First pass: component positions, keyed by the component's owning actor
            // (its Outer). Cooked actors keep their transform on the root component's
            // RelativeLocation; for level-placed actors that is the world position.
            foreach (var lazy in pkg.ExportsLazy)
            {
                CUE4Parse.UE4.Assets.Exports.UObject? export;
                try
                {
                    export = lazy.Value;
                }
                catch
                {
                    continue; // tolerate per-export deserialization failures
                }
                if (export?.Outer is null) continue;

                var locTag = export.Properties.FirstOrDefault(p => p.Name.Text == "RelativeLocation");
                var value = locTag?.Tag?.GenericValue;
                if (value is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) value = ss.StructType;
                if (value is not FVector v) continue;

                var actor = export.Outer.Name.Text;
                // The root component wins; secondary positioned components (triggers,
                // meshes with offsets) must not overwrite it. Roots come first in
                // export order, so first-in stays.
                if (!result.ContainsKey(actor))
                {
                    result[actor] = new DoorWorldLocation(v.X, v.Y, v.Z);
                }
            }
            Diagnostics.EditorLog.Info(
                "DoorMap", $"Resolved {result.Count} actor position(s) from {mapName}.");
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn(
                "DoorMap", $"Could not load {packagePath} for door positions: {ex.Message}");
        }
        return result;
    }
}
