namespace AbioticEditor.Plugins.Scene;

/// <summary>
/// Optional for an <see cref="ISceneModelProvider"/>: reading everything that is slow the first time
/// (a level's index of what stands where) ahead of use, once per game version, so the 3D view never
/// waits on it. The host offers this to the player up front and runs it in the background.
/// </summary>
public interface ISceneModelPreparation
{
    /// <summary>How many pieces of work are still to do; 0 when everything is ready.</summary>
    int RemainingToPrepare();

    /// <summary>
    /// Does the remaining work one piece at a time, reporting how many are done of how many. Stops early
    /// when cancelled; what was finished stays done.
    /// </summary>
    void Prepare(IProgress<(int Done, int Total)> progress, CancellationToken cancellationToken);
}
