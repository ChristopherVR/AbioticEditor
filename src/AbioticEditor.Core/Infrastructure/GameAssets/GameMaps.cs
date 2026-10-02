using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Engine;

namespace AbioticEditor.Core.Assets;

/// <summary>Small helpers for reading the game's level files.</summary>
public static class GameMaps
{
    /// <summary>
    /// A map's world object (its streaming entries and persistent level). Found by name first: the
    /// world is named after its map, and reading only it is quick, where walking the exports in order
    /// builds every actor before it (tens of seconds for the large maps, under the shared game-files
    /// lock). Falls back to that walk for a map whose world is named differently.
    /// </summary>
    public static UWorld? WorldOf(IPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var name = package.Name;
        name = name[(name.LastIndexOf('/') + 1)..];
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0) name = name[..dot];
        if (name.Length > 0 && package.GetExportIndex(name, StringComparison.OrdinalIgnoreCase) >= 0
            && package.GetExportOrNull(name, StringComparison.OrdinalIgnoreCase) is UWorld named)
        {
            return named;
        }
        return package.GetExports().OfType<UWorld>().FirstOrDefault();
    }
}
