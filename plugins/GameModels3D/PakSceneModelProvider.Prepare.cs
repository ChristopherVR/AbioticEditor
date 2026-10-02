using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Reading every level's index ahead of use (<see cref="ISceneModelPreparation"/>). An index (which
/// meshes stand where, in which part of the level) takes from a second to a few minutes to read from
/// the game's files the first time, and the 3D view used to wait on it the first time an area was
/// shown, and again after every game update. Done up front, once, the view finds them all on disk.
/// The reads go through <see cref="LevelFor"/>, shared with the view's own reading, so a level the
/// view asks for meanwhile is read once, and the view's model and texture reads go first.
/// </summary>
internal sealed partial class PakSceneModelProvider : ISceneModelPreparation
{
    private string? _preparing;
    private int _preparedDone;
    private int _preparedTotal;

    /// <summary>The game's own level files (its Maps folder), leaving out the main menu.</summary>
    private List<string> MapsToPrepare() => _mapsByName.Value.Values
        .Where(path => path.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)
                       && !Path.GetFileName(path).StartsWith("MainMenu", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private bool NeedsPreparing(string map) => !_levels.ContainsKey(map) && !File.Exists(LevelCachePath(map));

    public int RemainingToPrepare() => IsAvailable ? MapsToPrepare().Count(NeedsPreparing) : 0;

    public ScenePreparationProgress Progress
    {
        get
        {
            var map = _preparing;
            var (stepDone, stepTotal) = map is not null && _levelSteps.TryGetValue(map, out var step) ? step : (0, 0);
            return new ScenePreparationProgress(_preparedDone, _preparedTotal,
                map is null ? null : Path.GetFileNameWithoutExtension(map), stepDone, stepTotal);
        }
    }

    public void Prepare(CancellationToken cancellationToken)
    {
        if (!IsAvailable) return;
        var todo = MapsToPrepare().Where(NeedsPreparing).ToList();
        _preparedDone = 0;
        _preparedTotal = todo.Count;
        try
        {
            foreach (var map in todo)
            {
                if (cancellationToken.IsCancellationRequested) return;
                _preparing = map;
                try
                {
                    LevelFor(map);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _host.Log.Warn($"Could not read level {map} while preparing: {ex.Message}");
                }
                _preparedDone++;
            }
        }
        finally
        {
            _preparing = null;
        }
    }
}
