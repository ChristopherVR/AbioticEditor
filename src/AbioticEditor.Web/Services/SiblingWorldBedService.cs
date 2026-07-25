using System.Collections.Concurrent;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>A world save file a carried pet can be sent to.</summary>
public sealed record SiblingWorldOption(string Path, string Name);

/// <summary>A pet bed found in a world save, as a send-to target.</summary>
public sealed record SiblingPetBed(double X, double Y, double Z, string DisplayName);

/// <summary>
/// Cross-save pet-bed discovery for the COMPANIONS tab, mirroring the native editor: the
/// player's sibling <c>WorldSave_*.sav</c> files are scanned read-only for pet beds so a
/// carried pet can be sent to a bed even when no world save is loaded in the workspace.
/// Scans run off-thread, are cached per file timestamp (the Facility save is ~16 MB), and
/// never happen on workspace open - only when the picker actually needs them. Writes never
/// happen here: a send stages into a <see cref="WorldSaveSession"/> and the user saves it.
/// </summary>
public sealed class SiblingWorldBedService
{
    private readonly ConcurrentDictionary<string, CachedBeds> _bedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedDeployables> _deployableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedBeds(DateTime LastWriteUtc, IReadOnlyList<SiblingPetBed> Beds);
    private sealed record CachedDeployables(DateTime LastWriteUtc, IReadOnlyList<WorldDeployable> Deployables);
    private sealed record CachedSession(DateTime LastWriteUtc, WorldSaveSession Session);

    /// <summary>
    /// The sibling <c>WorldSave_Facility.sav</c> for a player save (the native bed-lookup
    /// source: the world folder is the player file's grandparent), or null when the file
    /// does not exist on disk.
    /// </summary>
    public static string? FacilityPathFor(string playerSavePath)
    {
        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(playerSavePath)));
        if (worldDir is null) return null;
        var facility = Path.Combine(worldDir, "WorldSave_Facility.sav");
        return File.Exists(facility) ? facility : null;
    }

    /// <summary>
    /// Every deployable in a world save, for callers that need more than pet beds (player
    /// bed spawn targets, bed-claim persona names). A live session for the path is
    /// preferred so staged claims show; otherwise the file is read off-thread, read-only,
    /// and cached until it changes on disk.
    /// </summary>
    public async Task<IReadOnlyList<WorldDeployable>> GetDeployablesAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(worldPath);
        if (SessionFor(fullPath, loadedSession) is { } session) return session.Deployables;

        var stamp = File.GetLastWriteTimeUtc(fullPath);
        if (_deployableCache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == stamp)
            return cached.Deployables;

        var deployables = await Task.Run(
            () => (IReadOnlyList<WorldDeployable>)[.. WorldSaveReader.ReadFromFile(fullPath).Deployables], cancellationToken)
            .ConfigureAwait(false);
        _deployableCache[fullPath] = new CachedDeployables(stamp, deployables);
        return deployables;
    }

    /// <summary>
    /// World saves a pet can be sent to: the open workspace's region saves when available
    /// (the metadata save is never a target, matching native), otherwise the files sitting
    /// next to the player save on disk (Core's sibling-save layout walk).
    /// </summary>
    public IReadOnlyList<SiblingWorldOption> SiblingWorlds(string playerSavePath, SaveWorkspace? workspace)
    {
        var fromWorkspace = workspace?.Saves
            .Where(save => save.Kind == SaveDocumentKind.World)
            .Select(save => new SiblingWorldOption(save.Path, save.Name))
            .ToArray() ?? [];
        if (fromWorkspace.Length > 0) return fromWorkspace;
        return PetSaveLocator.SiblingWorldSaves(playerSavePath)
            .Select(path => new SiblingWorldOption(path, Path.GetFileName(path)))
            .ToArray();
    }

    /// <summary>
    /// The pet beds in a world save. A live session for that path (loaded in the workspace
    /// or created by an earlier send) is preferred so staged state shows; otherwise the file
    /// is read off-thread, read-only, and cached until it changes on disk.
    /// </summary>
    public async Task<IReadOnlyList<SiblingPetBed>> GetBedsAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(worldPath);
        if (SessionFor(fullPath, loadedSession) is { } session)
            return BedsFrom(session.Deployables);

        var stamp = File.GetLastWriteTimeUtc(fullPath);
        if (_bedCache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == stamp)
            return cached.Beds;

        var beds = await Task.Run(() => BedsFrom(WorldSaveReader.ReadFromFile(fullPath).Deployables), cancellationToken)
            .ConfigureAwait(false);
        _bedCache[fullPath] = new CachedBeds(stamp, beds);
        return beds;
    }

    /// <summary>
    /// The staged-edit session a send writes into. The workspace's live session is reused
    /// when it is the same file; otherwise a session is loaded once and kept so repeated
    /// sends (and the eventual SAVE WORLD) stay on one staged tree. Sessions with no
    /// unsaved changes are reloaded when the file changed on disk since they were read.
    /// </summary>
    public async Task<WorldSaveSession> GetOrLoadSessionAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(worldPath);
        if (loadedSession is not null && PathsEqual(loadedSession.Path, fullPath)) return loadedSession;

        var stamp = File.GetLastWriteTimeUtc(fullPath);
        if (_sessions.TryGetValue(fullPath, out var cached)
            && (cached.Session.IsDirty || cached.LastWriteUtc == stamp))
            return cached.Session;

        var session = await Task.Run(
            () => new WorldSaveSession(WorldSaveReader.ReadFromFile(fullPath), fullPath), cancellationToken)
            .ConfigureAwait(false);
        _sessions[fullPath] = new CachedSession(stamp, session);
        return session;
    }

    /// <summary>The already-loaded session for a path, when one exists.</summary>
    public WorldSaveSession? SessionFor(string worldPath, WorldSaveSession? loadedSession = null)
    {
        var fullPath = Path.GetFullPath(worldPath);
        if (loadedSession is not null && PathsEqual(loadedSession.Path, fullPath)) return loadedSession;
        return _sessions.TryGetValue(fullPath, out var cached) ? cached.Session : null;
    }

    private static SiblingPetBed[] BedsFrom(IEnumerable<WorldDeployable> deployables)
        => deployables.Where(deployable => deployable.IsPetBed)
            .Select(bed => new SiblingPetBed(bed.X, bed.Y, bed.Z, bed.DisplayName))
            .ToArray();

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), right, StringComparison.OrdinalIgnoreCase);
}
