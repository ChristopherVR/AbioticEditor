using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Plugins.Scene;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The browser build's 3D scenery: the level around a base, read from the prepared scenery the
/// editor's website hosts next to the browser editor (<c>/scenery/v1/</c>, see
/// <c>assets/scenery/README.md</c>) instead of from an installed game, which a browser cannot read.
/// </summary>
/// <remarks>
/// <para>
/// The hosted files are the desktop Game Models provider's own render cache (named by the SHA-256 of
/// their key, as <c>PakSceneModelProvider.CachePath</c> names them), so this answers a level query
/// exactly as that provider answers it from a full cache: same pieces, order, materials and lights.
/// <c>HostedSceneryReaderTests</c> holds the two side by side on the real hosted files.
/// </para>
/// <para>
/// Asset ids in the answers are the hosted files' own addresses, so the view fetches meshes and
/// textures straight from the website. Only level scenery is hosted; placed objects stay boxes.
/// </para>
/// </remarks>
public sealed partial class HostedSceneryReader : IDisposable
{
    /// <summary>Where the scenery lives relative to the browser editor (<c>/AbioticEditor/app/</c>).</summary>
    public const string FolderFromApp = "../scenery/v1/";

    // The provider's cache layout (see PakSceneModelProvider): folders, versions and sizes.
    private const string MaterialsFolder = "materials-v5";
    private const string TerrainMaterialsFolder = "terrain-materials-v1";
    private const string AlphaFolder = "texture-alpha-v1";
    private const int LevelTextureSize = 512;
    private const int LevelLod = 1;
    private const float LevelMarginCm = 300f;
    private const float MaxPieceRadiusCm = 40000f;
    private const int MaxLevelLights = 48;
    private const string LandMarker = "#land=";
    private const string PoseMarker = "#pose=";
    private const uint LevelMagic = 0x3149_4C41; // "ALI1"
    private const int LevelFormatVersion = 14;

    /// <summary>How long a query waits for level files still downloading before answering with what is ready.</summary>
    private static readonly TimeSpan LevelWait = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly float[] FallbackColor = [0.62f, 0.62f, 0.6f];

    private readonly HttpClient _http;
    private readonly Uri _root;
    private readonly SemaphoreSlim _downloads = new(16);
    private readonly Lazy<Task<Uri?>> _build;
    private readonly Lazy<Task<Dictionary<string, JsonElement>?>> _descriptions;
    private int _filesAsked;
    private int _filesDone;
    private readonly ConcurrentDictionary<string, Task<World?>> _worlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Level?>> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<MeshInfo?>> _meshInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Material?>> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<List<SceneMaterial>?>> _terrain = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<bool>> _alpha = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scenery next to the browser editor this client was loaded from.</summary>
    public HostedSceneryReader(HttpClient http)
        : this(http, new Uri(http?.BaseAddress ?? throw new ArgumentException("The client needs the app's address.", nameof(http)), FolderFromApp))
    {
    }

    /// <summary>Scenery under <paramref name="root"/> (the <c>scenery/v1/</c> folder).</summary>
    public HostedSceneryReader(HttpClient http, Uri root)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _root = root;
        _build = new Lazy<Task<Uri?>>(FindBuildAsync);
        _descriptions = new Lazy<Task<Dictionary<string, JsonElement>?>>(ReadDescriptionsAsync);
    }

    /// <summary>
    /// Files downloaded so far and files asked for, since the page opened: the view shows the
    /// difference over a level query as its download progress.
    /// </summary>
    [JSInvokable]
    public int[] Progress() => [Volatile.Read(ref _filesDone), Volatile.Read(ref _filesAsked)];

    public void Dispose() => _downloads.Dispose();

    // ---- the player's permission -------------------------------------------------------------

    /// <summary>
    /// The player agreed to download the scenery (tens of megabytes an area). Nothing is fetched until
    /// then, not even the build index: the 3D view asks first, and the answer is kept in this browser.
    /// </summary>
    public bool Allowed => HostPreferenceStore.Read(HostPreferenceStore.Keys.HostedScenery, "hosted-scenery.txt") == "yes";

    /// <summary>Records the player's answer: true allows downloads from now on, false forgets an earlier yes.</summary>
    public void SetAllowed(bool allowed)
        => HostPreferenceStore.Write(HostPreferenceStore.Keys.HostedScenery, "hosted-scenery.txt", allowed ? "yes" : null);

    // ---- what the view asks -------------------------------------------------------------------

    /// <summary>Whether scenery is on offer (allowed, and the website has a prepared build), in the view's status shape.</summary>
    [JSInvokable]
    public async Task<SceneModelStatus> Status()
        => !Allowed || await _build.Value.ConfigureAwait(false) is null
            ? new SceneModelStatus(false, false, null, null)
            : new SceneModelStatus(true, true, "Game scenery", null);

    /// <summary>Placed objects: none are hosted, so every class stays a box.</summary>
    [JSInvokable]
    public Task<Dictionary<string, SceneClassModel?>> DescribeClasses(string[] classPaths)
        => Task.FromResult(new Dictionary<string, SceneClassModel?>(StringComparer.Ordinal));

    /// <summary>The level geometry around a base, as the desktop provider answers it; null when the region has none.</summary>
    [JSInvokable]
    public async Task<SceneLevelSlice?> DescribeLevel(SceneLevelQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Allowed || string.IsNullOrWhiteSpace(query.Region) || query.Min is not { Length: 3 } || query.Max is not { Length: 3 }) return null;
        try
        {
            return await DescribeLevelAsync(query with { MaxInstances = Math.Clamp(query.MaxInstances, 1, 200_000) }, LevelWait).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            EditorLog.Warn("Scene", $"Hosted scenery for {query.Region} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>A level query, waiting up to <paramref name="wait"/> for level files still downloading.</summary>
    public async Task<SceneLevelSlice?> DescribeLevelAsync(SceneLevelQuery query, TimeSpan wait)
    {
        if (!RegionName().IsMatch(query.Region) || await _build.Value.ConfigureAwait(false) is not { } build) return null;
        if (await WorldFor(query.Region).ConfigureAwait(false) is not { } world) return null;
        if (world.AlwaysDrawn.Count == 0 && world.Streamed.Count == 0) return null;

        var a = PointFromViewer(new Vector3(query.Min[0], query.Min[1], query.Min[2]));
        var b = PointFromViewer(new Vector3(query.Max[0], query.Max[1], query.Max[2]));
        var min = Vector3.Min(a, b) - new Vector3(LevelMarginCm);
        var max = Vector3.Max(a, b) + new Vector3(LevelMarginCm);

        var maps = world.AlwaysDrawn
            .Concat(world.Streamed.Where(kv => kv.Value.Any(v => Overlaps(v.Min, v.Max, min, max))).Select(kv => kv.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var centre = (min + max) / 2;
        var excluded = new HashSet<string>(
            (query.ExcludeActors ?? []).Select(ActorName).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        var only = query.OnlyActors is { Count: > 0 }
            ? new HashSet<string>(query.OnlyActors.Select(ActorName).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase)
            : null;
        var openDoors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var door in query.OpenDoors ?? [])
        {
            var bar = door.IndexOf('|', StringComparison.Ordinal);
            openDoors[bar < 0 ? door : door[..bar]] = bar < 0 ? "" : door[(bar + 1)..];
        }

        // Level files download in the background; a query waits a moment for them, then answers with
        // what has arrived and counts the rest as pending, so the view asks again (as on the desktop).
        var reads = maps.Select(LevelFor).ToList();
        await Task.WhenAny(Settled(reads), Task.Delay(wait)).ConfigureAwait(false);
        var ready = new List<Level>();
        var pending = 0;
        foreach (var read in reads)
        {
            if (!read.IsCompleted) pending++;
            else if (read.IsCompletedSuccessfully && read.Result is { } level) ready.Add(level);
        }

        var inBox = new List<(Level Map, Entry Entry, float Distance)>();
        foreach (var index in ready)
        {
            var placement = world.Placements.TryGetValue(index.Map, out var placed) ? placed : Matrix4x4.Identity;
            var (mapMin, mapMax) = TransformBox(index.Min, index.Max, placement);
            if (!Overlaps(mapMin, mapMax, min, max)) continue;
            var mapName = Path.GetFileNameWithoutExtension(index.Map);
            for (var entryIndex = 0; entryIndex < index.Entries.Count; entryIndex++)
            {
                var local = index.Entries[entryIndex];
                var inward = index.DoorOpenInward.Contains(entryIndex);
                var outward = index.DoorOpenOutward.Contains(entryIndex);
                if (index.DoorLeaves.Contains(entryIndex) || inward || outward)
                {
                    var open = openDoors.TryGetValue(mapName + ":" + index.Actors[local.Actor], out var way);
                    if (index.DoorLeaves.Contains(entryIndex) ? open : !(open && way == (inward ? "in" : "out"))) continue;
                }
                var e = placement.IsIdentity ? local : local with { World = local.World * placement, Centre = Vector3.Transform(local.Centre, placement) };
                var closest = Vector3.Clamp(e.Centre, min, max);
                if (e.Radius > MaxPieceRadiusCm || Vector3.DistanceSquared(closest, e.Centre) > e.Radius * e.Radius) continue;
                if (index.HlodMeshes[e.Mesh]) continue;
                if (excluded.Contains(index.Actors[e.Actor])) continue;
                if (only is not null && !only.Contains(index.Actors[e.Actor])) continue;
                inBox.Add((index, e, MathF.Max(0, Vector3.Distance(e.Centre, centre) - e.Radius)));
            }
        }

        var groups = inBox.OrderBy(x => x.Distance).Take(query.MaxInstances)
            .GroupBy(x => (x.Map.Meshes[x.Entry.Mesh], string.Join('|', x.Map.OverrideSets[x.Entry.Overrides])))
            .ToList();
        await FetchDescriptionsAsync(groups.Select(g => (g.First().Map.Meshes[g.First().Entry.Mesh], g.First().Map.OverrideSets[g.First().Entry.Overrides])).ToList()).ConfigureAwait(false);

        var batches = new List<SceneLevelBatch>();
        var actorNames = new List<string>();
        var actorIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int ActorId(Level map, Entry entry)
        {
            var key = Path.GetFileNameWithoutExtension(map.Map) + ":" + map.Actors[entry.Actor];
            if (!actorIds.TryGetValue(key, out var id)) { id = actorNames.Count; actorNames.Add(key); actorIds[key] = id; }
            return id;
        }
        foreach (var group in groups)
        {
            var (first, firstEntry, _) = group.First();
            var mesh = first.Meshes[firstEntry.Mesh];
            var overrides = first.OverrideSets[firstEntry.Overrides];
            if (Known(_meshInfo, mesh) is not { } info || IsEffectOnly(info, overrides)) continue;
            var materials = mesh.Contains(LandMarker, StringComparison.Ordinal) && overrides is [{ } terrainMaterial, ..]
                ? Known(_terrain, terrainMaterial) ?? [new SceneMaterial(null, FallbackColor)]
                : MaterialsFor(info, overrides, mesh.Contains(PoseMarker, StringComparison.Ordinal));
            if (materials.Any(m => m.Decal && (m.Texture is null || !KnownAlpha(m.Texture)))) continue;
            var matrices = new float[group.Count() * 16];
            var actors = new int[group.Count()];
            var i = 0;
            foreach (var x in group)
            {
                ToViewer(x.Entry.World).CopyTo(matrices, i * 16);
                actors[i] = ActorId(x.Map, x.Entry);
                i++;
            }
            batches.Add(new SceneLevelBatch(
                MeshUrl(build, $"mesh/{LevelLod}{mesh}"), materials.Select(m => Hosted(build, m)).ToList(), matrices,
                $"{ShortName(mesh)} ({Path.GetFileNameWithoutExtension(first.Map)})") { Actors = actors });
        }

        var lights = new List<(SceneLevelLight Light, float Distance)>();
        foreach (var index in ready)
        {
            var placement = world.Placements.TryGetValue(index.Map, out var placed) ? placed : Matrix4x4.Identity;
            foreach (var light in index.Lights)
            {
                var at = Vector3.Transform(light.Position, placement);
                if (at.X < min.X || at.Y < min.Y || at.Z < min.Z || at.X > max.X || at.Y > max.Y || at.Z > max.Z) continue;
                if (excluded.Contains(index.Actors[light.Actor])) continue;
                var viewer = PointToViewer(at);
                float[]? direction = null;
                if (light.Direction is { } d)
                {
                    var dv = Vector3.Normalize(PointToViewer(Vector3.TransformNormal(d, placement)));
                    direction = [dv.X, dv.Y, dv.Z];
                }
                lights.Add((new SceneLevelLight([viewer.X, viewer.Y, viewer.Z], [light.Color.X, light.Color.Y, light.Color.Z],
                    light.Brightness, light.Radius / 100f, direction, light.Cone), Vector3.Distance(at, centre)));
            }
        }
        var note = pending > 0 ? $"read {ready.Count} of {maps.Count} level files" : $"{maps.Count} level files";
        return new SceneLevelSlice(batches, inBox.Count, note, pending)
        {
            Actors = actorNames,
            Lights = lights.OrderBy(l => l.Distance).Take(MaxLevelLights).Select(l => l.Light).ToList(),
        };
    }

    // ---- descriptions, fetched together before a slice is put together ------------------------

    /// <summary>
    /// Downloads everything the pieces' materials depend on, a stage at a time (mesh slots, then
    /// materials, then the see-through checks), each stage many files at once.
    /// </summary>
    private async Task FetchDescriptionsAsync(IReadOnlyList<(string Mesh, string?[] Overrides)> pieces)
    {
        await Settled(pieces.Select(p => p.Mesh).Distinct(StringComparer.OrdinalIgnoreCase).Select(MeshInfoOf)).ConfigureAwait(false);
        var materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terrainPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mesh, overrides) in pieces)
        {
            if (Known(_meshInfo, mesh) is not { } info) continue;
            if (mesh.Contains(LandMarker, StringComparison.Ordinal) && overrides is [{ } terrain, ..]) terrainPaths.Add(terrain);
            for (var slot = 0; slot < Math.Max(1, info.Materials.Length); slot++)
            {
                if (SlotMaterial(info, overrides, slot) is { } path) materialPaths.Add(path);
            }
        }
        await Settled(materialPaths.Select(MaterialOf).Cast<Task>().Concat(terrainPaths.Select(TerrainMaterialOf))).ConfigureAwait(false);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in materialPaths)
        {
            if (Known(_materials, path) is not { BaseColorTexture: { } t } m) continue;
            // The checks MaterialsFor and the decal filter make (two-sided cards and decals).
            if ((!m.Masked && m.TwoSided && !m.Decal && m.Opacity >= 1f) || m.Decal) textures.Add($"tex/{LevelTextureSize}{t}");
        }
        foreach (var path in terrainPaths)
        {
            foreach (var m in Known(_terrain, path) ?? [])
                if (m.Decal && m.Texture is { } t) textures.Add(t);
        }
        await Settled(textures.Select(TextureHasAlpha)).ConfigureAwait(false);
    }

    /// <summary>Waits for every download; one that failed is left out of the answer (and tried again next time, see <see cref="Once"/>).</summary>
    private static async Task Settled(IEnumerable<Task> tasks)
    {
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException) { }
    }

    /// <summary>One download per file however many ask; a failed one (the connection dropped) is forgotten so it is retried.</summary>
    private static Task<T> Once<T>(ConcurrentDictionary<string, Task<T>> cache, string key, Func<string, Task<T>> read)
    {
        var task = cache.GetOrAdd(key, read);
        if (!task.IsFaulted && !task.IsCanceled) return task;
        cache.TryRemove(new KeyValuePair<string, Task<T>>(key, task));
        return cache.GetOrAdd(key, read);
    }

    private static string? SlotMaterial(MeshInfo info, IReadOnlyList<string?> overrides, int slot)
        => slot < overrides.Count && overrides[slot] is { } o ? o : slot < info.Materials.Length ? info.Materials[slot] : null;

    private bool IsEffectOnly(MeshInfo info, IReadOnlyList<string?> overrides)
    {
        for (var slot = 0; slot < Math.Max(1, info.Materials.Length); slot++)
        {
            if (SlotMaterial(info, overrides, slot) is not { } path || !(Known(_materials, path) ?? Material.Fallback).Effect) return false;
        }
        return true;
    }

    private List<SceneMaterial> MaterialsFor(MeshInfo info, IReadOnlyList<string?> overrides, bool character)
    {
        var result = new List<SceneMaterial>(info.Materials.Length);
        for (var slot = 0; slot < Math.Max(1, info.Materials.Length); slot++)
        {
            var path = SlotMaterial(info, overrides, slot);
            var m = path is null ? Material.Fallback : Known(_materials, path) ?? Material.Fallback;
            var texture = m.BaseColorTexture is { } t ? $"tex/{LevelTextureSize}{t}" : null;
            var masked = m.Masked || (m.TwoSided && !m.Decal && m.Opacity >= 1f && texture is not null && KnownAlpha(texture));
            if (character && !m.TwoSided) masked = false;
            result.Add(new SceneMaterial(texture, m.Color ?? FallbackColor, m.Opacity, m.TwoSided, masked, m.Emissive) { WorldTileMetres = m.TileCm / 100f, Decal = m.Decal });
        }
        return result;
    }

    private static T? Known<T>(ConcurrentDictionary<string, Task<T?>> cache, string key) where T : class
        => cache.TryGetValue(key, out var task) && task.IsCompletedSuccessfully ? task.Result : null;

    private bool KnownAlpha(string textureId) => _alpha.TryGetValue(textureId, out var task) && task.IsCompletedSuccessfully && task.Result;

    // ---- the hosted files ------------------------------------------------------------------

    private Task<World?> WorldFor(string region) => Once(_worlds, region, async r =>
    {
        if (await ReadJsonAsync<CachedWorld>("worlds", r, ".json").ConfigureAwait(false) is not { } cached) return null;
        var streamed = (cached.Streamed ?? []).ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(b => b.Length == 6).Select(b => (new Vector3(b[0], b[1], b[2]), new Vector3(b[3], b[4], b[5]))).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var placements = (cached.Placements ?? []).Where(kv => kv.Value.Length == 16).ToDictionary(
            kv => kv.Key,
            kv => { var m = kv.Value; return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]); },
            StringComparer.OrdinalIgnoreCase);
        return new World(cached.Always ?? [], streamed, placements);
    });

    private Task<Level?> LevelFor(string map) => Once(_levels, map, async m =>
        await ReadBytesAsync("levels", m, ".bin").ConfigureAwait(false) is { } bytes ? ParseLevel(bytes) : null);

    private Task<MeshInfo?> MeshInfoOf(string mesh) => Once(_meshInfo, mesh, async m =>
        await ReadJsonAsync<MeshInfo>("meshinfo", m, ".json").ConfigureAwait(false) is { Min.Length: 3, Max.Length: 3 } info
            ? info with { Materials = info.Materials ?? [] }
            : null);

    private Task<Material?> MaterialOf(string path) => Once(_materials, path, p => ReadJsonAsync<Material>(MaterialsFolder, p, ".json"));

    private Task<List<SceneMaterial>?> TerrainMaterialOf(string path) => Once(_terrain, path, p => ReadJsonAsync<List<SceneMaterial>>(TerrainMaterialsFolder, p, ".json"));

    private Task<bool> TextureHasAlpha(string textureId) => Once(_alpha, textureId, async id =>
        await ReadJsonAsync<CachedAlpha>(AlphaFolder, id, ".json").ConfigureAwait(false) is { SeeThrough: true });

    private async Task<T?> ReadJsonAsync<T>(string folder, string key, string extension) where T : class
    {
        // The small answers come from the build's one descriptions.json when it has one: a level asks
        // for about a thousand of them, which fetched one by one took 20 seconds on the website.
        if (DescriptionFolders.Contains(folder) && await _descriptions.Value.ConfigureAwait(false) is { } pack)
            return pack.TryGetValue($"{folder}/{Hash(key)}{extension}", out var value) ? value.Deserialize<T>(Json) : null;
        return await ReadBytesAsync(folder, key, extension).ConfigureAwait(false) is { } bytes ? JsonSerializer.Deserialize<T>(bytes, Json) : null;
    }

    /// <summary>Folders whose files are also published together in descriptions.json (DESCRIPTION_FOLDERS in tools/scenery.py).</summary>
    private static readonly HashSet<string> DescriptionFolders = new(StringComparer.Ordinal) { "meshinfo", MaterialsFolder, AlphaFolder, TerrainMaterialsFolder };

    /// <summary>The build's descriptions.json as key to value, or null when it has none (each file is then fetched alone).</summary>
    private async Task<Dictionary<string, JsonElement>?> ReadDescriptionsAsync()
    {
        if (await _build.Value.ConfigureAwait(false) is not { } build) return null;
        Interlocked.Increment(ref _filesAsked);
        try
        {
            using var response = await _http.GetAsync(new Uri(build, "descriptions.json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            var pack = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateObject()) pack[entry.Name] = entry.Value.Clone();
            return pack;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            EditorLog.Info("Scene", $"No hosted scenery descriptions; fetching them one by one: {ex.Message}");
            return null;
        }
        finally { Interlocked.Increment(ref _filesDone); }
    }

    /// <summary>One hosted file by its cache key, or null when the website does not have it.</summary>
    private async Task<byte[]?> ReadBytesAsync(string folder, string key, string extension)
    {
        if (await _build.Value.ConfigureAwait(false) is not { } build) return null;
        Interlocked.Increment(ref _filesAsked);
        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var response = await _http.GetAsync(new Uri(build, PublishedPath($"{folder}/{Hash(key)}{extension}"))).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            // Every hosted file has content. An empty answer ("204 No Content") is what a browser download
            // manager leaves behind when it takes a request for itself: counted as missing, never retried
            // (each retry could ask the player to save the file again), and said once in the log.
            if (bytes.Length == 0)
            {
                if (Interlocked.Exchange(ref _capturedWarned, 1) == 0)
                    EditorLog.Warn("Scene", $"Hosted scenery came back empty ({(int)response.StatusCode}); a download manager may be capturing the website's files.");
                return null;
            }
            return bytes;
        }
        finally
        {
            _downloads.Release();
            Interlocked.Increment(ref _filesDone);
        }
    }

    private int _capturedWarned;

    /// <summary>
    /// Where a cache file is published: level indexes are cached as <c>.bin</c> but published as
    /// <c>.ali</c>, as download managers capture every <c>.bin</c> address (see
    /// <c>published_name</c> in <c>tools/scenery.py</c>).
    /// </summary>
    public static string PublishedPath(string key)
        => key.EndsWith(".bin", StringComparison.Ordinal) ? key[..^4] + ".ali" : key;

    /// <summary>The prepared build the website offers (its <c>index.json</c> names the newest), or null.</summary>
    private async Task<Uri?> FindBuildAsync()
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(_root, "index.json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var index = JsonSerializer.Deserialize<SceneryIndex>(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), Json);
            return index is { Format: 1, Latest: { } latest } && BuildName().IsMatch(latest) ? new Uri(_root, latest + "/") : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            EditorLog.Info("Scene", $"No hosted scenery: {ex.Message}");
            return null;
        }
    }

    /// <summary>A mesh id as the address of its hosted file (the folder follows the provider's kinds of mesh).</summary>
    public static string MeshUrl(Uri build, string assetId)
    {
        var folder = assetId.Contains(LandMarker, StringComparison.Ordinal) ? "meshes-terrain-v3"
            : assetId.Contains(PoseMarker, StringComparison.Ordinal) ? "meshes-posed-v2"
            : "meshes";
        return new Uri(build, $"{folder}/{Hash(assetId)}.abm").AbsoluteUri;
    }

    /// <summary>A texture id as the address of its hosted file.</summary>
    public static string TextureUrl(Uri build, string assetId) => new Uri(build, $"textures/{Hash(assetId)}.png").AbsoluteUri;

    private static SceneMaterial Hosted(Uri build, SceneMaterial m) => m with
    {
        Texture = m.Texture is { } t ? TextureUrl(build, t) : null,
        Layers = m.Layers?.Select(l => l with { Texture = l.Texture is { } lt ? TextureUrl(build, lt) : null }).ToList(),
    };

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // ---- the level index's on-disk form (LevelIndex.Save in the GameModels3D plugin) -------------

    internal static Level? ParseLevel(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes, writable: false));
            if (r.ReadUInt32() != LevelMagic || r.ReadInt32() != LevelFormatVersion) return null;
            var map = r.ReadString();
            var min = ReadVector(r);
            var max = ReadVector(r);
            var meshes = new string[r.ReadInt32()];
            for (var i = 0; i < meshes.Length; i++) meshes[i] = r.ReadString();
            var sets = new string?[r.ReadInt32()][];
            for (var i = 0; i < sets.Length; i++)
            {
                var set = new string?[r.ReadInt32()];
                for (var j = 0; j < set.Length; j++) set[j] = r.ReadString() is { Length: > 0 } s ? s : null;
                sets[i] = set;
            }
            var actors = new string[r.ReadInt32()];
            for (var i = 0; i < actors.Length; i++) actors[i] = r.ReadString();
            var entries = new List<Entry>(r.ReadInt32());
            for (var i = entries.Capacity; i > 0; i--)
            {
                var mesh = r.ReadInt32();
                var overrides = r.ReadInt32();
                var actor = r.ReadInt32();
                var f = new float[12];
                for (var j = 0; j < 12; j++) f[j] = r.ReadSingle();
                var m = new Matrix4x4(f[0], f[1], f[2], 0, f[3], f[4], f[5], 0, f[6], f[7], f[8], 0, f[9], f[10], f[11], 1);
                entries.Add(new Entry(mesh, overrides, actor, m, ReadVector(r), r.ReadSingle()));
            }
            var lights = new List<Light>(r.ReadInt32());
            for (var i = lights.Capacity; i > 0; i--)
            {
                var position = ReadVector(r);
                var colour = ReadVector(r);
                var brightness = r.ReadSingle();
                var radius = r.ReadSingle();
                var hasDirection = r.ReadBoolean();
                var direction = ReadVector(r);
                var cone = r.ReadSingle();
                lights.Add(new Light(position, colour, brightness, radius, hasDirection ? direction : null, cone < 0 ? null : cone, r.ReadInt32()));
            }
            HashSet<int> ReadSet()
            {
                var set = new HashSet<int>();
                for (var i = r.ReadInt32(); i > 0; i--) set.Add(r.ReadInt32());
                return set;
            }
            var leaves = ReadSet();
            var inward = ReadSet();
            var outward = ReadSet();
            return new Level(map, meshes, sets, actors, entries, lights, leaves, inward, outward, min, max,
                meshes.Select(m => m.Contains("/HLOD/", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    // ---- space (SceneMath in the plugin) ---------------------------------------------------------

    private static readonly Matrix4x4 ToViewerBasis = new(0.01f, 0, 0, 0, 0, 0, 0.01f, 0, 0, 0.01f, 0, 0, 0, 0, 0, 1);
    private static readonly Matrix4x4 FromViewerBasis = new(100f, 0, 0, 0, 0, 0, 100f, 0, 0, 100f, 0, 0, 0, 0, 0, 1);

    private static float[] ToViewer(Matrix4x4 unreal)
    {
        var m = FromViewerBasis * unreal * ToViewerBasis;
        return [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    }

    private static Vector3 PointToViewer(Vector3 unreal) => new(unreal.X / 100f, unreal.Z / 100f, unreal.Y / 100f);

    private static Vector3 PointFromViewer(Vector3 viewer) => new(viewer.X * 100f, viewer.Z * 100f, viewer.Y * 100f);

    private static (Vector3 Min, Vector3 Max) TransformBox(Vector3 boxMin, Vector3 boxMax, Matrix4x4 m)
    {
        if (m.IsIdentity) return (boxMin, boxMax);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new Vector3((i & 1) == 0 ? boxMin.X : boxMax.X, (i & 2) == 0 ? boxMin.Y : boxMax.Y, (i & 4) == 0 ? boxMin.Z : boxMax.Z), m);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    private static bool Overlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        => aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

    private static string ActorName(string actor)
    {
        var s = actor.Trim();
        var cut = Math.Max(s.LastIndexOf('.'), s.LastIndexOf(':'));
        return cut >= 0 ? s[(cut + 1)..] : s;
    }

    private static string ShortName(string objectPath)
    {
        var dot = objectPath.LastIndexOf('.');
        return dot >= 0 ? objectPath[(dot + 1)..] : objectPath;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,80}$")]
    private static partial Regex RegionName();

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex BuildName();

    // ---- shapes of the hosted files --------------------------------------------------------------

    internal sealed record SceneryIndex(int Format, string? Latest);

    private sealed record CachedWorld(string[]? Always, Dictionary<string, float[][]>? Streamed, Dictionary<string, float[]>? Placements);

    private sealed record World(
        IReadOnlyList<string> AlwaysDrawn,
        Dictionary<string, List<(Vector3 Min, Vector3 Max)>> Streamed,
        Dictionary<string, Matrix4x4> Placements);

    private sealed record MeshInfo(string?[] Materials, float[]? Min, float[]? Max);

    private sealed record Material(string? BaseColorTexture, float[]? Color, float Opacity, bool TwoSided, bool Masked, bool Emissive, bool Effect = false, float TileCm = 0, bool Decal = false)
    {
        public static readonly Material Fallback = new(null, FallbackColor, 1f, false, false, false);
    }

    private sealed record CachedAlpha(bool SeeThrough);

    internal readonly record struct Entry(int Mesh, int Overrides, int Actor, Matrix4x4 World, Vector3 Centre, float Radius);

    internal readonly record struct Light(Vector3 Position, Vector3 Color, float Brightness, float Radius, Vector3? Direction, float? Cone, int Actor);

    internal sealed record Level(
        string Map, string[] Meshes, string?[][] OverrideSets, string[] Actors, List<Entry> Entries, List<Light> Lights,
        HashSet<int> DoorLeaves, HashSet<int> DoorOpenInward, HashSet<int> DoorOpenOutward, Vector3 Min, Vector3 Max, bool[] HlodMeshes);
}
