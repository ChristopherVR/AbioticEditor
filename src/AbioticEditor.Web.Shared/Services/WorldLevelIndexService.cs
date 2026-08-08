using System.Collections.Concurrent;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The regions of an open world - their level ids and readable names - for the spawn screen's
/// "pick the area the spawn point is in" list.
/// </summary>
/// <remarks>
/// <para>Core can already find a save's level id, but only by opening a path and streaming the
/// file from the front. A browser has neither: it reads through the folder handle the player
/// granted, and reading every region save whole would pull tens of megabytes across the interop
/// boundary just to fill a dropdown. The region list was simply empty there as a result.</para>
///
/// <para>So the lookup reads the END of each save instead. The game writes <c>LevelGUID</c> among
/// the last properties in the file: in every region save but the main facility one it lands
/// within a few hundred bytes of the end, and the facility's within a few megabytes. Asking for
/// a small slice first and widening only when that comes up empty turns a sixty-megabyte scan
/// into a few hundred kilobytes, and works the same way on both hosts.</para>
/// </remarks>
public sealed class WorldLevelIndexService(ISaveFileSystem files)
{
    /// <summary>
    /// Tail sizes tried in turn. The first covers every region save the game ships except the
    /// main facility one; the last is generous enough for that one too. A save that answers at
    /// the first size never pays for the rest.
    /// </summary>
    private static readonly int[] TailSizes = [64 * 1024, 1024 * 1024, 8 * 1024 * 1024];

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<WorldLevel>>> _byFolder =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The regions of the world <paramref name="workspace"/> has open, read once and remembered
    /// for as long as that folder stays open. Returns an empty list when no world is open.
    /// </summary>
    public Task<IReadOnlyList<WorldLevel>> GetLevelsAsync(
        SaveWorkspace? workspace, CancellationToken cancellationToken = default)
    {
        if (workspace is null) return Task.FromResult<IReadOnlyList<WorldLevel>>([]);
        return _byFolder.GetOrAdd(workspace.WorldFolder, _ => ScanAsync(workspace, cancellationToken));
    }

    private async Task<IReadOnlyList<WorldLevel>> ScanAsync(
        SaveWorkspace workspace, CancellationToken cancellationToken)
    {
        var levels = new List<WorldLevel>();
        // The metadata save is world-wide bookkeeping, not a place anyone spawns.
        var regions = workspace.Saves.Where(save => save.Kind == SaveDocumentKind.World);
        foreach (var save in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryReadLevelGuidAsync(save, cancellationToken).ConfigureAwait(false) is { } guid)
            {
                levels.Add(new WorldLevel(guid, TrimExtension(save.Name)));
            }
        }
        return levels;
    }

    private async Task<string?> TryReadLevelGuidAsync(WorkspaceSave save, CancellationToken cancellationToken)
    {
        var previousSize = 0;
        foreach (var size in TailSizes)
        {
            // The whole file was already covered by a smaller slice: widening cannot find more.
            if (previousSize >= save.Length) break;
            previousSize = size;
            try
            {
                var tail = await files.ReadTailAsync(save.Path, size, cancellationToken).ConfigureAwait(false);
                if (WorldLevelIndex.TryReadLevelGuid(tail) is { } guid) return guid;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // One unreadable save drops out of the list rather than emptying it.
                AbioticEditor.Core.Diagnostics.EditorLog.Warn(
                    "Scan", $"Could not read the end of {save.Name} to find its region: {exception.Message}");
                return null;
            }
        }
        return null;
    }

    private static string TrimExtension(string name)
        => name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
