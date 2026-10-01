using System.IO;
using System.Linq;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Plugins.Scene;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Drives the optional GameModels3D plugin's provider against the installed game and a real base:
/// class descriptions, baked meshes and textures, a level slice, with timings. Output-only.
/// </summary>
public class GameModelsProviderProbe
{
    private readonly ITestOutputHelper _output;
    public GameModelsProviderProbe(ITestOutputHelper output) => _output = output;

    private sealed class ProbeHost(string dir, ITestOutputHelper output) : AbioticEditor.Plugins.IPluginHost, AbioticEditor.Plugins.IPluginLog
    {
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(2, 21);
        public string HostKind => "probe";
        public AbioticEditor.Plugins.IPluginLog Log => this;
        public AbioticEditor.Plugins.IHostUi Ui => AbioticEditor.Plugins.NullHostUi.Instance;
        public string DataDirectory => dir;
        public void Info(string message) => output.WriteLine("[info] " + message);
        public void Warn(string message) => output.WriteLine("[warn] " + message);
        public void Error(string message, Exception? exception = null) => output.WriteLine("[error] " + message + " " + exception?.Message);
    }

    private static string? FacilitySave()
    {
        var root = Fixtures.ServerWorldsDir;
        if (root is null) return null;
        foreach (var dir in new[] { root, Path.Combine(root, "Cascade") })
        {
            var p = Path.Combine(dir, "WorldSave_Facility.sav");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void Describe_Classes_Assets_And_Level()
    {
        var assets = GameAssetProvider.CreateForLocalInstall();
        var save = FacilitySave();
        if (assets is null || save is null) { _output.WriteLine("paks or fixture missing"); return; }
        AbioticEditor.Core.Plugins.PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-models-probe");
        var provider = new AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider(new ProbeHost(dir, _output));
        _output.WriteLine($"available={provider.IsAvailable} cache={dir}");

        var census = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(save));
        var objects = census.Objects!.Where(o => o.ClassPath is not null && o.Transform?.Translation is not null).ToList();
        var classes = objects.Select(o => o.ClassPath!).Distinct().ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var models = classes.ToDictionary(c => c, c => provider.DescribeClass(c));
        _output.WriteLine($"described {classes.Count} classes in {sw.ElapsedMilliseconds} ms; with model: {models.Count(m => m.Value is not null)}");
        foreach (var (c, _) in models.Where(m => m.Value is null).Take(15)) _output.WriteLine("   no model: " + c);
        sw.Restart();
        models = classes.ToDictionary(c => c, c => provider.DescribeClass(c));
        _output.WriteLine($"second pass (disk cache) {sw.ElapsedMilliseconds} ms");

        foreach (var (c, m) in models.Where(m => m.Value is not null).Take(6))
        {
            _output.WriteLine($"\n{c}\n   bounds {string.Join(",", m!.BoundsMin.Select(f => f.ToString("0.00")))} .. {string.Join(",", m.BoundsMax.Select(f => f.ToString("0.00")))}");
            foreach (var p in m.Parts)
            {
                _output.WriteLine($"   part {p.Name} mesh={p.Mesh} t=({p.Matrix[12]:0.00},{p.Matrix[13]:0.00},{p.Matrix[14]:0.00})");
                foreach (var mat in p.Materials) _output.WriteLine($"      tex={mat.Texture} color={string.Join(",", mat.Color.Select(f => f.ToString("0.00")))} op={mat.Opacity} two={mat.TwoSided} masked={mat.Masked} emissive={mat.Emissive}");
            }
        }

        sw.Restart();
        long meshBytes = 0, texBytes = 0;
        int meshes = 0, textures = 0, failed = 0;
        foreach (var m in models.Values.Where(m => m is not null).Take(20))
        {
            foreach (var p in m!.Parts)
            {
                var a = provider.OpenAsset(p.Mesh);
                if (a is null) { failed++; _output.WriteLine("   mesh failed " + p.Mesh); } else { meshes++; meshBytes += a.Data.Length; }
                foreach (var mat in p.Materials.Where(x => x.Texture is not null))
                {
                    var t = provider.OpenAsset(mat.Texture!);
                    if (t is null) { failed++; _output.WriteLine("   tex failed " + mat.Texture); } else { textures++; texBytes += t.Data.Length; }
                }
            }
        }
        _output.WriteLine($"\nbaked {meshes} meshes ({meshBytes / 1024} KB), {textures} textures ({texBytes / 1024} KB), {failed} failed in {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"invalid ids rejected: {provider.OpenAsset("mesh/0/../../etc/passwd") is null} {provider.OpenAsset("tex/512C:/Windows/win.ini") is null} {provider.OpenAsset("mesh/0/Game/Nope/Nothing.Nothing") is null}");

        var built = objects.Where(o => o.DeployedByPlayer == true).Select(o => o.Transform!.Translation!.Value).ToList();
        var mx = built.Select(t => t.X).OrderBy(x => x).ElementAt(built.Count / 2);
        var my = built.Select(t => t.Y).OrderBy(x => x).ElementAt(built.Count / 2);
        var mz = built.Select(t => t.Z).OrderBy(x => x).ElementAt(built.Count / 2);
        _output.WriteLine($"\nbase centre (cm) {mx:0},{my:0},{mz:0}");
        var c0 = PlacedSceneSpace.ToViewer(new PlacedVector(mx, my, mz));
        var q = new SceneLevelQuery("Facility",
            [(float)c0.X - 30, (float)c0.Y - 10, (float)c0.Z - 30], [(float)c0.X + 30, (float)c0.Y + 10, (float)c0.Z + 30], 40000,
            objects.Where(o => o.ActorPath is not null).Select(o => o.ActorPath!).ToList());
        sw.Restart();
        var slice = provider.DescribeLevel(q);
        _output.WriteLine($"first level query (answers at once): {slice?.Batches.Count} batches, pending {slice?.PendingMaps}, {sw.ElapsedMilliseconds} ms");
        provider.WaitForLevels(TimeSpan.FromMinutes(20));
        _output.WriteLine($"background indexing done after {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        slice = provider.DescribeLevel(q);
        _output.WriteLine($"level slice: {slice?.Batches.Count} batches, {slice?.Batches.Sum(b => b.Matrices.Length / 16)} instances of {slice?.TotalInBox} in box, {sw.ElapsedMilliseconds} ms; {slice?.Note}, pending {slice?.PendingMaps}");
        sw.Restart();
        slice = provider.DescribeLevel(q);
        _output.WriteLine($"level slice again: {sw.ElapsedMilliseconds} ms");
        foreach (var b in slice?.Batches.OrderByDescending(b => b.Matrices.Length).Take(25) ?? [])
            _output.WriteLine($"   {b.Name} x{b.Matrices.Length / 16} tex={b.Materials.FirstOrDefault()?.Texture}");
        sw.Restart();
        long levelBytes = 0;
        var levelFailed = 0;
        foreach (var b in slice?.Batches.Take(150) ?? [])
        {
            var a = provider.OpenAsset(b.Mesh);
            if (a is null) levelFailed++; else levelBytes += a.Data.Length;
        }
        _output.WriteLine($"baked 150 level meshes: {levelBytes / 1024} KB, {levelFailed} failed, {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Every level piece around a point, with its materials, for finding what blocks the view of a
    /// base (light-beam cones, roof trusses). Point in save centimetres via ABIOTIC_POINT="x y z".
    /// </summary>
    [Fact]
    public void Dump_LevelPiecesAroundPoint()
    {
        var assets = GameAssetProvider.CreateForLocalInstall();
        var point = (Environment.GetEnvironmentVariable("ABIOTIC_POINT") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
        if (assets is null || point.Length != 3) { _output.WriteLine("no game or no ABIOTIC_POINT"); return; }
        AbioticEditor.Core.Plugins.PluginHostEnvironment.GameAssets = () => assets;
        var provider = new AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider(new ProbeHost(Path.Combine(Path.GetTempPath(), "abiotic-models-probe"), _output));
        var c0 = PlacedSceneSpace.ToViewer(new PlacedVector(point[0], point[1], point[2]));
        var q = new SceneLevelQuery(Environment.GetEnvironmentVariable("ABIOTIC_REGION") ?? "Facility",
            [(float)c0.X - 20, (float)c0.Y - 3, (float)c0.Z - 20], [(float)c0.X + 20, (float)c0.Y + 15, (float)c0.Z + 20], 40000, []);
        provider.DescribeLevel(q);
        provider.WaitForLevels(TimeSpan.FromMinutes(20));
        var slice = provider.DescribeLevel(q)!;
        _output.WriteLine($"{slice.Batches.Count} batches; {slice.Note}");
        foreach (var b in slice.Batches.OrderByDescending(b => b.Matrices.Length))
        {
            var heights = Enumerable.Range(0, b.Matrices.Length / 16).Select(k => b.Matrices[(k * 16) + 13] - (float)c0.Y).ToList();
            var mats = string.Join(" ; ", b.Materials.Select(m => $"op={m.Opacity:0.00} em={m.Emissive} two={m.TwoSided} tex={m.Texture?.Split('/')[^1]}"));
            _output.WriteLine($"{b.Name} x{heights.Count} up {heights.Min():0.0}..{heights.Max():0.0} m | {mats}");
        }
    }

    /// <summary>A mesh's material chain with blend mode and shading as read, for checking translucency detection. Mesh name via ABIOTIC_MESH (search).</summary>
    [Fact]
    public void Dump_MaterialChainOfMesh()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var want = Environment.GetEnvironmentVariable("ABIOTIC_MESH");
        if (assets is null || want is null) return;
        foreach (var path in assets.AssetPaths.Where(p => p.Contains("/" + want + ".uasset", StringComparison.OrdinalIgnoreCase)).Take(2))
        {
            assets.UseFileProvider(p =>
            {
                var mesh = p.LoadPackage(path).GetExports().FirstOrDefault(e => e is CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh) as CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh;
                _output.WriteLine($"{path}: {mesh?.StaticMaterials?.Length} materials");
                foreach (var sm in mesh?.StaticMaterials ?? [])
                {
                    CUE4Parse.UE4.Assets.Exports.UObject? cur = sm.MaterialInterface?.Load();
                    for (var d = 0; cur is not null && d < 8; d++)
                    {
                        var props = string.Join(", ", cur.Properties.Where(x => x.Name.Text is "BlendMode" or "ShadingModel" or "BasePropertyOverrides" or "TwoSided" or "MaterialDomain").Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}"));
                        var extra = cur is CUE4Parse.UE4.Assets.Exports.Material.UMaterial um ? $" [UMaterial BlendMode field={um.BlendMode} shading={um.ShadingModel}]" : "";
                        _output.WriteLine($"   {cur.ExportType} {cur.GetPathName()} {props}{extra}");
                        cur = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("Parent")?.Load();
                    }
                    var r = AbioticEditor.Plugins.GameModels3D.MaterialResolver.Resolve(sm.MaterialInterface?.Load() as CUE4Parse.UE4.Assets.Exports.Material.UMaterialInterface);
                    _output.WriteLine($"   resolved opacity={r.Opacity} emissive={r.Emissive} masked={r.Masked}");
                }
                return 0;
            });
        }
    }

    /// <summary>
    /// For every region save in a world folder (ABIOTIC_WORLD_DIR), how much level the view finds
    /// around the middle of that region's saved objects: a region whose level comes back empty is
    /// placed wrong (or its map is not found).
    /// </summary>
    [Fact]
    public void Dump_LevelAroundEveryRegion()
    {
        var dir = Environment.GetEnvironmentVariable("ABIOTIC_WORLD_DIR");
        var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null || dir is null) { _output.WriteLine("no game or no ABIOTIC_WORLD_DIR"); return; }
        AbioticEditor.Core.Plugins.PluginHostEnvironment.GameAssets = () => assets;
        var provider = new AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider(new ProbeHost(Path.Combine(Path.GetTempPath(), "abiotic-models-probe"), _output));
        foreach (var file in Directory.GetFiles(dir, "WorldSave_*.sav").Order(StringComparer.OrdinalIgnoreCase))
        {
            var region = Path.GetFileNameWithoutExtension(file)["WorldSave_".Length..];
            if (region is "MetaData") continue;
            PlacedObjectCensusReport census;
            try { census = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(file)); }
            catch (Exception ex) { _output.WriteLine($"{region}: unreadable ({ex.GetType().Name})"); continue; }
            var points = census.Objects!.Where(o => o.Transform is not null).Select(o => o.Transform!.EffectiveTranslation).ToList();
            if (points.Count == 0) { _output.WriteLine($"{region}: no placed objects"); continue; }
            var mid = new PlacedVector(points.Select(p => p.X).Order().ElementAt(points.Count / 2), points.Select(p => p.Y).Order().ElementAt(points.Count / 2), points.Select(p => p.Z).Order().ElementAt(points.Count / 2));
            var c0 = PlacedSceneSpace.ToViewer(mid);
            var q = new SceneLevelQuery(region, [(float)c0.X - 30, (float)c0.Y - 15, (float)c0.Z - 30], [(float)c0.X + 30, (float)c0.Y + 15, (float)c0.Z + 30], 20000, []);
            var slice = provider.DescribeLevel(q);
            if (slice is { PendingMaps: > 0 }) { provider.WaitForLevels(TimeSpan.FromMinutes(20)); slice = provider.DescribeLevel(q); }
            _output.WriteLine($"{region,-28} objects {points.Count,5}  level pieces {slice?.TotalInBox ?? -1,6}  {slice?.Note ?? "no answer"}");
        }
    }

    /// <summary>How many spline meshes and landscape components each map has, to judge what the level view leaves out.</summary>
    [Fact]
    public void Dump_SplineAndLandscapeCounts()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var maps = assets.AssetPaths.Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) && p.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)).ToList();
        long splines = 0, landscape = 0, statics = 0;
        foreach (var map in maps)
        {
            var (sp, la, st, sample) = assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(map, out var pkg)) return (0, 0, 0, "");
                var exports = pkg.GetExports().ToList();
                var spl = exports.Where(e => e.ExportType == "SplineMeshComponent").ToList();
                var meshes = spl.Select(e => e.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("StaticMesh")?.ResolvedObject?.Name.Text ?? "?")
                    .GroupBy(n => n).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} x{g.Count()}");
                return (spl.Count, exports.Count(e => e.ExportType == "LandscapeComponent"), exports.Count(e => e.ExportType is "StaticMeshComponent" or "InstancedStaticMeshComponent" or "HierarchicalInstancedStaticMeshComponent"), string.Join(", ", meshes));
            });
            splines += sp; landscape += la; statics += st;
            if (sp > 20 || la > 0) _output.WriteLine($"{Path.GetFileNameWithoutExtension(map),-34} splines {sp,5}  landscape {la,4}  static {st,6}  {sample}");
        }
        _output.WriteLine($"total: {maps.Count} maps, spline meshes {splines}, landscape components {landscape}, static mesh components {statics}");
    }

    /// <summary>Where a map's spline meshes are (ABIOTIC_MAP, default Facility), grouped by their straight mesh, with a sample position each.</summary>
    [Fact]
    public void Dump_SplineMeshPlaces()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var map = Environment.GetEnvironmentVariable("ABIOTIC_MAP") ?? "Facility";
        var index = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.LevelIndex.Build(p, $"AbioticFactor/Content/Maps/{map}.umap"));
        var splines = index.Entries.Where(e => AbioticEditor.Plugins.GameModels3D.SplineBaker.IsKey(index.Meshes[e.Mesh])).ToList();
        _output.WriteLine($"{splines.Count} spline mesh entries");
        foreach (var g in splines.GroupBy(e => assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.SplineBaker.Load(p, index.Meshes[e.Mesh]) is { } c
                     && AbioticEditor.Plugins.GameModels3D.ClassModelResolver.TryMesh([c], out var m) ? m.Split('.')[^1] : "?")).OrderByDescending(g => g.Count()))
        {
            foreach (var e in g.Take(4))
            {
                var curve = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.SplineBaker.Load(p, index.Meshes[e.Mesh]) is { } c ? AbioticEditor.Plugins.GameModels3D.SplineBaker.Read(c) : null);
                var start = curve is null ? default : System.Numerics.Vector3.Transform(curve.StartPos, e.World);
                var end = curve is null ? default : System.Numerics.Vector3.Transform(curve.EndPos, e.World);
                _output.WriteLine($"  {g.Key,-28} component at {e.World.Translation.X:0},{e.World.Translation.Y:0},{e.World.Translation.Z:0}  curve {start.X:0},{start.Y:0},{start.Z:0} -> {end.X:0},{end.Y:0},{end.Z:0}  tangent {curve?.StartTangent}  axis {curve?.ForwardAxis}");
            }
        }
    }

    /// <summary>One spline mesh component's transform chain (ABIOTIC_KEY, a "#spline=" key).</summary>
    [Fact]
    public void Dump_SplineComponentChain()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var key = Environment.GetEnvironmentVariable("ABIOTIC_KEY");
        if (assets is null || key is null) return;
        assets.UseFileProvider(p =>
        {
            var c = AbioticEditor.Plugins.GameModels3D.SplineBaker.Load(p, key);
            _output.WriteLine($"key [{key}] loaded={c is not null}");
            for (var o = c; o is not null; o = o.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("AttachParent")?.Load())
            {
                var props = string.Join(", ", o.Properties.Where(x => x.Name.Text.StartsWith("Relative", StringComparison.Ordinal) || x.Name.Text.StartsWith("bAbsolute", StringComparison.Ordinal) || x.Name.Text.Contains("Mobility", StringComparison.Ordinal))
                    .Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}"));
                _output.WriteLine($"{o.ExportType} {o.Name} outer={o.Outer?.Name} [{props}] template={o.Template?.Name}");
                if (o.Template?.Load() is { } t) _output.WriteLine($"   template {t.ExportType} {t.Name} [{string.Join(", ", t.Properties.Where(x => x.Name.Text.StartsWith("Relative", StringComparison.Ordinal) || x.Name.Text.StartsWith("bAbsolute", StringComparison.Ordinal)).Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}"))}]");
            }
            var actor = c?.Outer;
            _output.WriteLine($"actor {actor?.Name} class {actor?.GetType().Name}");
            return 0;
        });
    }

    /// <summary>A map's landscape materials: parameters, referenced textures and the weightmap layers its components use (ABIOTIC_MAP).</summary>
    [Fact]
    public void Dump_LandscapeMaterials()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var map = Environment.GetEnvironmentVariable("ABIOTIC_MAP") ?? "Facility_Dam";
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage($"AbioticFactor/Content/Maps/{map}.umap");
            var materials = new HashSet<string>();
            var layers = new Dictionary<string, int>();
            foreach (var proxy in pkg.GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Actor.ALandscapeProxy>())
            {
                if (proxy.LandscapeMaterial is { IsNull: false } lm) materials.Add(lm.ResolvedObject!.GetPathName());
                foreach (var ci in proxy.LandscapeComponents)
                {
                    var c = ci.Load<CUE4Parse.UE4.Assets.Exports.Component.Landscape.ULandscapeComponent>();
                    if (c is null) continue;
                    if (c.OverrideMaterial is { IsNull: false } om) materials.Add(om.ResolvedObject!.GetPathName());
                    foreach (var a in c.GetWeightmapLayerAllocations()) { var n = a.GetLayerName(); layers[n] = layers.GetValueOrDefault(n) + 1; }
                }
            }
            _output.WriteLine($"layers: {string.Join(", ", layers.Select(kv => $"{kv.Key} x{kv.Value}"))}");
            foreach (var m in materials)
            {
                _output.WriteLine($"material {m}");
                for (CUE4Parse.UE4.Assets.Exports.UObject? cur = p.LoadPackageObject(m); cur is not null; cur = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("Parent")?.Load())
                {
                    _output.WriteLine($"  {cur.ExportType} {cur.GetPathName()}");
                    foreach (var t in cur.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("TextureParameterValues", []))
                        _output.WriteLine($"     tex {t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ParameterInfo")?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("Name").Text} = {t.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("ParameterValue")?.ResolvedObject?.Name}");
                    foreach (var v in cur.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("ScalarParameterValues", []))
                        _output.WriteLine($"     scalar {v.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ParameterInfo")?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("Name").Text} = {v.GetOrDefault<float>("ParameterValue")}");
                    if (cur is CUE4Parse.UE4.Assets.Exports.Material.UMaterial um)
                        _output.WriteLine($"     referenced: {string.Join(", ", um.ReferencedTextures.Where(x => x is not null).Select(x => x!.Name))}");
                }
            }
            return 0;
        });
    }

    /// <summary>The terrain master material's cooked properties (looking for its landscape layer names and texture slots).</summary>
    [Fact]
    public void Dump_TerrainMaster()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage("AbioticFactor/Content/Textures/M_AbioticTerrain_Master.uasset");
            _output.WriteLine("names: " + string.Join(" ", pkg.NameMap.Select(n => n.Name)));
            var m = p.LoadPackageObject("/Game/Textures/M_AbioticTerrain_Master.M_AbioticTerrain_Master");
            if (m is CUE4Parse.UE4.Assets.Exports.Material.UMaterial um)
            {
                var cached = Newtonsoft.Json.JsonConvert.SerializeObject(um.CachedExpressionData);
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "terrain-master-cached.json"), cached); _output.WriteLine("cached written " + cached.Length);
            }
            foreach (var prop in m.Properties)
            {
                var text = Newtonsoft.Json.JsonConvert.SerializeObject(prop.Tag?.GenericValue);
                _output.WriteLine($"{prop.Name.Text} = {(text.Length > 1500 ? text[..1500] + "..." : text)}");
            }
            return 0;
        });
    }

    /// <summary>Every map's landscape layers (layer info object, its LayerName) and the material's texture slots, to infer which layer drives which slot.</summary>
    [Fact]
    public void Dump_LandscapeLayersEverywhere()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var maps = assets.AssetPaths.Where(x => x.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) && x.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var map in maps)
        {
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(map, out var pkg)) return 0;
                var proxies = pkg.GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Actor.ALandscapeProxy>().ToList();
                if (proxies.Count == 0) return 0;
                var layers = new Dictionary<string, int>();
                var mats = new HashSet<string>();
                foreach (var proxy in proxies)
                {
                    if (proxy.LandscapeMaterial is { IsNull: false } lm) mats.Add(lm.ResolvedObject!.Name.Text);
                    foreach (var ci in proxy.LandscapeComponents)
                    {
                        var c = ci.Load<CUE4Parse.UE4.Assets.Exports.Component.Landscape.ULandscapeComponent>();
                        if (c is null) continue;
                        if (c.OverrideMaterial is { IsNull: false } om) mats.Add(om.ResolvedObject!.Name.Text);
                        foreach (var a in c.GetWeightmapLayerAllocations())
                        {
                            var info = a.LayerInfo?.Load();
                            var name = $"{a.GetLayerName()}({info?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("LayerName").Text})";
                            layers[name] = layers.GetValueOrDefault(name) + 1;
                        }
                    }
                }
                var slots = string.Join(" | ", mats.Select(m =>
                {
                    var mi = p.LoadPackageObject($"/Game/Textures/Landscape/{m}.{m}");
                    return m + ": " + string.Join(", ", mi.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("TextureParameterValues", [])
                        .Select(t => $"{t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ParameterInfo")?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("Name").Text}={t.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("ParameterValue")?.ResolvedObject?.Name}"));
                }));
                _output.WriteLine($"{Path.GetFileNameWithoutExtension(map),-26} layers {string.Join(", ", layers.Select(kv => $"{kv.Key} x{kv.Value}"))}  ||  {slots}");
                return 0;
            });
        }
    }

    /// <summary>The materials on a map's pieces of one mesh (ABIOTIC_MAP, ABIOTIC_MESH_NAME), with each material chain's blend mode, parameters and the functions its master uses.</summary>
    [Fact]
    public void Dump_MaterialsOfMeshInMap()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var map = Environment.GetEnvironmentVariable("ABIOTIC_MAP") ?? "Facility_Dam";
        var meshName = Environment.GetEnvironmentVariable("ABIOTIC_MESH_NAME") ?? "Plane";
        var index = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.LevelIndex.Build(p, $"AbioticFactor/Content/Maps/{map}.umap"));
        var seen = new HashSet<string>();
        foreach (var e in index.Entries.Where(e => index.Meshes[e.Mesh].EndsWith("." + meshName, StringComparison.Ordinal)))
        {
            var scale = new System.Numerics.Vector3(e.World.M11, e.World.M12, e.World.M13).Length();
            foreach (var m in index.OverrideSets[e.Overrides].Where(x => x is not null))
            {
                _output.WriteLine($"{index.Actors[e.Actor]} scale {scale:0.0} material {m}");
                if (!seen.Add(m!)) continue;
                assets.UseFileProvider(p =>
                {
                    for (CUE4Parse.UE4.Assets.Exports.UObject? cur = p.LoadPackageObject(m!); cur is not null; cur = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("Parent")?.Load())
                    {
                        var props = string.Join(", ", cur.Properties.Where(x => x.Name.Text is "BlendMode" or "ShadingModel" or "BasePropertyOverrides").Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}"));
                        _output.WriteLine($"   {cur.ExportType} {cur.GetPathName()} {props}");
                        foreach (var t in cur.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("ScalarParameterValues", []))
                            _output.WriteLine($"      scalar {t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ParameterInfo")?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("Name").Text} = {t.GetOrDefault<float>("ParameterValue")}");
                        if (cur is CUE4Parse.UE4.Assets.Exports.Material.UMaterial um)
                        {
                            var json = Newtonsoft.Json.JsonConvert.SerializeObject(um.CachedExpressionData);
                            var functions = System.Text.RegularExpressions.Regex.Matches(json, "MaterialFunction'([^']+)'").Select(x => x.Groups[1].Value).Distinct();
                            _output.WriteLine($"      functions: {string.Join(", ", functions)}");
                            var names = p.LoadPackage(um.GetPathName().Split('.')[0]).NameMap.Select(n => n.Name ?? "").Where(n => !n.StartsWith('/'));
                            _output.WriteLine($"      names: {string.Join(" ", names)}");
                        }
                    }
                    return 0;
                });
            }
        }
    }

    /// <summary>A map's decal components (ABIOTIC_MAP): size, material and that material's texture parameters and blend mode.</summary>
    [Fact]
    public void Dump_Decals()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var map = Environment.GetEnvironmentVariable("ABIOTIC_MAP") ?? "Facility_Office1";
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage($"AbioticFactor/Content/Maps/{map}.umap");
            var decals = pkg.GetExports().Where(e => e.ExportType.Contains("DecalComponent", StringComparison.Ordinal)).ToList();
            _output.WriteLine($"{decals.Count} decal components; types {string.Join(",", decals.Select(d => d.ExportType).Distinct())}");
            foreach (var g in decals.GroupBy(d => AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(d, "DecalMaterial", null)?.ResolvedObject?.GetPathName() ?? "(none)").OrderByDescending(g => g.Count()).Take(14))
            {
                var d = g.First();
                var size = AbioticEditor.Plugins.GameModels3D.Props.Get(d, "DecalSize", new CUE4Parse.UE4.Objects.Core.Math.FVector(128, 256, 256));
                _output.WriteLine($"x{g.Count(),4} {g.Key} size {size}");
                if (g.Key == "(none)") continue;
                for (CUE4Parse.UE4.Assets.Exports.UObject? cur = p.LoadPackageObject(g.Key); cur is not null; cur = cur.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("Parent")?.Load())
                {
                    var props = string.Join(", ", cur.Properties.Where(x => x.Name.Text is "BlendMode" or "MaterialDomain" or "DecalBlendMode").Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}"));
                    var tex = string.Join(", ", cur.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("TextureParameterValues", [])
                        .Select(t => $"{t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ParameterInfo")?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FName>("Name").Text}={t.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("ParameterValue")?.ResolvedObject?.Name}"));
                    var refs = cur is CUE4Parse.UE4.Assets.Exports.Material.UMaterial um ? " refs " + string.Join(",", um.ReferencedTextures.Where(x => x is not null).Select(x => x!.Name)) : "";
                    _output.WriteLine($"      {cur.ExportType} {cur.Name} {props} {tex}{refs}");
                }
            }
            return 0;
        });
    }

    /// <summary>Skeletal meshes placed in levels: how many, which meshes, and whether each names an animation to play (AnimationData.AnimToPlay) or uses an anim blueprint.</summary>
    [Fact]
    public void Dump_LevelSkeletalMeshes()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var maps = assets.AssetPaths.Where(x => x.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) && x.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)).ToList();
        var total = 0; var withAnim = 0; var withAbp = 0;
        var meshes = new Dictionary<string, int>();
        var anims = new Dictionary<string, int>();
        foreach (var map in maps)
        {
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(map, out var pkg)) return 0;
                foreach (var c in pkg.GetExports().Where(e => e.ExportType.Contains("SkeletalMeshComponent", StringComparison.Ordinal)))
                {
                    total++;
                    var mesh = AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkeletalMesh", null)?.ResolvedObject?.Name.Text
                               ?? AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkinnedAsset", null)?.ResolvedObject?.Name.Text ?? "?";
                    meshes[mesh] = meshes.GetValueOrDefault(mesh) + 1;
                    var data = AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Assets.Objects.FStructFallback?>(c, "AnimationData", null);
                    var anim = data?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>("AnimToPlay")?.ResolvedObject?.Name.Text;
                    if (anim is not null) { withAnim++; anims[anim] = anims.GetValueOrDefault(anim) + 1; }
                    if (AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "AnimClass", null) is { IsNull: false }) withAbp++;
                }
                return 0;
            });
        }
        _output.WriteLine($"{total} skeletal mesh components, {withAnim} with AnimToPlay, {withAbp} with an anim blueprint");
        foreach (var (m, n) in meshes.OrderByDescending(kv => kv.Value).Take(15)) _output.WriteLine($"  mesh {m} x{n}");
        foreach (var (a, n) in anims.OrderByDescending(kv => kv.Value).Take(15)) _output.WriteLine($"  anim {a} x{n}");
    }

    /// <summary>Whether a level pose animation (ABIOTIC_ANIM, a name) decodes: its skeleton, track count and frame-0 transforms of a few bones.</summary>
    [Fact]
    public void Dump_DecodeAnimation()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var name = Environment.GetEnvironmentVariable("ABIOTIC_ANIM") ?? "Pose_Scientist_Dead_FlatBackrt";
        if (assets is null) return;
        var path = assets.AssetPaths.FirstOrDefault(x => x.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"path {path}");
        if (path is null) return;
        assets.UseFileProvider(p =>
        {
            var anim = p.LoadPackage(path).GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Animation.UAnimSequence>().First();
            var codec = anim.BoneCompressionSettings?.GetPathName();
            _output.WriteLine($"codec settings {codec}; frames {anim.NumFrames}");
            try
            {
                var set = CUE4Parse_Conversion.Animations.AnimConverter.ConvertAnims(anim);
                var seq = set.Sequences[0];
                _output.WriteLine($"skeleton {set.Skeleton.Name} bones {set.Skeleton.ReferenceSkeleton.FinalRefBoneInfo.Length}; tracks {seq.Tracks.Count}, frames {seq.NumFrames}");
                for (var b = 0; b < Math.Min(5, seq.Tracks.Count); b++)
                {
                    var t = seq.Tracks[b];
                    _output.WriteLine($"  bone {set.Skeleton.ReferenceSkeleton.FinalRefBoneInfo[b].Name} quats {t.KeyQuat.Length} pos {t.KeyPos.Length} first {(t.KeyQuat.Length > 0 ? t.KeyQuat[0].ToString() : "-")}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine("decode failed: " + ex.GetType().Name + " " + ex.Message);
            }
            return 0;
        });
    }

    /// <summary>Why a posed mesh key (ABIOTIC_KEY) does or does not bake.</summary>
    [Fact]
    public void Debug_PoseKey()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var key = Environment.GetEnvironmentVariable("ABIOTIC_KEY");
        if (assets is null || key is null) return;
        assets.UseFileProvider(p =>
        {
            var c = AbioticEditor.Plugins.GameModels3D.PoseBaker.Load(p, key);
            _output.WriteLine($"component {c?.ExportType} {c?.Name}");
            if (c is null) return 0;
            var hasMesh = AbioticEditor.Plugins.GameModels3D.ClassModelResolver.TryMesh([c], out var meshPath);
            _output.WriteLine($"mesh {hasMesh} {meshPath}");
            var meshIndex = AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkeletalMesh", null)
                            ?? AbioticEditor.Plugins.GameModels3D.Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkinnedAsset", null);
            var loaded = meshIndex?.Load();
            _output.WriteLine($"mesh object {loaded?.GetType().Name} {loaded?.Name}");
            var pose = AbioticEditor.Plugins.GameModels3D.PoseBaker.PoseOf(c);
            _output.WriteLine($"pose {pose?.Anim.Name} t={pose?.Time}");
            try
            {
                var bytes = AbioticEditor.Plugins.GameModels3D.PoseBaker.Bake(c, 1);
                _output.WriteLine($"baked {bytes?.Length}");
            }
            catch (Exception ex) { _output.WriteLine("bake threw " + ex.GetType().Name + ": " + ex.Message + " | " + ex.StackTrace?[..Math.Min(1500, ex.StackTrace.Length)]); }
            return 0;
        });
    }
}
