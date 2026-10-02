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
    /// Does the remaining work one piece at a time, on the calling thread. Stops early when
    /// cancelled; what was finished stays done. Work the 3D view asks for meanwhile goes first.
    /// </summary>
    void Prepare(CancellationToken cancellationToken);

    /// <summary>How far <see cref="Prepare"/> has got; safe to read from any thread at any time.</summary>
    ScenePreparationProgress Progress { get; }
}

/// <summary>
/// A snapshot of preparing: pieces done of all, the piece being worked on (a level's name, or null)
/// and how far into it (steps done of all; 0 of 0 when that is not known yet).
/// </summary>
public sealed record ScenePreparationProgress(int Done, int Total, string? Current, int CurrentDone, int CurrentTotal)
{
    /// <summary>Nothing started yet.</summary>
    public static ScenePreparationProgress None { get; } = new(0, 0, null, 0, 0);

    /// <summary>The whole job as a fraction from 0 to 1, counting the piece in hand by how far into it.</summary>
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((Done + (CurrentTotal > 0 ? (double)CurrentDone / CurrentTotal : 0)) / Total, 0, 1);
}
