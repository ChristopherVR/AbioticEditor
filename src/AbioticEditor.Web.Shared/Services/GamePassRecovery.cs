using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The ways back from a Game Pass world that has gone wrong: the game's own spare copy of a world,
/// and save data Xbox dropped off its list while leaving it on the disk.
/// </summary>
/// <remarks>
/// <para>Deliberately a static helper over the open workspace rather than an injected service.
/// Every screen that needs it already has the workspace, and a new registration would have to be
/// added to each host that composes these screens - one of which is out of reach of the code that
/// would depend on it, so a missing registration would surface as a screen that fails to appear
/// rather than as a build error.</para>
///
/// <para>Both operations end by reopening the container they touched. That is not a nicety: the
/// editor works on a temp copy it unpacked when the world was opened, and after a restore that copy
/// holds the world as it was BEFORE the restore. Leaving it in place would mean the next SAVE
/// packed the old world straight back over the copy the player had just recovered.</para>
/// </remarks>
public static class GamePassRecovery
{
    /// <summary>The game's spare copies of the worlds in the open Game Pass save (empty for any
    /// other kind of save, and for a folder that cannot be read).</summary>
    public static IReadOnlyList<GamePassWorldBackup> Backups(SaveWorkspaceSessionService workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Current?.GamePass?.Set is not { } set) return [];
        try
        {
            return set.WorldBackups();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            EditorLog.Warn("GamePass", $"Could not list the game's backup copies: {ex.Message}");
            return [];
        }
    }

    /// <summary>Save data in the open Game Pass folder that its container list no longer points at.</summary>
    public static IReadOnlyList<WgsOrphanedContainer> Orphans(SaveWorkspaceSessionService workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Current?.GamePass?.Set is not { } set) return [];
        try
        {
            return set.OrphanedContainers();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            EditorLog.Warn("GamePass", $"Could not list leftover save data: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// True when the leftover can be put back on its own. Core declines to suggest a name when the
    /// data does not say which world it is, and when a live world already holds that name (which
    /// makes the leftover an older copy of a world that is not missing at all). Inventing a name in
    /// either case would register a container whose name disagrees with the world inside it, so the
    /// screen offers nothing instead and says why.
    /// </summary>
    public static bool CanPutBack(WgsOrphanedContainer orphan)
    {
        ArgumentNullException.ThrowIfNull(orphan);
        return !string.IsNullOrWhiteSpace(orphan.SuggestedContainerName);
    }

    /// <summary>
    /// Replaces the live world with the game's spare copy of it, then reopens that world so the
    /// editor is showing the copy that is now on disk.
    /// </summary>
    /// <returns>The world that was restored.</returns>
    public static async Task<string> RestoreAsync(
        SaveWorkspaceSessionService workspace,
        GamePassWorldBackup backup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(backup);
        if (workspace.Current?.GamePass is not { } session)
            throw new InvalidOperationException("No Game Pass save is open.");

        var folder = session.WgsFolder;
        var world = await Task.Run(
            () => session.Set.RestoreWorldFromBackup(backup.ContainerName), cancellationToken).ConfigureAwait(false);
        await ReopenAsync(workspace, folder, backup.LiveContainerName, cancellationToken).ConfigureAwait(false);
        return world;
    }

    /// <summary>
    /// Puts one leftover back into the container list, then opens it, so the player can see for
    /// themselves that the world they lost is the one that came back.
    /// </summary>
    /// <returns>The container it was put back under.</returns>
    public static async Task<string> PutBackAsync(
        SaveWorkspaceSessionService workspace,
        WgsOrphanedContainer orphan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(orphan);
        if (workspace.Current?.GamePass is not { } session)
            throw new InvalidOperationException("No Game Pass save is open.");

        var folder = session.WgsFolder;
        var container = await Task.Run(
            () => session.Set.RecoverOrphanedWorld(orphan), cancellationToken).ConfigureAwait(false);
        await ReopenAsync(workspace, folder, container, cancellationToken).ConfigureAwait(false);
        return container;
    }

    /// <summary>True when the editor's own repair is the remedy for what is wrong with the open
    /// save, as opposed to something only the player can do (closing the game, waiting for a sync).</summary>
    public static bool RepairIsTheRemedy(SaveWorkspaceSessionService workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Current?.GamePass?.Set is { } set && (set.IsMidSync || set.NeedsAttention);
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

    /// <summary>A byte count in the same shape the save list uses, so two sizes on one screen
    /// cannot be read as two different units.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes / 1024d / 1024d:0.0} MB",
    };

    /// <summary>
    /// Reopens the container that was just put back, from a freshly read folder.
    /// </summary>
    /// <remarks>
    /// The open set has already written the folder, so its own view is current, but the unpacked
    /// working copy behind the editor is not - see the class remarks. Reopening rebuilds both.
    /// </remarks>
    private static async Task ReopenAsync(
        SaveWorkspaceSessionService workspace,
        string wgsFolder,
        string container,
        CancellationToken cancellationToken)
    {
        var source = workspace.Current?.Source;
        await workspace.OpenGamePassAsync(wgsFolder, container, source, cancellationToken).ConfigureAwait(false);
        if (workspace.Current?.Saves is { Count: > 0 } saves)
        {
            await workspace.SelectAsync(saves[0].Path, cancellationToken).ConfigureAwait(false);
        }
    }
}
