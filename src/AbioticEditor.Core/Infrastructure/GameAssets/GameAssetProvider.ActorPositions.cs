using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CUE4Parse.UE4.Assets;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Where the level's own actors stand (doors, buttons, breakable walls, wall sockets...), kept on
/// disk. Reading one used to load its whole level file under the shared game-files lock, every time:
/// the 3D view asks for hundreds at once, which on a fresh start held everything else up (the level
/// around the view waited two minutes). Now a level file is read once for many of its actors, and
/// each answer is remembered across runs until the game itself changes.
/// </summary>
public sealed partial class GameAssetProvider
{
    private const int RecentPackageCount = 3;
    private readonly List<(string Path, IPackage Package)> _recentPackages = [];
    private Timer? _recentPackagesTimer;

    private Dictionary<string, ActorTransform?>? _actorPositions;
    private Timer? _actorPositionsSaveTimer;
    private readonly object _actorPositionsLock = new();

    /// <summary>
    /// The actor an object path names (<c>/Game/Maps/X.X:PersistentLevel.Door_C_3</c>), with its level
    /// file kept open for the next few lookups. The caller holds the game-files lock.
    /// </summary>
    private CUE4Parse.UE4.Assets.Exports.UObject? LoadActorKeepingLevel(string objectPath)
    {
        var colon = objectPath.IndexOf(':', StringComparison.Ordinal);
        var dot = objectPath.LastIndexOf('.');
        var packageEnd = objectPath.IndexOf('.', objectPath.LastIndexOf('/') + 1);
        if (colon <= 0 || dot <= colon || packageEnd <= 0 || packageEnd > colon)
        {
            return _provider.TryLoadPackageObject(objectPath, out var direct) ? direct : null;
        }
        var packagePath = objectPath[..packageEnd];
        var name = objectPath[(dot + 1)..];

        var index = _recentPackages.FindIndex(p => p.Path.Equals(packagePath, StringComparison.OrdinalIgnoreCase));
        IPackage package;
        if (index >= 0)
        {
            package = _recentPackages[index].Package;
            _recentPackages.RemoveAt(index);
        }
        else
        {
            if (!_provider.TryLoadPackage(packagePath, out var loaded) || loaded is null) return null;
            package = loaded;
        }
        _recentPackages.Insert(0, (packagePath, package));
        if (_recentPackages.Count > RecentPackageCount) _recentPackages.RemoveAt(_recentPackages.Count - 1);
        // Let the open level files go once the lookups stop (they hold what was read from them).
        _recentPackagesTimer ??= new Timer(_ => { using (ProviderLock()) _recentPackages.Clear(); });
        _recentPackagesTimer.Change(20_000, Timeout.Infinite);

        return package.GetExportIndex(name, StringComparison.OrdinalIgnoreCase) >= 0
            ? package.GetExportOrNull(name, StringComparison.OrdinalIgnoreCase)
            : _provider.TryLoadPackageObject(objectPath, out var fallback) ? fallback : null;
    }

    private string ActorPositionsFile => Path.Combine(_cacheDir, "actors", "positions-" + GameFilesStamp() + ".json");

    private bool TryGetKnownActorPosition(string actorPath, out ActorTransform? position)
    {
        lock (_actorPositionsLock)
        {
            _actorPositions ??= ReadActorPositions();
            return _actorPositions.TryGetValue(actorPath, out position);
        }
    }

    private void RememberActorPosition(string actorPath, ActorTransform? position)
    {
        lock (_actorPositionsLock)
        {
            _actorPositions ??= ReadActorPositions();
            _actorPositions[actorPath] = position;
            _actorPositionsSaveTimer ??= new Timer(_ => SaveActorPositions());
            _actorPositionsSaveTimer.Change(3000, Timeout.Infinite);
        }
    }

    private sealed record StoredPosition(double[]? T);

    private Dictionary<string, ActorTransform?> ReadActorPositions()
    {
        try
        {
            if (File.Exists(ActorPositionsFile)
                && JsonSerializer.Deserialize<Dictionary<string, StoredPosition>>(File.ReadAllText(ActorPositionsFile)) is { } stored)
            {
                return stored.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.T is { Length: 7 } t ? new ActorTransform(t[0], t[1], t[2], t[3], t[4], t[5], t[6]) : (ActorTransform?)null,
                    StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Ignoring unreadable actor position cache: {ex.Message}");
        }
        return new Dictionary<string, ActorTransform?>(StringComparer.Ordinal);
    }

    private void SaveActorPositions()
    {
        try
        {
            Dictionary<string, StoredPosition> snapshot;
            lock (_actorPositionsLock)
            {
                if (_actorPositions is null) return;
                snapshot = _actorPositions.ToDictionary(
                    kv => kv.Key,
                    kv => new StoredPosition(kv.Value is { } p ? [p.X, p.Y, p.Z, p.QuatX, p.QuatY, p.QuatZ, p.QuatW] : null),
                    StringComparer.Ordinal);
            }
            var file = ActorPositionsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Could not keep actor positions: {ex.Message}");
        }
    }

    private string? _gameFilesStamp;

    /// <summary>Changes whenever the game's pak files do (an update), so remembered answers are not reused across one.</summary>
    private string GameFilesStamp()
    {
        if (_gameFilesStamp is not null) return _gameFilesStamp;
        var builder = new StringBuilder();
        var paks = _paksDirectory ?? AfInstallLocator.FindPaksDirectory();
        if (paks is not null && Directory.Exists(paks))
        {
            foreach (var file in Directory.EnumerateFiles(paks).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                builder.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
            }
        }
        foreach (var mod in _loadedMods) builder.Append(mod).Append('|');
        return _gameFilesStamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }
}
