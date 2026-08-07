using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Applies the story-flag rules in <see cref="StoryFlagSync"/> to the sibling
/// <c>WorldSave_Facility.sav</c> through the host's own file system, so the same STORY and
/// TRADERS actions work in the desktop app and the browser.
/// </summary>
/// <remarks>
/// This is the one place outside a save session that writes a file the player did not pick, so
/// it deliberately does nothing when it cannot identify the facility save, and it goes through
/// <see cref="ISaveFileSystem.WriteAllBytesAsync"/> - which keeps the <c>.bak</c> - rather than
/// writing bytes itself.
/// </remarks>
public sealed class StoryFlagSyncService(ISaveFileSystem files, SaveWorkspaceSessionService workspace)
{
    /// <summary>Adds arbitrary world flags (trader gating, stock unlocks) to the facility save.</summary>
    public Task<(int Count, string Message)> AddFlagsAsync(
        string metadataSavePath, IReadOnlyCollection<string> flagsToAdd, CancellationToken cancellationToken = default)
        => ApplyAsync(metadataSavePath, facility => StoryFlagSync.PlanAddFlags(facility, flagsToAdd), cancellationToken);

    /// <summary>Adds every chapter trigger flag up to and including <paramref name="chapterRow"/>.</summary>
    public Task<(int Count, string Message)> SyncToChapterAsync(
        string metadataSavePath, string chapterRow, CancellationToken cancellationToken = default)
        => ApplyAsync(metadataSavePath, facility => StoryFlagSync.PlanSyncToChapter(facility, chapterRow), cancellationToken);

    /// <summary>Removes the chapter and quest flags that come after <paramref name="chapterRow"/>.</summary>
    public Task<(int Count, string Message)> ClearForwardFlagsAsync(
        string metadataSavePath, string chapterRow, CancellationToken cancellationToken = default)
        => ApplyAsync(metadataSavePath, facility => StoryFlagSync.PlanClearForwardFlags(facility, chapterRow), cancellationToken);

    private async Task<(int Count, string Message)> ApplyAsync(
        string metadataSavePath, Func<WorldSaveData, StoryFlagSync.FlagPlan> plan, CancellationToken cancellationToken)
    {
        var facilityPath = FacilityPathFor(metadataSavePath);
        if (facilityPath is null)
        {
            return (0, $"{StoryFlagSync.FacilitySaveName} not found next to the metadata save.");
        }

        var bytes = await files.ReadAllBytesAsync(facilityPath, cancellationToken).ConfigureAwait(false);
        var written = await Task.Run<(byte[]? Bytes, int Count, string Message)>(() =>
        {
            var data = WorldSaveReader.ReadFromStream(new MemoryStream(bytes, writable: false));
            var result = plan(data);
            if (result.Flags is null) return (null, result.Count, result.Message);

            WorldSaveWriter.ApplyFlags(data, result.Flags);
            using var buffer = new MemoryStream();
            data.Raw.WriteTo(buffer);
            return (buffer.ToArray(), result.Count, result.Message);
        }, cancellationToken).ConfigureAwait(false);

        if (written.Bytes is null) return (written.Count, written.Message);

        await files.WriteAllBytesAsync(facilityPath, written.Bytes, cancellationToken).ConfigureAwait(false);
        return (written.Count, written.Message);
    }

    /// <summary>
    /// The facility save to act on: the one already open in the workspace, which is the only
    /// answer available on a host with no local paths, otherwise the file beside the metadata save.
    /// </summary>
    private string? FacilityPathFor(string metadataSavePath)
    {
        var fromWorkspace = workspace.Current?.Saves.FirstOrDefault(save =>
            save.Kind == SaveDocumentKind.World
            && string.Equals(save.Name, StoryFlagSync.FacilitySaveName, StringComparison.OrdinalIgnoreCase));
        if (fromWorkspace is not null) return fromWorkspace.Path;

        return files.HasLocalPaths ? StoryFlagSync.SiblingFacilityPath(metadataSavePath) : null;
    }
}
