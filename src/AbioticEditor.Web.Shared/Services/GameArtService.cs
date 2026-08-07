using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>An in-game sector map ready to draw on: its artwork, and how to pin world positions on it.</summary>
/// <param name="Fit">World-to-texture-fraction transform for the level this map depicts.</param>
/// <param name="TextureRef">Game ref of the drawing, for <see cref="GameArtService.ArtUrl"/>.</param>
public sealed record SectorMap(SectorMapFit Fit, string TextureRef);

/// <summary>
/// Extracts arbitrary game textures by their raw UE asset path (chapter card art, trader
/// portraits, skill icons) for the detail panels that used to live in the retired native
/// app's shared slot sidebar. Distinct from <see cref="ItemCatalogService"/>, which only
/// resolves icons for catalog item ids. Also resolves a door actor's exact world position
/// by reading it live from the cooked sub-level package (<see cref="DoorLocationResolver"/>),
/// the same mechanism the native app used for its door detail panel.
/// </summary>
public sealed class GameArtService : IDisposable
{
    private readonly Lazy<GameAssetProvider?> _provider = new(CreateProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ConcurrentDictionary<string, Task<string?>> _paths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<(double X, double Y, double Z)?>> _doorPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyDictionary<string, DoorWorldLocation>>> _doorMapPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<string?>> _wikiImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ActorTransform?>> _actorTransforms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyDictionary<string, DoorStoryGate>>> _doorGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<SectorMap?>> _sectorMaps = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<Task<IReadOnlyList<string>>>? _npcStates;

    private readonly bool _extractsLive;

    /// <param name="files">
    /// Tells the two hosts apart, exactly as <see cref="ItemCatalogService"/> does. A host that can
    /// reach the local machine pulls pictures out of the installed game on demand and serves them
    /// from its own endpoint; a browser cannot, so it uses the set dumped ahead of time and shipped
    /// as static files.
    /// </param>
    public GameArtService(ISaveFileSystem? files = null)
    {
        _extractsLive = files is null || files.HasLocalPaths;
    }

    public string ArtUrl(string gameRef) => _extractsLive
        ? $"/game-art/{Uri.EscapeDataString(gameRef)}"
        : $"art/{Uri.EscapeDataString(BundledArt.FileNameFor(gameRef))}";

    /// <summary>URL for a wiki-image file cached/downloaded via <see cref="Core.Assets.WikiImageCache"/>.</summary>
    public string WikiImageUrl(string fileName) => _extractsLive
        ? $"/wiki-image/{Uri.EscapeDataString(fileName)}"
        // The bundle stores these under the same tidied name the cache writes on disk, because
        // a wiki File: name can carry spaces and punctuation that do not belong in a URL.
        : $"wiki/{Uri.EscapeDataString(Core.Assets.WikiImageCache.SafeNameFor(fileName))}.png";

    public bool HasGameInstall => _extractsLive && _provider.Value is not null;

    public Task<string?> GetTexturePathAsync(string? gameRef)
    {
        if (string.IsNullOrWhiteSpace(gameRef)) return Task.FromResult<string?>(null);

        // In a browser there is no local path to hand back and nothing to extract. What the
        // callers actually want to know is "will drawing this show a picture or a broken image?",
        // which the shipped manifest answers without a request. Returning the ref stands for yes.
        if (!_extractsLive)
        {
            return Task.FromResult(BundledArt.LoadBundled()?.Has(gameRef) == true ? gameRef : null);
        }

        return _paths.GetOrAdd(gameRef, static (r, service) => service.ExtractAsync(r), this);
    }

    private async Task<string?> ExtractAsync(string gameRef) => await Task.Run(() =>
    {
        try
        {
            var provider = _provider.Value;
            return provider is not { HasMappings: true } ? null : provider.ExtractTextureByGameRef(gameRef);
        }
        catch { return null; }
    }).ConfigureAwait(false);

    /// <summary>
    /// A door actor's exact world position, read live from its cooked sub-level package.
    /// The save itself stores door STATE only, never a position, so this is the only way
    /// to show where a door physically sits. Null means the game install is unavailable,
    /// the sub-level package could not be read, or the actor was not found in it (e.g. a
    /// future game version renamed it) - never thrown as an error to the caller.
    /// </summary>
    public Task<(double X, double Y, double Z)?> TryGetDoorPositionAsync(string? mapName, string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName)) return Task.FromResult<(double, double, double)?>(null);
        var key = $"{mapName}|{actorName}";
        return _doorPositions.GetOrAdd(key, _ => ResolveDoorPositionAsync(mapName, actorName));
    }

    private async Task<(double X, double Y, double Z)?> ResolveDoorPositionAsync(string? mapName, string actorName) => await Task.Run(() =>
    {
        try
        {
            var provider = _provider.Value;
            if (provider is not { HasMappings: true }) return null;
            var location = DoorLocationResolver.Resolve(provider, mapName, actorName);
            return location is null ? ((double X, double Y, double Z)?)null : (location.X, location.Y, location.Z);
        }
        catch { return null; }
    }).ConfigureAwait(false);

    /// <summary>
    /// World positions of every door actor in <paramref name="mapName"/>'s cooked sub-level
    /// package, resolved in a single pass (<see cref="DoorLocationResolver.ForMap"/> caches per
    /// map for the process, so re-selecting doors in the same sub-level costs nothing after the
    /// first read). Used to plot the door mini-map; empty when the game install or package is
    /// unavailable.
    /// </summary>
    public Task<IReadOnlyDictionary<string, DoorWorldLocation>> GetDoorPositionsForMapAsync(string? mapName)
    {
        var key = mapName ?? string.Empty;
        return _doorMapPositions.GetOrAdd(key, _ => ResolveMapPositionsAsync(mapName));
    }

    private async Task<IReadOnlyDictionary<string, DoorWorldLocation>> ResolveMapPositionsAsync(string? mapName) => await Task.Run(() =>
    {
        try
        {
            var provider = _provider.Value;
            if (provider is not { HasMappings: true }) return (IReadOnlyDictionary<string, DoorWorldLocation>)new Dictionary<string, DoorWorldLocation>();
            return DoorLocationResolver.ForMap(provider, mapName);
        }
        catch { return new Dictionary<string, DoorWorldLocation>(); }
    }).ConfigureAwait(false);

    /// <summary>
    /// Which world flag opens a given door, read live from its cooked sub-level package. Null
    /// means the door is not story gated - or that the level could not be read at all, so
    /// callers must not treat null as proof. Only a handful of doors in the whole game carry
    /// one; see <see cref="DoorGateResolver"/>.
    /// </summary>
    public async Task<DoorStoryGate?> TryGetDoorGateAsync(string? mapName, string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName)) return null;
        var gates = await GetDoorGatesForMapAsync(mapName).ConfigureAwait(false);
        return gates.TryGetValue(actorName, out var gate) ? gate : null;
    }

    /// <summary>
    /// Every story-gated door in one sub-level, keyed by actor name. Absent means "no gate";
    /// an empty result also means the level could not be read, so callers should treat it as
    /// "nothing known" rather than proof.
    /// </summary>
    public Task<IReadOnlyDictionary<string, DoorStoryGate>> GetDoorGatesForMapAsync(string? mapName)
        => _doorGates.GetOrAdd(mapName ?? string.Empty, _ => Task.Run(() =>
        {
            try
            {
                var provider = _provider.Value;
                if (provider is not { HasMappings: true })
                {
                    return (IReadOnlyDictionary<string, DoorStoryGate>)new Dictionary<string, DoorStoryGate>();
                }
                return DoorGateResolver.ForMap(provider, mapName);
            }
            catch { return new Dictionary<string, DoorStoryGate>(); }
        }));

    /// <summary>
    /// The in-game sector map that depicts <paramref name="mapName"/>, with everything needed
    /// to pin a world position on it. Null when the level has no calibrated map (most of them
    /// do not) or the game install is unavailable.
    /// </summary>
    public Task<SectorMap?> GetSectorMapAsync(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName)) return Task.FromResult<SectorMap?>(null);
        return _sectorMaps.GetOrAdd(mapName, static (name, service) => Task.Run(() =>
        {
            try
            {
                var fit = SectorMapCalibration.FitFor(name);
                if (fit is null) return null;
                var provider = service._extractsLive ? service._provider.Value : null;
                var maps = provider is { HasMappings: true }
                    ? SectorMapCatalog.LoadFrom(provider)
                    : GameDataRegistry.LoadBundled()?.SectorMaps ?? Array.Empty<SectorMapInfo>();
                var info = SectorMapCatalog.ForRow(maps, fit.PamphletRow);
                return info is null ? null : new SectorMap(fit, info.TexturePath);
            }
            catch { return null; }
        }), this);
    }

    /// <summary>
    /// Local path of a cached/downloaded wiki-image file (see
    /// <see cref="Core.Assets.WikiImageCache"/>), or <c>null</c> when the wiki has no such file
    /// and no bundled offline copy ships either.
    /// </summary>
    public Task<string?> GetWikiImagePathAsync(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return Task.FromResult<string?>(null);

        // The browser ships the offline copies as static files and has no disk to cache to, so
        // the answer is simply "one shipped": see GetTexturePathAsync for why a name comes back.
        if (!_extractsLive)
        {
            return Task.FromResult(Core.Assets.WikiImageManifest.Contains(fileName) ? fileName : null);
        }

        return _wikiImages.GetOrAdd(fileName, static f => Core.Assets.WikiImageCache.Default.GetAsync(f));
    }

    /// <summary>
    /// A placed actor's original spawn transform, read live from its cooked level package - the
    /// same mechanism the native app used for a vehicle's "reset to spawn". Null means the game
    /// install is unavailable, the level package could not be read, or the actor was not found in
    /// it (never thrown as an error to the caller).
    /// </summary>
    public Task<ActorTransform?> TryGetActorTransformAsync(string? actorObjectPath)
    {
        if (string.IsNullOrWhiteSpace(actorObjectPath)) return Task.FromResult<ActorTransform?>(null);
        return _actorTransforms.GetOrAdd(actorObjectPath, static (path, service) => Task.Run(() =>
        {
            try
            {
                var provider = service._provider.Value;
                return provider is not { HasMappings: true } ? null : provider.TryGetActorTransform(path);
            }
            catch { return null; }
        }), this);
    }

    /// <summary>
    /// The value vocabulary of narrative NPC script states (<c>E_NarrativeNPCStates</c>), read
    /// live from the mounted paks. Empty when the game install is unavailable; callers should
    /// fall back to free-text entry in that case.
    /// </summary>
    public Task<IReadOnlyList<string>> GetNpcStateOptionsAsync()
    {
        _npcStates ??= new Lazy<Task<IReadOnlyList<string>>>(() => Task.Run(() =>
        {
            try
            {
                var provider = _provider.Value;
                return provider is not { HasMappings: true } ? [] : NpcStateCatalog.LoadFrom(provider);
            }
            catch { return (IReadOnlyList<string>)[]; }
        }));
        return _npcStates.Value;
    }

    private static GameAssetProvider? CreateProvider()
    {
        try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
        catch { return null; }
    }

    public void Dispose() { if (_provider.IsValueCreated) _provider.Value?.Dispose(); }
}
