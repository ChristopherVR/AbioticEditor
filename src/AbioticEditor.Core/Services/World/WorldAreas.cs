namespace AbioticEditor.Core.WorldSaves;

/// <summary>One area save a base can be moved to or brought from.</summary>
/// <param name="World">The world's folder name.</param>
/// <param name="Area">The area's name (from its save file: "Facility", "V Alps").</param>
/// <param name="FilePath">The area's save file.</param>
/// <param name="IsThisWorld">True for another area of the world the list was made for.</param>
public sealed record WorldArea(string World, string Area, string FilePath, bool IsThisWorld);

/// <summary>
/// The areas of the player's worlds on this computer that keep their own built pieces: every world
/// folder next to a given one, each area save in it except the sectors of another area (a save named
/// <c>Facility_Dam</c> is a sector of <c>Facility</c>: what is built there is kept in the Facility
/// save) and the story save.
/// </summary>
public static class WorldAreas
{
    /// <summary>
    /// The areas of <paramref name="worldFolder"/>'s world and of every world next to it, leaving out
    /// <paramref name="excludeFile"/>. The same area of the other worlds comes first (the usual move),
    /// then this world's other areas, then the rest.
    /// </summary>
    public static IReadOnlyList<WorldArea> List(string worldFolder, string? excludeFile = null, Action<string>? warn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldFolder);
        var found = new List<WorldArea>();
        try
        {
            var here = Path.GetFullPath(worldFolder);
            var worlds = Path.GetDirectoryName(here) is { Length: > 0 } parent && Directory.Exists(parent)
                ? Directory.EnumerateDirectories(parent).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList()
                : [here];
            if (!worlds.Any(w => string.Equals(Path.GetFullPath(w), here, StringComparison.OrdinalIgnoreCase))) worlds.Insert(0, here);
            var excluded = excludeFile is null ? null : Path.GetFullPath(excludeFile);
            foreach (var world in worlds)
            {
                var isHere = string.Equals(Path.GetFullPath(world), here, StringComparison.OrdinalIgnoreCase);
                foreach (var (area, file) in BuildAreas(world))
                {
                    if (excluded is not null && string.Equals(Path.GetFullPath(file), excluded, StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add(new WorldArea(Path.GetFileName(world), area, file, isHere));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warn?.Invoke($"Could not list the areas: {ex.Message}");
        }
        var mine = excludeFile is null ? "WorldSave_Facility.sav" : Path.GetFileName(excludeFile);
        return found
            .OrderBy(a => !a.IsThisWorld && string.Equals(Path.GetFileName(a.FilePath), mine, StringComparison.OrdinalIgnoreCase) ? 0 : a.IsThisWorld ? 1 : 2)
            .ToList();
    }

    /// <summary>The area saves of one world folder that keep their own built pieces (not the sectors of another area).</summary>
    public static IEnumerable<(string Area, string File)> BuildAreas(string world)
    {
        var names = Directory.EnumerateFiles(world, "WorldSave_*.sav")
            .Select(f => (Name: Path.GetFileNameWithoutExtension(f)["WorldSave_".Length..], File: f))
            .Where(a => !a.Name.Equals("MetaData", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var all = names.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, file) in names.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (all.Any(other => !other.Equals(name, StringComparison.OrdinalIgnoreCase) && name.StartsWith(other + "_", StringComparison.OrdinalIgnoreCase))) continue;
            yield return (name.Replace('_', ' '), file);
        }
    }
}
