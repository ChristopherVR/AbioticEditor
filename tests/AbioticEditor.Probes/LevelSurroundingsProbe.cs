using System.IO;
using System.Linq;
using System.Numerics;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// What surrounds a base in a level, by any component type: which geometry kinds (static meshes,
/// landscape, brushes, instanced/foliage, geometry collections) the 3D view would need to draw the
/// floor and walls a base stands on. Output-only.
/// </summary>
public class LevelSurroundingsProbe
{
    private readonly ITestOutputHelper _output;
    public LevelSurroundingsProbe(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Which streamed maps the level's streaming volumes load (ordinary areas) versus the ones only
    /// game script loads (vignettes and other set pieces that share the same space).
    /// </summary>
    [Fact]
    public void Dump_StreamingVolumeTargets()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) { _output.WriteLine("no game"); return; }
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage("AbioticFactor/Content/Maps/Facility.umap");
            var byVolume = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in pkg.GetExports().Where(e => e.ExportType.Contains("LevelStreamingVolume", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var n in e.GetOrDefault<FName[]>("StreamingLevelNames", [])) byVolume.Add(n.Text);
            }
            foreach (var e in pkg.GetExports().Where(e => e.ExportType == "AbioticLevelStreamingVolume").Take(3))
            {
                _output.WriteLine($"{e.Name}:");
                foreach (var prop in e.Properties) _output.WriteLine($"   {prop.Name.Text} = {prop.Tag?.GenericValue}");
            }
            var world = pkg.GetExports().OfType<UWorld>().First();
            _output.WriteLine($"volume targets ({byVolume.Count}): {string.Join(", ", byVolume)}");
            foreach (var s in world.StreamingLevels ?? [])
            {
                var ls = s.Load();
                var asset = ls?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FSoftObjectPath>("WorldAsset").AssetPathName.Text ?? "";
                var name = asset[(asset.LastIndexOf('.') + 1)..];
                var pkgName = ls?.GetOrDefault<FName>("PackageNameToLoad").Text;
                if (ls is not null && ls.TryGetValue(out FTransform lt, "LevelTransform")) _output.WriteLine($"      LevelTransform t={lt.Translation} r={lt.Rotation.Rotator()} s={lt.Scale3D}");
                _output.WriteLine($"  {name,-32} {ls?.ExportType,-28} volume={byVolume.Any(v => v.EndsWith(name, StringComparison.OrdinalIgnoreCase))} initLoaded={ls?.GetOrDefault("bInitiallyLoaded", false)} initVisible={ls?.GetOrDefault("bInitiallyVisible", false)} alwaysLoaded={ls?.GetOrDefault("bShouldBeLoaded", false)}");
            }
            return 0;
        });
    }

    /// <summary>How much compiled BSP (brush) geometry each map near the base holds, and how much of it is near the base.</summary>
    [Fact]
    public void Dump_BspNearBase()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var base0 = new Vector3(-46432f, 8620f, 1341f);
        foreach (var map in new[] { "Facility", "Facility_Dam", "Facility_Dam_Waterfall", "Facility_Office1" })
        {
            assets.UseFileProvider(p =>
            {
                var pkg = p.LoadPackage($"AbioticFactor/Content/Maps/{map}.umap");
                var level = pkg.GetExports().OfType<UWorld>().First().PersistentLevel.Load<ULevel>()!;
                var model = level.Model.Load<UModel>();
                if (model is null) { _output.WriteLine($"{map}: no level model"); return 0; }
                int polys = 0, near = 0, tris = 0;
                var mats = new Dictionary<string, int>();
                foreach (var node in model.Nodes)
                {
                    if (node.NumVertices < 3) continue;
                    polys++;
                    tris += node.NumVertices - 2;
                    var v0 = model.Points[model.Verts[node.iVertPool].pVertex];
                    if (Math.Abs(v0.X - base0.X) < 3000 && Math.Abs(v0.Y - base0.Y) < 3000 && Math.Abs(v0.Z - base0.Z) < 1500)
                    {
                        near++;
                        var m = model.Surfs[node.iSurf].Material.Name;
                        mats[m] = mats.GetValueOrDefault(m) + 1;
                    }
                }
                _output.WriteLine($"{map}: nodes {model.Nodes.Length}, polygons {polys} ({tris} triangles), points {model.Points.Length}, surfs {model.Surfs.Length}, model components {level.ModelComponents.Length}; near base {near}: {string.Join(", ", mats.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} x{k.Value}"))}");
                return 0;
            });
        }
    }

    [Theory]
    [InlineData(3873.9f, 33836.0f, 1608.1f)]
    public void Dump_AroundPoint(float x, float y, float z)
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var pt = new Vector3(x, y, z);
        var vols = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider.StreamingVolumes(p, "AbioticFactor/Content/Maps/Facility.umap"));
        var maps = vols.Where(v => pt.X >= v.Box.Min.X && pt.Y >= v.Box.Min.Y && pt.Z >= v.Box.Min.Z && pt.X <= v.Box.Max.X && pt.Y <= v.Box.Max.Y && pt.Z <= v.Box.Max.Z).Select(v => v.Map).Distinct().ToList();
        _output.WriteLine($"volumes holding the point: {string.Join(", ", maps)}");
        var placements = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider.StreamedPlacements(p, "AbioticFactor/Content/Maps/Facility.umap"))
            .ToDictionary(x => x.Map, x => x.Placement, StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps.Prepend("Facility"))
        {
            var place = placements.TryGetValue(map, out var pl) ? pl : Matrix4x4.Identity;
            var index = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.LevelIndex.Build(p, $"AbioticFactor/Content/Maps/{map}.umap"));
            AbioticEditor.Plugins.GameModels3D.LevelIndex.ApplyBounds(index, mesh =>
                assets.UseFileProvider(p => p.TryLoadPackageObject(mesh, out var o) ? AbioticEditor.Plugins.GameModels3D.MeshBaker.Describe(o) : null));
            var nearest = index.Entries
                .Select(e => e with { Centre = Vector3.Transform(e.Centre, place), World = e.World * place })
                .Select(e => (e, d: MathF.Max(0, Vector3.Distance(e.Centre, pt) - e.Radius)))
                .OrderBy(t => t.d).Take(6).ToList();
            _output.WriteLine($"{map}: {index.Entries.Count} entries; nearest by bounds:");
            var below = index.Entries.Select(e => e with { World = e.World * place, Centre = Vector3.Transform(e.Centre, place) })
                .Where(e => MathF.Abs(e.Centre.X - pt.X) < e.Radius && MathF.Abs(e.Centre.Y - pt.Y) < e.Radius && e.Centre.Z < pt.Z && pt.Z - e.Centre.Z < 600)
                .Select(e => index.Meshes[e.Mesh].Split('.')[^1]).Distinct().Take(8);
            _output.WriteLine($"   under the point: {string.Join(", ", below)}");
            foreach (var (e, d) in nearest)
                _output.WriteLine($"   {d / 100,6:0.0} m  {index.Meshes[e.Mesh].Split('.')[^1],-36} actor {index.Actors[e.Actor],-30} centre {e.Centre / 100} r {e.Radius / 100:0.0} m");
        }
    }

    [Fact]
    public void Dump_VolumesHoldingBase()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var base0 = new Vector3(-46432f, 8620f, 1341f);
        var vols = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.PakSceneModelProvider.StreamingVolumes(p, "AbioticFactor/Content/Maps/Facility.umap"));
        _output.WriteLine($"volumes: {vols.Count}");
        foreach (var (map, (min, max)) in vols.Where(v => base0.X >= v.Box.Min.X && base0.Y >= v.Box.Min.Y && base0.Z >= v.Box.Min.Z && base0.X <= v.Box.Max.X && base0.Y <= v.Box.Max.Y && base0.Z <= v.Box.Max.Z))
            _output.WriteLine($"  holds base: {map,-28} size {(max - min) / 100} m");
        foreach (var (map, (min, max)) in vols.OrderByDescending(v => (v.Box.Max - v.Box.Min).Length()).Take(8))
            _output.WriteLine($"  biggest: {map,-28} size {(max - min) / 100} m centre {(max + min) / 200}");
    }

    [Theory]
    [InlineData("AbioticFactor/Content/Maps/Facility.umap", -46432f, 8620f, 1341f, 4000f)]
    public void Dump_ComponentsNear(string map, float x, float y, float z, float radius)
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) { _output.WriteLine("no game"); return; }
        var centre = new Vector3(x, y, z);
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage(map);
            var world = pkg.GetExports().OfType<UWorld>().First();
            var level = world.PersistentLevel.Load<ULevel>()!;
            var types = new Dictionary<string, int>();
            var near = new List<string>();
            foreach (var a in level.Actors)
            {
                if (a is not { IsNull: false } || !a.TryLoad(out UObject? actor) || actor is null) continue;
                var root = actor.GetOrDefault<FPackageIndex?>("RootComponent")?.Load();
                var loc = root?.GetOrDefault("RelativeLocation", FVector.ZeroVector) ?? FVector.ZeroVector;
                var d = Vector3.Distance(new Vector3(loc.X, loc.Y, loc.Z), centre);
                types[actor.ExportType] = types.GetValueOrDefault(actor.ExportType) + 1;
                if (d < radius) near.Add($"{d,6:0} {actor.ExportType,-36} {actor.Name} root={root?.ExportType}");
            }
            _output.WriteLine($"actors within {radius / 100} m: {near.Count}");
            foreach (var n in near.OrderBy(s => s).Take(80)) _output.WriteLine("  " + n);
            _output.WriteLine("\nactor types in level:");
            foreach (var (t, c) in types.OrderByDescending(kv => kv.Value).Take(40)) _output.WriteLine($"  {t,-44} {c}");

            // Landscape proxies: where are they?
            foreach (var e in pkg.GetExports().Where(e => e.ExportType.Contains("Landscape", StringComparison.OrdinalIgnoreCase) && !e.ExportType.Contains("Component")).Take(10))
            {
                var r = e.GetOrDefault<FPackageIndex?>("RootComponent")?.Load();
                _output.WriteLine($"landscape actor {e.ExportType} {e.Name} at {r?.GetOrDefault("RelativeLocation", FVector.ZeroVector)} scale {r?.GetOrDefault("RelativeScale3D", FVector.OneVector)}");
            }
            var lcs = pkg.GetExports().Where(e => e.ExportType == "LandscapeComponent").ToList();
            var sectionBase = lcs.Select(c => c.GetOrDefault<FIntPoint>("SectionBaseX")).Take(1).ToList();
            _output.WriteLine($"landscape components: {lcs.Count}, sample SectionBaseX/Y: {string.Join(";", lcs.Take(5).Select(c => $"{c.GetOrDefault<int>("SectionBaseX")},{c.GetOrDefault<int>("SectionBaseY")} size={c.GetOrDefault<int>("ComponentSizeQuads")} loc={c.GetOrDefault("RelativeLocation", FVector.ZeroVector)}"))}");
            var models = pkg.GetExports().Where(e => e.ExportType == "Model").ToList();
            _output.WriteLine($"BSP models: {models.Count}");

            // Landscape components covering the point (component origin is relative to the proxy).
            foreach (var lc in lcs)
            {
                var proxy = lc.Outer?.Load();
                var proxyRoot = proxy?.GetOrDefault<FPackageIndex?>("RootComponent")?.Load();
                var pl = proxyRoot?.GetOrDefault("RelativeLocation", FVector.ZeroVector) ?? FVector.ZeroVector;
                var ps = proxyRoot?.GetOrDefault("RelativeScale3D", FVector.OneVector) ?? FVector.OneVector;
                var rel = lc.GetOrDefault("RelativeLocation", FVector.ZeroVector);
                var size = lc.GetOrDefault<int>("ComponentSizeQuads");
                var x0 = pl.X + (rel.X * ps.X); var y0 = pl.Y + (rel.Y * ps.Y);
                var x1 = x0 + (size * ps.X); var y1 = y0 + (size * ps.Y);
                if (x >= Math.Min(x0, x1) && x <= Math.Max(x0, x1) && y >= Math.Min(y0, y1) && y <= Math.Max(y0, y1))
                    _output.WriteLine($"landscape component {lc.Name} of {proxy?.Name} covers the point: x {x0:0}..{x1:0} y {y0:0}..{y1:0} proxy z {pl.Z:0}");
            }
            return 0;
        });

        // Large meshes whose real bounds hold the point even though their origin is far away.
        var index = assets.UseFileProvider(p => AbioticEditor.Plugins.GameModels3D.LevelIndex.Build(p, map));
        var hits = 0;
        foreach (var e in index.Entries.Where(e => Vector3.Distance(e.World.Translation, centre) < 30000))
        {
            var meshPath = index.Meshes[e.Mesh];
            var info = assets.UseFileProvider(p => p.TryLoadPackageObject(meshPath, out var o) ? AbioticEditor.Plugins.GameModels3D.MeshBaker.Describe(o) : null);
            if (info is null) continue;
            var world = e.World;
            if (!Matrix4x4.Invert(world, out var inv)) continue;
            var local = Vector3.Transform(centre, inv);
            var grow = new Vector3(300f);
            if (local.X >= info.BoundsMin.X - grow.X && local.Y >= info.BoundsMin.Y - grow.Y && local.Z >= info.BoundsMin.Z - grow.Z
                && local.X <= info.BoundsMax.X + grow.X && local.Y <= info.BoundsMax.Y + grow.Y && local.Z <= info.BoundsMax.Z + grow.Z)
            {
                hits++;
                var size = (info.BoundsMax - info.BoundsMin) * new Vector3(new Vector3(world.M11, world.M12, world.M13).Length(), new Vector3(world.M21, world.M22, world.M23).Length(), new Vector3(world.M31, world.M32, world.M33).Length());
                _output.WriteLine($"  contains point: {meshPath.Split('.')[^1],-40} {index.Actors[e.Actor],-28} origin dist {Vector3.Distance(e.World.Translation, centre) / 100:0} m, size {size.X / 100:0}x{size.Y / 100:0}x{size.Z / 100:0} m");
            }
        }
        _output.WriteLine($"meshes whose bounds hold the point: {hits}");
    }
}
