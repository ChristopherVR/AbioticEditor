using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Scene;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Serves the game's own meshes, textures and level geometry to the 3D view, read from the
/// player's installed game through the host's shared archive mount.
/// </summary>
/// <remarks>
/// <para>
/// Asset ids carry the game path they stand for (<c>mesh/&lt;lod&gt;/Game/...</c>,
/// <c>tex/&lt;max size&gt;/Game/...</c>), so they survive restarts; every id is checked against a strict
/// pattern and only ever resolves to a game asset of the expected type, never to a file path.
/// </para>
/// <para>
/// Everything extracted is cached under the plugin's data folder, in a sub-folder stamped with
/// the installed archives' names, sizes and dates, so a game update starts a fresh cache.
/// </para>
/// </remarks>
internal sealed partial class PakSceneModelProvider : ISceneModelProvider
{
    /// <summary>Bumped when the cached formats change.</summary>
    private const int CacheVersion = 1;

    // Answers that depend on how materials are read carry their own version, so a reader fix does not
    // throw away the slow level indexes and baked meshes. v2: blend modes read as enum names.
    private const string MaterialsFolder = "materials-v2";
    private const string ClassesFolder = "classes-v2";

    private const int ObjectTextureSize = 1024;
    private const int LevelTextureSize = 512;
    private const int LevelLod = 1;
    private const float LevelMarginCm = 300f;

    /// <summary>Pieces wider than this (sky domes, distant backdrops) are left out: their bounds hold everything.</summary>
    private const float MaxPieceRadiusCm = 40000f;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IPluginHost _host;
    private readonly Lazy<string> _cacheRoot;
    private readonly ConcurrentDictionary<string, MeshInfo?> _meshInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ResolvedMaterial> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LevelIndexData> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexingLock = new();
    private Queue<string> _indexQueue = new();
    private Task? _indexing;
    private readonly ConcurrentDictionary<string, WorldMaps> _worlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Dictionary<string, string>> _mapsByName;

    public PakSceneModelProvider(IPluginHost host)
    {
        _host = host;
        _cacheRoot = new Lazy<string>(() => Path.Combine(host.DataDirectory, "cache", InstallStamp()));
        _mapsByName = new Lazy<Dictionary<string, string>>(IndexMapNames);
    }

    public string Id => "pak-models";

    public string Title => "Game models";

    public bool IsAvailable => Assets() is { HasMappings: true };

    private static GameAssetProvider? Assets() => PluginHostEnvironment.GameAssets();

    private static T Read<T>(Func<IFileProvider, T> read)
        => Assets() is { } assets ? assets.UseFileProvider(read) : throw new InvalidOperationException("The game is not available.");

    // ---- classes ----------------------------------------------------------------------------

    public SceneClassModel? DescribeClass(string classPath)
    {
        if (string.IsNullOrWhiteSpace(classPath) || !GamePath().IsMatch(classPath)) return null;
        var cacheFile = CachePath(ClassesFolder, classPath, ".json");
        if (TryReadJson<CachedClass>(cacheFile) is { } cached) return cached.Model;

        var model = BuildClass(classPath);
        WriteJson(cacheFile, new CachedClass(model));
        return model;
    }

    /// <summary>
    /// A painted object: its own mesh parts wear the paint colour's materials slot by slot, the way
    /// the game applies them (<see cref="PaintResolver"/>); a class that cannot be painted, or a colour
    /// with nothing for it, looks as unpainted.
    /// </summary>
    public SceneClassModel? DescribeClass(string classPath, int paintColor)
    {
        if (paintColor == AbioticEditor.Core.WorldSaves.DeployablePaintCatalog.NoneValue) return DescribeClass(classPath);
        if (string.IsNullOrWhiteSpace(classPath) || !GamePath().IsMatch(classPath)) return null;
        var cacheFile = CachePath(ClassesFolder, $"{classPath}#paint={paintColor}", ".json");
        if (TryReadJson<CachedClass>(cacheFile) is { } cached) return cached.Model;

        var paint = Read(p => PaintResolver.Materials(p, classPath, paintColor));
        var model = paint is null ? DescribeClass(classPath) : BuildClass(classPath, paint);
        WriteJson(cacheFile, new CachedClass(model));
        return model;
    }

    /// <summary>A cached answer, including "no model" so a class without one is not re-read every run.</summary>
    private sealed record CachedClass(SceneClassModel? Model);

    private SceneClassModel? BuildClass(string classPath, IReadOnlyList<string?>? paint = null)
    {
        var parts = Read(p => ClassModelResolver.Resolve(p, classPath));
        if (parts.Count == 0) return null;

        var scene = new List<ScenePart>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
        {
            if (MeshInfoOf(part.Mesh) is not { } info || IsEffectOnly(info, part.MaterialOverrides)) continue;
            SceneMath.Encapsulate(ref min, ref max, info.BoundsMin, info.BoundsMax, part.Local);
            // Paint reaches the actor's own meshes only; attached child actors (plug sockets) keep theirs.
            var overrides = paint is not null && !part.Name.Contains('/', StringComparison.Ordinal)
                ? Painted(part.MaterialOverrides, paint)
                : part.MaterialOverrides;
            scene.Add(new ScenePart(
                $"mesh/0{part.Mesh}", SceneMath.ToViewer(part.Local),
                MaterialsFor(info, overrides, ObjectTextureSize), part.Name));
        }
        if (scene.Count == 0) return null;
        return new SceneClassModel(scene, [min.X, min.Y, min.Z], [max.X, max.Y, max.Z]);
    }

    private static string?[] Painted(IReadOnlyList<string?> overrides, IReadOnlyList<string?> paint)
    {
        var merged = new string?[Math.Max(overrides.Count, paint.Count)];
        for (var slot = 0; slot < merged.Length; slot++)
            merged[slot] = slot < paint.Count && paint[slot] is { } painted ? painted : slot < overrides.Count ? overrides[slot] : null;
        return merged;
    }

    // ---- levels -----------------------------------------------------------------------------

    public SceneLevelSlice? DescribeLevel(SceneLevelQuery query)
    {
        if (!RegionName().IsMatch(query.Region)) return null;
        var world = WorldFor(query.Region);
        if (world.AlwaysDrawn.Count == 0 && world.Streamed.Count == 0) return null;

        var a = SceneMath.PointFromViewer(new Vector3(query.Min[0], query.Min[1], query.Min[2]));
        var b = SceneMath.PointFromViewer(new Vector3(query.Max[0], query.Max[1], query.Max[2]));
        var min = Vector3.Min(a, b) - new Vector3(LevelMarginCm);
        var max = Vector3.Max(a, b) + new Vector3(LevelMarginCm);

        // Streamed areas count only where the game would load them: one of their streaming
        // volumes overlaps the box. Set pieces loaded by script (vignettes) have no volume and
        // share the same space, so they stay out.
        var maps = world.AlwaysDrawn
            .Concat(world.Streamed.Where(kv => kv.Value.Any(v => Overlaps(v.Min, v.Max, min, max))).Select(kv => kv.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var centre = (min + max) / 2;
        var excluded = new HashSet<string>(
            (query.ExcludeActors ?? []).Select(ActorName).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

        var ready = new List<LevelIndexData>();
        var pending = 0;
        foreach (var map in maps)
        {
            if (_levels.TryGetValue(map, out var index)) ready.Add(index);
            else pending++;
        }
        if (pending > 0) StartIndexing(maps);

        // A piece is in when its bounding sphere touches the box; the nearest surfaces win the cap,
        // so the floor and walls a base stands in come before distant scenery.
        // Indexes are kept in each map's own space; a streamed map is placed into the world by its
        // streaming transform (an offset and a turn), applied here.
        var inBox = new List<(LevelIndexData Map, LevelEntry Entry, float Distance)>();
        foreach (var index in ready)
        {
            var placement = world.Placements.TryGetValue(index.Map, out var placed) ? placed : Matrix4x4.Identity;
            var (mapMin, mapMax) = TransformBox(index.Min, index.Max, placement);
            if (!Overlaps(mapMin, mapMax, min, max)) continue;
            foreach (var local in index.Entries)
            {
                var e = placement.IsIdentity ? local : local with { World = local.World * placement, Centre = Vector3.Transform(local.Centre, placement) };
                var closest = Vector3.Clamp(e.Centre, min, max);
                if (e.Radius > MaxPieceRadiusCm || Vector3.DistanceSquared(closest, e.Centre) > e.Radius * e.Radius) continue;
                if (excluded.Contains(index.Actors[e.Actor])) continue;
                inBox.Add((index, e, MathF.Max(0, Vector3.Distance(e.Centre, centre) - e.Radius)));
            }
        }

        var batches = new List<SceneLevelBatch>();
        var kept = inBox.OrderBy(x => x.Distance).Take(query.MaxInstances);
        foreach (var group in kept.GroupBy(x => (x.Map.Meshes[x.Entry.Mesh], string.Join('|', x.Map.OverrideSets[x.Entry.Overrides]))))
        {
            var (first, firstEntry, _) = group.First();
            var mesh = first.Meshes[firstEntry.Mesh];
            if (MeshInfoOf(mesh) is not { } info || IsEffectOnly(info, first.OverrideSets[firstEntry.Overrides])) continue;
            var matrices = new float[group.Count() * 16];
            var i = 0;
            foreach (var x in group)
            {
                SceneMath.ToViewer(x.Entry.World).CopyTo(matrices, i * 16);
                i++;
            }
            batches.Add(new SceneLevelBatch(
                $"mesh/{LevelLod}{mesh}", MaterialsFor(info, first.OverrideSets[firstEntry.Overrides], LevelTextureSize), matrices, $"{ShortName(mesh)} ({Path.GetFileNameWithoutExtension(first.Map)})"));
        }
        var note = pending > 0
            ? $"read {ready.Count} of {maps.Count} level files"
            : $"{maps.Count} level files";
        return new SceneLevelSlice(batches, inBox.Count, note, pending);
    }

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

    /// <summary>
    /// Reads the given maps in the background, cached ones first (they load in milliseconds), so a
    /// query never waits minutes: it answers with what is ready and reports the rest as pending.
    /// </summary>
    private void StartIndexing(IReadOnlyList<string> maps)
    {
        lock (_indexingLock)
        {
            foreach (var map in maps)
            {
                if (!_levels.ContainsKey(map) && !_indexQueue.Contains(map, StringComparer.OrdinalIgnoreCase)) _indexQueue.Enqueue(map);
            }
            if (_indexing is { IsCompleted: false } || _indexQueue.Count == 0) return;
            _indexing = Task.Run(IndexQueuedMaps);
        }
    }

    private void IndexQueuedMaps()
    {
        while (true)
        {
            string map;
            lock (_indexingLock)
            {
                if (_indexQueue.Count == 0) return;
                // Cached maps first: they cost milliseconds and fill the view straight away.
                map = _indexQueue.FirstOrDefault(m => File.Exists(LevelCachePath(m))) ?? _indexQueue.Peek();
                _indexQueue = new Queue<string>(_indexQueue.Where(m => !string.Equals(m, map, StringComparison.OrdinalIgnoreCase)));
            }
            try
            {
                _levels[map] = LoadOrBuildLevel(map);
            }
            catch (Exception ex)
            {
                _host.Log.Warn($"Could not read level {map}: {ex.Message}");
                _levels[map] = new LevelIndexData { Map = map, Meshes = [], OverrideSets = [], Actors = [], Entries = [] };
            }
        }
    }

    /// <summary>Waits for background level reading to finish (for tests and research probes).</summary>
    internal void WaitForLevels(TimeSpan timeout)
    {
        Task? running;
        lock (_indexingLock) running = _indexing;
        running?.Wait(timeout);
    }

    private LevelIndexData LoadOrBuildLevel(string map)
    {
        var file = LevelCachePath(map);
        if (LevelIndex.Load(file) is { } cached) return cached;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var built = Read(p => LevelIndex.Build(p, map));
        // Mesh bounds are read outside the archive lock one mesh at a time (and cached across
        // maps, which share most of their meshes), so other extraction is not held up.
        LevelIndex.ApplyBounds(built, MeshInfoOf);
        _host.Log.Info($"Indexed {built.Entries.Count} mesh instances ({built.Meshes.Count} meshes) in {Path.GetFileNameWithoutExtension(map)} ({started.ElapsedMilliseconds} ms).");
        try { LevelIndex.Save(file, built); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _host.Log.Warn($"Could not cache {map}: {ex.Message}"); }
        return built;
    }

    private string LevelCachePath(string map) => CachePath("levels", map, ".bin");

    /// <summary>
    /// The level files that make up the world a region belongs to. The outermost map its name nests
    /// under (<c>Facility_Dam_Central</c> sits in <c>Facility</c>) and the region's own map are always
    /// drawn; every map the outer one streams in is drawn only inside the streaming volumes that load
    /// it (see <see cref="StreamingVolumes"/>). Found by name and by the level data, so new regions need
    /// no list. Streamed areas matter because player-built pieces are kept in the save of the
    /// persistent level even when they stand inside a streamed area.
    /// </summary>
    private WorldMaps WorldFor(string region) => _worlds.GetOrAdd(region, r =>
    {
        var byName = _mapsByName.Value;
        var parts = r.Split('_');
        string? root = null;
        for (var k = 1; k <= parts.Length && root is null; k++)
        {
            if (byName.TryGetValue(string.Join('_', parts[..k]), out var candidate)) root = candidate;
        }
        var always = new List<string>();
        if (root is not null) always.Add(root);
        if (byName.TryGetValue(r, out var own) && !always.Contains(own, StringComparer.OrdinalIgnoreCase)) always.Add(own);

        var placements = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        if (root is not null)
        {
            foreach (var (name, placement) in Read(p => StreamedPlacements(p, root)))
            {
                if (byName.TryGetValue(name, out var path)) placements[path] = placement;
            }
        }

        var streamed = new Dictionary<string, List<(Vector3 Min, Vector3 Max)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in always)
        {
            foreach (var (target, box) in Read(p => StreamingVolumes(p, map)))
            {
                if (!byName.TryGetValue(target, out var targetPath) || always.Contains(targetPath, StringComparer.OrdinalIgnoreCase)) continue;
                if (!streamed.TryGetValue(targetPath, out var boxes)) streamed[targetPath] = boxes = [];
                boxes.Add(box);
            }
        }
        return new WorldMaps(always, streamed, placements);
    });

    private static CUE4Parse.UE4.Objects.Core.Math.FVector[] CubeCorners(Vector3 half)
        => Enumerable.Range(0, 8)
            .Select(i => new CUE4Parse.UE4.Objects.Core.Math.FVector((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z))
            .ToArray();

    private sealed record WorldMaps(
        IReadOnlyList<string> AlwaysDrawn,
        IReadOnlyDictionary<string, List<(Vector3 Min, Vector3 Max)>> Streamed,
        IReadOnlyDictionary<string, Matrix4x4> Placements);

    /// <summary>
    /// Where each map a level streams in sits in the world: its streaming entry's
    /// <c>LevelTransform</c> (sub-levels are authored around their own origin and moved and turned
    /// into place). A scale the data leaves at zero is the unset default, so it means one.
    /// </summary>
    internal static List<(string Map, Matrix4x4 Placement)> StreamedPlacements(IFileProvider provider, string mapPackage)
    {
        var result = new List<(string, Matrix4x4)>();
        if (!provider.TryLoadPackage(mapPackage, out var package)) return result;
        var world = package.GetExports().OfType<UWorld>().FirstOrDefault();
        foreach (var index in world?.StreamingLevels ?? [])
        {
            var streaming = index.Load();
            var asset = streaming?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FSoftObjectPath>("WorldAsset").AssetPathName.Text;
            if (streaming is null || string.IsNullOrEmpty(asset) || asset == "None") continue;
            var name = asset[(asset.LastIndexOf('.') + 1)..];
            if (!streaming.TryGetValue(out CUE4Parse.UE4.Objects.Core.Math.FTransform transform, "LevelTransform")) continue;
            var scale = transform.Scale3D;
            if (scale.X == 0 && scale.Y == 0 && scale.Z == 0) scale = CUE4Parse.UE4.Objects.Core.Math.FVector.OneVector;
            result.Add((name, SceneMath.Transform(transform.Translation, transform.Rotation, scale)));
        }
        return result;
    }

    /// <summary>
    /// Every volume in a map that streams another map in, as (map name, world box). Any actor with a
    /// <c>LevelToLoad</c> path counts (the game's own streaming volume class), plus the engine's
    /// <c>StreamingLevelNames</c> list, so no class is named here. The box is the volume brush's
    /// size, scaled and turned by its root component.
    /// </summary>
    internal static List<(string Map, (Vector3 Min, Vector3 Max) Box)> StreamingVolumes(IFileProvider provider, string mapPackage)
    {
        var result = new List<(string, (Vector3, Vector3))>();
        if (!provider.TryLoadPackage(mapPackage, out var package)) return result;
        foreach (var actor in package.GetExports())
        {
            var targets = new List<string>();
            if (actor.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FSoftObjectPath level, "LevelToLoad") && level.AssetPathName.Text is { Length: > 0 } path && path != "None")
                targets.Add(path[(path.LastIndexOf('.') + 1)..]);
            foreach (var n in actor.GetOrDefault<FName[]>("StreamingLevelNames", []))
                targets.Add(n.Text[(n.Text.LastIndexOf('/') + 1)..]);
            if (targets.Count == 0) continue;

            var root = actor.GetOrDefault<FPackageIndex?>("RootComponent")?.Load();
            if (root is null) continue;
            var world = SceneMath.Transform(
                Props.Get(root, "RelativeLocation", CUE4Parse.UE4.Objects.Core.Math.FVector.ZeroVector),
                Props.Get(root, "RelativeRotation", CUE4Parse.UE4.Objects.Core.Math.FRotator.ZeroRotator),
                Props.Get(root, "RelativeScale3D", CUE4Parse.UE4.Objects.Core.Math.FVector.OneVector));
            // The volume's shape is its brush: the model's points, in the actor's own space. The
            // brush builder's X, Y, Z are only a fallback (they are not the placed size).
            var points = (actor.GetOrDefault<FPackageIndex?>("Brush")?.Load() as CUE4Parse.UE4.Objects.Engine.UModel)?.Points
                         ?? CubeCorners(new Vector3(actor.GetOrDefault("X", 200f), actor.GetOrDefault("Y", 200f), actor.GetOrDefault("Z", 200f)) / 2);
            if (points.Length == 0) continue;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var point in points)
            {
                var p = Vector3.Transform(new Vector3(point.X, point.Y, point.Z), world);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            foreach (var t in targets) result.Add((t, (min, max)));
        }
        return result;
    }

    private Dictionary<string, string> IndexMapNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assets = Assets();
        if (assets is null) return map;
        foreach (var path in assets.AssetPaths)
        {
            if (!path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            // Prefer the game's Maps folder when two maps share a name.
            if (!map.ContainsKey(name) || path.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)) map[name] = path;
        }
        return map;
    }

    private static string ActorName(string actor)
    {
        var s = actor.Trim();
        var cut = Math.Max(s.LastIndexOf('.'), s.LastIndexOf(':'));
        return cut >= 0 ? s[(cut + 1)..] : s;
    }

    // ---- meshes and materials ---------------------------------------------------------------

    private MeshInfo? MeshInfoOf(string meshPath) => _meshInfo.GetOrAdd(meshPath, path =>
    {
        var file = CachePath("meshinfo", path, ".json");
        if (TryReadJson<CachedMeshInfo>(file) is { } cached)
        {
            return cached.Min is null || cached.Max is null ? null
                : new MeshInfo(cached.Materials ?? [], new Vector3(cached.Min[0], cached.Min[1], cached.Min[2]), new Vector3(cached.Max[0], cached.Max[1], cached.Max[2]));
        }
        var info = Read(p => p.TryLoadPackageObject(path, out var obj) ? MeshBaker.Describe(obj) : null);
        WriteJson(file, info is null
            ? new CachedMeshInfo(null, null, null)
            : new CachedMeshInfo(info.Materials.ToArray(), [info.BoundsMin.X, info.BoundsMin.Y, info.BoundsMin.Z], [info.BoundsMax.X, info.BoundsMax.Y, info.BoundsMax.Z]));
        return info;
    });

    private sealed record CachedMeshInfo(string?[]? Materials, float[]? Min, float[]? Max);

    private List<SceneMaterial> MaterialsFor(MeshInfo info, IReadOnlyList<string?> overrides, int textureSize)
    {
        var result = new List<SceneMaterial>(info.Materials.Count);
        for (var slot = 0; slot < Math.Max(1, info.Materials.Count); slot++)
        {
            var path = slot < overrides.Count && overrides[slot] is { } o ? o : slot < info.Materials.Count ? info.Materials[slot] : null;
            var m = path is null ? ResolvedMaterial.Fallback : MaterialOf(path);
            result.Add(new SceneMaterial(
                m.BaseColorTexture is { } t ? $"tex/{textureSize}{t}" : null,
                m.Color, m.Opacity, m.TwoSided, m.Masked, m.Emissive));
        }
        return result;
    }

    /// <summary>
    /// True when every material of the mesh is a see-through glow (a fake light beam or glow card);
    /// drawn without the game's lighting they are solid shapes that hide the room.
    /// </summary>
    private bool IsEffectOnly(MeshInfo info, IReadOnlyList<string?> overrides)
    {
        var slots = Math.Max(1, info.Materials.Count);
        for (var slot = 0; slot < slots; slot++)
        {
            var path = slot < overrides.Count && overrides[slot] is { } o ? o : slot < info.Materials.Count ? info.Materials[slot] : null;
            if (path is null || !MaterialOf(path).Effect) return false;
        }
        return true;
    }

    private ResolvedMaterial MaterialOf(string materialPath) => _materials.GetOrAdd(materialPath, path =>
    {
        var file = CachePath(MaterialsFolder, path, ".json");
        if (TryReadJson<ResolvedMaterial>(file) is { } cached) return cached;
        var resolved = Read(p => p.TryLoadPackageObject(path, out var obj) ? MaterialResolver.Resolve(obj as UMaterialInterface) : ResolvedMaterial.Fallback);
        WriteJson(file, resolved);
        return resolved;
    });

    // ---- assets -----------------------------------------------------------------------------

    public SceneAsset? OpenAsset(string assetId)
    {
        var match = AssetId().Match(assetId ?? string.Empty);
        if (!match.Success || assetId!.Contains("..", StringComparison.Ordinal)) return null;
        var kind = match.Groups["kind"].Value;
        var size = int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var path = match.Groups["path"].Value;
        var isMesh = kind == "mesh";
        var file = CachePath(isMesh ? "meshes" : "textures", assetId, isMesh ? ".abm" : ".png");
        var contentType = isMesh ? SceneMeshFormat.ContentType : "image/png";
        if (File.Exists(file))
        {
            var bytes = File.ReadAllBytes(file);
            return bytes.Length == 0 ? null : new SceneAsset(contentType, bytes);
        }

        var data = isMesh
            ? Read(p => p.TryLoadPackageObject(path, out var obj) ? MeshBaker.Bake(obj, size) : null)
            : BakeTexture(path, Math.Clamp(size, 16, 2048));
        WriteBytes(file, data ?? []);
        return data is null ? null : new SceneAsset(contentType, data);
    }

    private static byte[]? BakeTexture(string path, int maxSize)
    {
        // Decode under the archive lock (it reads the pak), resize and encode outside it.
        var decoded = Read(p => p.TryLoadPackageObject(path, out var obj) && obj is UTexture2D texture
            ? DecodeWithFallback(texture, maxSize)
            : null);
        if (decoded is null) return null;
        using var bitmap = decoded.ToSkBitmap();
        var scale = Math.Min(1f, maxSize / (float)Math.Max(bitmap.Width, bitmap.Height));
        using var sized = scale < 1f
            ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), SKFilterQuality.Medium)
            : null;
        using var image = SKImage.FromBitmap(sized ?? bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }

    /// <summary>
    /// On Windows CUE4Parse decodes some block formats (BC7 among them) with a native library the
    /// editor does not ship or initialise, which throws "not initialized". The managed decoder
    /// handles every format, so the first such failure switches decoding to it for the process
    /// (the native path could not have worked for anyone else either) and the texture is retried.
    /// </summary>
    private static CTexture? DecodeWithFallback(UTexture2D texture, int maxSize)
    {
        try
        {
            return texture.Decode(maxSize);
        }
        catch (Exception ex) when (!TextureDecoder.UseAssetRipperTextureDecoder && ex.Message.Contains("not initialized", StringComparison.OrdinalIgnoreCase))
        {
            TextureDecoder.UseAssetRipperTextureDecoder = true;
            return texture.Decode(maxSize);
        }
    }

    // ---- cache ------------------------------------------------------------------------------

    private string CachePath(string folder, string key, string extension)
        => Path.Combine(_cacheRoot.Value, folder, Hash(key) + extension);

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string InstallStamp()
    {
        var builder = new StringBuilder($"v{CacheVersion}|");
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is not null && Directory.Exists(paks))
        {
            foreach (var file in Directory.EnumerateFiles(paks).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                builder.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
            }
        }
        return Hash(builder.ToString())[..16];
    }

    private T? TryReadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _host.Log.Warn($"Ignoring unreadable cache file {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private void WriteJson<T>(string path, T value) => WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, Json));

    private void WriteBytes(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Log.Warn($"Could not cache {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static string ShortName(string objectPath)
    {
        var dot = objectPath.LastIndexOf('.');
        return dot >= 0 ? objectPath[(dot + 1)..] : objectPath;
    }

    /// <summary>A game object path: <c>/Mount/Folder/Name.Name</c> (optionally <c>_C</c> for a class).</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9_]+(/[A-Za-z0-9_\- ]+)+\.[A-Za-z0-9_\- ]+$")]
    private static partial Regex GamePath();

    [GeneratedRegex(@"^(?<kind>mesh|tex)/(?<n>\d{1,4})(?<path>/[A-Za-z0-9_]+(/[A-Za-z0-9_\- ]+)+\.[A-Za-z0-9_\- ]+)$")]
    private static partial Regex AssetId();

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,80}$")]
    private static partial Regex RegionName();
}
