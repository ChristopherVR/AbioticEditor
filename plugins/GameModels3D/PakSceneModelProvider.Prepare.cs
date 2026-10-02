using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Reading every level's index ahead of use (<see cref="ISceneModelPreparation"/>). An index (which
/// meshes stand where, in which part of the level) takes from a second to a few minutes to read from
/// the game's files the first time, and the 3D view used to wait on it the first time an area was
/// shown, and again after every game update. Done up front, once, the view finds them all on disk.
/// </summary>
internal sealed partial class PakSceneModelProvider : ISceneModelPreparation
{
    /// <summary>The game's own level files (its Maps folder), leaving out the main menu.</summary>
    private List<string> MapsToPrepare() => _mapsByName.Value.Values
        .Where(path => path.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)
                       && !System.IO.Path.GetFileName(path).StartsWith("MainMenu", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public int RemainingToPrepare()
    {
        if (!IsAvailable) return 0;
        return MapsToPrepare().Count(map => !_levels.ContainsKey(map) && !File.Exists(LevelCachePath(map)));
    }

    public void Prepare(IProgress<(int Done, int Total)> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!IsAvailable) return;
        var todo = MapsToPrepare().Where(map => !_levels.ContainsKey(map) && !File.Exists(LevelCachePath(map))).ToList();
        progress.Report((0, todo.Count));
        for (var i = 0; i < todo.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var map = todo[i];
            try
            {
                _levels[map] = LoadOrBuildLevel(map);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _host.Log.Warn($"Could not read level {map} while preparing: {ex.Message}");
            }
            progress.Report((i + 1, todo.Count));
        }
    }
}
