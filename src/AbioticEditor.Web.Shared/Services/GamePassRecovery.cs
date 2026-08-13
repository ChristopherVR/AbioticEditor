using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The one repair the editor can carry out on a Game Pass save by itself: putting back parts of it
/// that describe themselves wrongly, or that point at data under a name Xbox never finished writing.
/// </summary>
/// <remarks>
/// <para>Deliberately a static helper over the open workspace rather than an injected service.
/// Every screen that needs it already has the workspace, and a new registration would have to be
/// added to each host that composes these screens, one of which is out of reach of the code that
/// would depend on it, so a missing registration would surface as a screen that fails to appear
/// rather than as a build error.</para>
///
/// <para>The heavier recovery routes - restoring the game's own spare copy of a world, or putting
/// back save data Xbox dropped off its list - live on the command line only. They are rare, they
/// are destructive in ways that need explaining, and putting them beside the save list meant every
/// player met a "rescue a world" panel while nothing was wrong.</para>
/// </remarks>
public static class GamePassRecovery
{
    /// <summary>
    /// True when a repair would actually change something about the open save.
    /// </summary>
    /// <remarks>
    /// Asks what a repair would do rather than whether the save looks unwell. Those are different
    /// questions, and answering the second one is how the editor came to offer a repair for an
    /// unresolved cloud conflict - which only Xbox can settle - and then report that it had fixed
    /// nothing.
    /// </remarks>
    public static bool RepairIsTheRemedy(SaveWorkspaceSessionService workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Current?.GamePass is { } gamePass
            && GamePassSaveSet.PartsNeedingRepair(gamePass.WgsFolder).Count > 0;
    }

    /// <summary>Repairs the open Game Pass save off the render thread. Returns what it repaired.</summary>
    public static Task<IReadOnlyList<string>> RepairAsync(
        SaveWorkspaceSessionService workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Current?.GamePass?.Set is { } set
            ? Task.Run(set.RepairMidSync, cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>([]);
    }
}
