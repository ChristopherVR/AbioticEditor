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
}
