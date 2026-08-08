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
/// Scans run off-thread, are cached until the file changes (the Facility save is ~16 MB), and
/// never happen on workspace open - only when the picker actually needs them. Writes never
/// happen here: a send stages into a <see cref="WorldSaveSession"/> and the user saves it.
/// </summary>
/// <remarks>
/// Everything goes through <see cref="ISaveFileSystem"/> rather than <see cref="System.IO"/>.
/// The desktop behaviour is unchanged; the browser has no disk to walk, so its Facility save is
/// found among the folder the player opened instead of beside a path.
/// </remarks>
public sealed class SiblingWorldBedService(ISaveFileSystem files)
{
    private readonly ConcurrentDictionary<string, CachedBeds> _bedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedDeployables> _deployableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedBeds(string? Stamp, IReadOnlyList<SiblingPetBed> Beds);
    private sealed record CachedDeployables(string? Stamp, IReadOnlyList<WorldDeployable> Deployables);
    private sealed record CachedSession(string? Stamp, WorldSaveSession Session);

    /// <summary>The Facility region save's file name, which is where beds are looked up.</summary>
    private const string FacilitySaveName = "WorldSave_Facility.sav";

    /// <summary>
    /// The <c>WorldSave_Facility.sav</c> that goes with a player save, or null when there isn't
    /// one to read. Preference order is what each host can actually answer: a save already open
    /// in the workspace first, then - only where the editor can reach the disk - the file sitting
    /// in the player save's world folder (the native lookup: the world folder is the player
    /// file's grandparent).
    /// </summary>
    public async Task<string?> FacilityPathForAsync(
        string playerSavePath, SaveWorkspace? workspace, CancellationToken cancellationToken = default)
    {
        var fromWorkspace = workspace?.Saves.FirstOrDefault(save =>
            save.Kind == SaveDocumentKind.World
            && string.Equals(save.Name, FacilitySaveName, StringComparison.OrdinalIgnoreCase));
        if (fromWorkspace is not null) return fromWorkspace.Path;

        if (!files.HasLocalPaths) return null;

        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(playerSavePath)));
        if (worldDir is null) return null;
        var facility = Path.Combine(worldDir, FacilitySaveName);
        return await files.GetVersionStampAsync(facility, cancellationToken).ConfigureAwait(false) is null
            ? null
            : facility;
    }

    /// <summary>
    /// Every deployable in a world save, for callers that need more than pet beds (player
    /// bed spawn targets, bed-claim persona names). A live session for the path is
    /// preferred so staged claims show; otherwise the file is read off-thread, read-only,
    /// and cached until it changes.
    /// </summary>
    public async Task<IReadOnlyList<WorldDeployable>> GetDeployablesAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Normalize(worldPath);
        if (SessionFor(fullPath, loadedSession) is { } session) return session.Deployables;

        var stamp = await files.GetVersionStampAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (IsFresh(_deployableCache, fullPath, stamp, out var cached)) return cached.Deployables;

        var save = await ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var deployables = (IReadOnlyList<WorldDeployable>)[.. save.Deployables];
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

        // Walking the disk for neighbours is a desktop-only answer. In the browser the folder
        // the player opened IS the whole world, so an empty workspace means no targets exist.
        if (!files.HasLocalPaths) return [];

        return PetSaveLocator.SiblingWorldSaves(playerSavePath)
            .Select(path => new SiblingWorldOption(path, Path.GetFileName(path)))
            .ToArray();
    }

    /// <summary>
    /// The pet beds in a world save. A live session for that path (loaded in the workspace
    /// or created by an earlier send) is preferred so staged state shows; otherwise the file
    /// is read off-thread, read-only, and cached until it changes.
    /// </summary>
    public async Task<IReadOnlyList<SiblingPetBed>> GetBedsAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Normalize(worldPath);
        if (SessionFor(fullPath, loadedSession) is { } session)
            return BedsFrom(session.Deployables);

        var stamp = await files.GetVersionStampAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (IsFresh(_bedCache, fullPath, stamp, out var cached)) return cached.Beds;

        var save = await ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var beds = BedsFrom(save.Deployables);
        _bedCache[fullPath] = new CachedBeds(stamp, beds);
        return beds;
    }

    /// <summary>
    /// The staged-edit session a send writes into. The workspace's live session is reused
    /// when it is the same file; otherwise a session is loaded once and kept so repeated
    /// sends (and the eventual SAVE WORLD) stay on one staged tree. Sessions with no
    /// unsaved changes are reloaded when the file changed since they were read.
    /// </summary>
    public async Task<WorldSaveSession> GetOrLoadSessionAsync(
        string worldPath, WorldSaveSession? loadedSession = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Normalize(worldPath);
        if (loadedSession is not null && PathsEqual(loadedSession.Path, fullPath)) return loadedSession;

        var stamp = await files.GetVersionStampAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (_sessions.TryGetValue(fullPath, out var cached)
            && (cached.Session.IsDirty || (stamp is not null && cached.Stamp == stamp)))
            return cached.Session;

        var save = await ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        // Hand the session the host's file system. Without it, saving this world falls back to
        // writing straight to a disk path, which works on the desktop and cannot work in a
        // browser - so sending a pet to another world staged fine and then failed at SAVE WORLD.
        var session = new WorldSaveSession(save, fullPath, files);
        _sessions[fullPath] = new CachedSession(stamp, session);
        return session;
    }

    /// <summary>The already-loaded session for a path, when one exists.</summary>
    public WorldSaveSession? SessionFor(string worldPath, WorldSaveSession? loadedSession = null)
    {
        var fullPath = Normalize(worldPath);
        if (loadedSession is not null && PathsEqual(loadedSession.Path, fullPath)) return loadedSession;
        return _sessions.TryGetValue(fullPath, out var cached) ? cached.Session : null;
    }

    /// <summary>
    /// Reads a world save through the host's file system. Parsing is the expensive half (~16 MB
    /// for the Facility save), so it stays off the render thread on both hosts.
    /// </summary>
    private async Task<WorldSaveData> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await files.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => WorldSaveReader.ReadFromStream(new MemoryStream(bytes, writable: false)), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// True when a cached entry can still be trusted. An unknown stamp counts as changed, so a
    /// host that cannot tell re-reads rather than serving something stale.
    /// </summary>
    private static bool IsFresh<T>(
        ConcurrentDictionary<string, T> cache, string path, string? stamp, out T cached) where T : class
    {
        if (cache.TryGetValue(path, out var found) && stamp is not null && StampOf(found) == stamp)
        {
            cached = found;
            return true;
        }
        cached = null!;
        return false;

        static string? StampOf(T entry) => entry switch
        {
            CachedBeds beds => beds.Stamp,
            CachedDeployables deployables => deployables.Stamp,
            CachedSession session => session.Stamp,
            _ => null,
        };
    }

    /// <summary>
    /// Canonicalizes a path for use as a cache key. Only meaningful where paths are real; a
    /// browser identifier is already canonical and running it through
    /// <see cref="Path.GetFullPath(string)"/> would turn it into a made-up local path.
    /// </summary>
    private string Normalize(string path) => files.HasLocalPaths ? Path.GetFullPath(path) : path;

    private static SiblingPetBed[] BedsFrom(IEnumerable<WorldDeployable> deployables)
        => deployables.Where(deployable => deployable.IsPetBed)
            .Select(bed => new SiblingPetBed(bed.X, bed.Y, bed.Z, bed.DisplayName))
            .ToArray();

    private bool PathsEqual(string left, string right)
        => string.Equals(Normalize(left), right, StringComparison.OrdinalIgnoreCase);
}
