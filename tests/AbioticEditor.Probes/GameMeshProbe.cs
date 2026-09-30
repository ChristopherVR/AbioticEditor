using System.IO;
using System.Linq;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Dto;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for the optional 3D game-model view: can a saved placed object's class be turned
/// into the meshes the game draws for it, and how much static level geometry surrounds a base?
/// Output-only (see <c>docs/reference/research/research-game-models-3d.md</c>).
/// </summary>
public class GameMeshProbe
{
    private readonly ITestOutputHelper _output;
    public GameMeshProbe(ITestOutputHelper output) => _output = output;

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    private static string? FacilitySave()
    {
        foreach (var root in new[] { Fixtures.ServerWorldsDir, Fixtures.ServerWorldsDir is null ? null : Path.Combine(Fixtures.ServerWorldsDir, "Cascade") })
        {
            if (root is null) continue;
            var p = Path.Combine(root, "WorldSave_Facility.sav");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void Dump_PlacedClassMeshes()
    {
        using var provider = CreateProvider();
        var save = FacilitySave();
        if (provider is null || save is null) { _output.WriteLine("paks or fixture missing"); return; }

        var census = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(save));
        var classes = census.Objects!.Where(o => o.DeployedByPlayer == true && o.ClassPath is not null)
            .GroupBy(o => o.ClassPath!).OrderByDescending(g => g.Count()).Take(25).ToList();
        _output.WriteLine($"player-built objects: {census.Objects!.Count(o => o.DeployedByPlayer == true)}, classes: {classes.Count}");

        var converted = 0;
        foreach (var g in classes)
        {
            _output.WriteLine($"\n== {g.Key} x{g.Count()}");
            if (!provider.TryLoadPackageObject(g.Key, out var cls) || cls is not UClass klass)
            {
                _output.WriteLine("   (class did not load)");
                continue;
            }
            // Walk the class and its blueprint parents.
            var depth = 0;
            for (UStruct? c = klass; c is not null && depth < 8; c = c.SuperStruct?.Load<UStruct>(), depth++)
            {
                if (c is not CUE4Parse.UE4.Objects.Engine.UBlueprintGeneratedClass bpgc) { _output.WriteLine($"   [{depth}] {c.Name} (native)"); break; }
                _output.WriteLine($"   [{depth}] {bpgc.Name}");
                var scsIndex = bpgc.GetOrDefault<FPackageIndex?>("SimpleConstructionScript");
                if (scsIndex?.Load<USimpleConstructionScript>() is { } scs)
                {
                    foreach (var nodeIdx in scs.AllNodes)
                    {
                        if (nodeIdx?.Load<USCS_Node>() is not { } node) continue;
                        var tmpl = node.ComponentTemplate?.Load();
                        if (tmpl is null) continue;
                        var mesh = tmpl.GetOrDefault<FPackageIndex?>("StaticMesh") ?? tmpl.GetOrDefault<FPackageIndex?>("SkeletalMesh") ?? tmpl.GetOrDefault<FPackageIndex?>("SkinnedAsset");
                        var loc = tmpl.GetOrDefault<FVector>("RelativeLocation");
                        var parent = node.GetOrDefault<FName>("ParentComponentOrVariableName");
                        var vis = tmpl.GetOrDefault("bVisible", true) && !tmpl.GetOrDefault("bHiddenInGame", false);
                        _output.WriteLine($"      {tmpl.ExportType,-32} {node.InternalVariableName.Text,-28} parent={parent.Text,-20} mesh={mesh?.Name ?? "-"} loc={loc} vis={vis}");
                        if (converted < 4 && mesh?.Load() is UStaticMesh sm)
                        {
                            converted++;
                            TryConvert(sm, tmpl);
                        }
                    }
                }
                var ich = bpgc.GetOrDefault<FPackageIndex?>("InheritableComponentHandler")?.Load();
                if (ich is not null)
                {
                    var records = ich.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("Records", []);
                    foreach (var r in records)
                    {
                        var t = r.GetOrDefault<FPackageIndex?>("ComponentTemplate")?.Load();
                        var m = t?.GetOrDefault<FPackageIndex?>("StaticMesh");
                        _output.WriteLine($"      override {t?.Name} mesh={m?.Name ?? "-"}");
                    }
                }
            }
        }
    }

    private void TryConvert(UStaticMesh sm, UObject component)
    {
        try
        {
            using var dto = new StaticMeshDto(sm);
            var lod = dto.LODs[0];
            _output.WriteLine($"         -> LOD0 verts={lod.Vertices.Length} tris={lod.Indices.Length / 3} sections={lod.Sections.Length} lods={dto.LODs.Count} bounds={dto.Bounds}");
            var overrides = component.GetOrDefault<FPackageIndex?[]>("OverrideMaterials", []);
            for (var i = 0; i < dto.Materials.Length; i++)
            {
                var matIdx = i < overrides.Length && overrides[i] is { IsNull: false } o ? o : dto.Materials[i].Material;
                var mat = matIdx?.Load<UMaterialInterface>();
                _output.WriteLine($"            mat[{i}] {mat?.GetPathName()} ({mat?.ExportType})");
                if (mat is null) continue;
                var p = new CUE4Parse.UE4.Assets.Exports.Material.CMaterialParams2();
                mat.GetParams(p, EMaterialDepth.AllLayers);
                foreach (var (k, v) in p.Textures.Take(6)) _output.WriteLine($"               tex {k} = {v.GetPathName()}");
                foreach (var (k, v) in p.Colors.Take(4)) _output.WriteLine($"               col {k} = {v}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"         -> convert failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void Dump_FacilityLevelGeometry()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("paks missing"); return; }
        var maps = provider.Files.Keys.Where(k => k.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) && k.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
        _output.WriteLine($"umaps under /Maps/: {maps.Count}");
        foreach (var m in maps.Where(m => m.Contains("Facility", StringComparison.OrdinalIgnoreCase)).Take(400)) _output.WriteLine("  " + m);

        foreach (var name in new[] { "AbioticFactor/Content/Maps/Facility.umap" })
        {
            if (!provider.TryLoadPackage(name, out var pkg)) { _output.WriteLine($"{name}: not loadable"); continue; }
            var exports = pkg.GetExports().ToList();
            var byType = exports.GroupBy(e => e.ExportType).OrderByDescending(g => g.Count()).Take(30);
            _output.WriteLine($"\n{name}: {exports.Count} exports");
            foreach (var t in byType) _output.WriteLine($"  {t.Key,-40} {t.Count()}");
            var world = exports.OfType<CUE4Parse.UE4.Objects.Engine.UWorld>().FirstOrDefault();
            var levels = world?.StreamingLevels ?? [];
            _output.WriteLine($"  streaming levels: {levels.Length}");
            foreach (var l in levels.Take(10))
            {
                var sl = l.Load();
                _output.WriteLine($"    {sl?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FSoftObjectPath>("WorldAsset").AssetPathName.Text}");
            }
        }
    }
}

public class GameMeshResolveProbe
{
    private readonly ITestOutputHelper _output;
    public GameMeshResolveProbe(ITestOutputHelper output) => _output = output;

    private sealed record Comp(string Name, string Type, string? Parent, UObject Template);

    /// <summary>Resolved component list for a class: SCS nodes root-to-leaf, templates replaced by the most-derived override.</summary>
    private static List<Comp> Resolve(UClass klass)
    {
        var chain = new List<CUE4Parse.UE4.Objects.Engine.UBlueprintGeneratedClass>();
        for (UStruct? c = klass; c is CUE4Parse.UE4.Objects.Engine.UBlueprintGeneratedClass b && chain.Count < 12; c = c.SuperStruct?.Load<UStruct>()) chain.Add(b);
        var comps = new Dictionary<string, Comp>(StringComparer.OrdinalIgnoreCase);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var scs = chain[i].GetOrDefault<FPackageIndex?>("SimpleConstructionScript")?.Load<USimpleConstructionScript>();
            foreach (var nodeIdx in scs?.AllNodes ?? [])
            {
                if (nodeIdx?.Load<USCS_Node>() is not { } node || node.ComponentTemplate?.Load() is not { } t) continue;
                var parent = node.GetOrDefault<FName>("ParentComponentOrVariableName").Text;
                comps[node.InternalVariableName.Text] = new Comp(node.InternalVariableName.Text, t.ExportType, parent is "None" or "" ? null : parent, t);
            }
        }
        // Overrides: the most-derived class wins, so apply from base to leaf.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var ich = chain[i].GetOrDefault<FPackageIndex?>("InheritableComponentHandler")?.Load();
            foreach (var r in ich?.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("Records", []) ?? [])
            {
                var t = r.GetOrDefault<FPackageIndex?>("ComponentTemplate")?.Load();
                var key = r.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("ComponentKey");
                var name = key?.GetOrDefault<FName>("SCSVariableName").Text;
                if (t is null || string.IsNullOrEmpty(name) || !comps.TryGetValue(name, out var existing)) continue;
                comps[name] = existing with { Template = t };
            }
        }
        return comps.Values.ToList();
    }

    [Fact]
    public void Dump_ResolvedMeshesForAllPlacedClasses()
    {
        using var provider = typeof(GameMeshProbe).GetMethod("CreateProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null) as DefaultFileProvider;
        var save = typeof(GameMeshProbe).GetMethod("FacilitySave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null) as string;
        if (provider is null || save is null) { _output.WriteLine("paks or fixture missing"); return; }
        var census = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(save));
        var groups = census.Objects!.Where(o => o.ClassPath is not null).GroupBy(o => o.ClassPath!).OrderByDescending(g => g.Count()).ToList();
        int withMesh = 0, without = 0, failedLoad = 0, detailed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var noMesh = new List<string>();
        foreach (var g in groups)
        {
            if (!provider.TryLoadPackageObject(g.Key, out var cls) || cls is not UClass klass) { failedLoad++; noMesh.Add("LOAD " + g.Key); continue; }
            var comps = Resolve(klass);
            var meshes = comps.Select(c => (c, m: c.Template.GetOrDefault<FPackageIndex?>("StaticMesh") ?? c.Template.GetOrDefault<FPackageIndex?>("SkeletalMesh") ?? c.Template.GetOrDefault<FPackageIndex?>("SkinnedAsset")))
                .Where(x => x.m is { IsNull: false }).ToList();
            if (meshes.Count == 0) { without++; noMesh.Add($"{g.Key} x{g.Count()} built={g.Count(o => o.DeployedByPlayer == true)} comps={string.Join(",", comps.Select(c => c.Type))}"); continue; }
            withMesh++;
            if (detailed++ < 6)
            {
                _output.WriteLine($"\n== {g.Key}");
                foreach (var (c, m) in meshes)
                {
                    var loc = c.Template.GetOrDefault<FVector>("RelativeLocation");
                    var rot = c.Template.GetOrDefault<FRotator>("RelativeRotation");
                    var scl = c.Template.GetOrDefault("RelativeScale3D", FVector.OneVector);
                    _output.WriteLine($"   {c.Type} {c.Name} parent={c.Parent} mesh={m!.ResolvedObject?.GetPathName()} loc={loc} rot={rot} scale={scl}");
                    if (m.Load() is not UStaticMesh sm) continue;
                    var overrides = c.Template.GetOrDefault<FPackageIndex?[]>("OverrideMaterials", []);
                    for (var i = 0; i < sm.StaticMaterials.Length; i++)
                    {
                        var matIdx = i < overrides.Length && overrides[i] is { IsNull: false } o ? o : sm.StaticMaterials[i].MaterialInterface;
                        var mat = matIdx?.Load<UMaterialInterface>();
                        _output.WriteLine($"      mat[{i}] {mat?.GetPathName()} ({mat?.ExportType})");
                        if (mat is null) continue;
                        var p = new CMaterialParams2();
                        mat.GetParams(p, EMaterialDepth.AllLayers);
                        foreach (var (k, v) in p.Textures.Take(8)) _output.WriteLine($"         tex {k} = {v.GetPathName()}");
                        foreach (var (k, v) in p.Colors.Take(4)) _output.WriteLine($"         col {k} = {v}");
                    }
                }
            }
        }
        _output.WriteLine($"\nclasses={groups.Count} withMesh={withMesh} without={without} failedLoad={failedLoad} in {sw.ElapsedMilliseconds} ms");
        foreach (var n in noMesh) _output.WriteLine("  no mesh: " + n);
    }
}

public class GameMaterialProbe
{
    private readonly ITestOutputHelper _output;
    public GameMaterialProbe(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("/Game/Models/Items/Misc/M_Lootbag_01.M_Lootbag_01", "/Game/Models/Items/Misc/SM_LootBag_01.SM_LootBag_01")]
    [InlineData("/Game/Models/Furniture/Crafting/CraftingBench/M_CraftingBench_Base.M_CraftingBench_Base", "/Game/Models/Furniture/Crafting/CraftingBench/SM_CraftingBench.SM_CraftingBench")]
    [InlineData("/Game/Models/Items/Misc/M_WaterCooler.M_WaterCooler", "/Game/Models/Items/Misc/SM_WaterCooler_01.SM_WaterCooler_01")]
    public void Dump_MaterialInstanceOwnParams(string materialPath, string meshPath)
    {
        using var provider = typeof(GameMeshProbe).GetMethod("CreateProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null) as DefaultFileProvider;
        if (provider is null) return;
        for (var obj = provider.LoadPackageObject(materialPath); obj is not null;)
        {
            _output.WriteLine($"{obj.GetPathName()} ({obj.ExportType})");
            foreach (var t in obj.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("TextureParameterValues", []))
                _output.WriteLine($"   tex {t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback>("ParameterInfo")?.GetOrDefault<FName>("Name").Text} = {t.GetOrDefault<FPackageIndex>("ParameterValue")?.ResolvedObject?.GetPathName()}");
            foreach (var t in obj.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("VectorParameterValues", []))
                _output.WriteLine($"   vec {t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback>("ParameterInfo")?.GetOrDefault<FName>("Name").Text} = {t.GetOrDefault<FLinearColor>("ParameterValue")}");
            foreach (var t in obj.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback[]>("ScalarParameterValues", []))
                _output.WriteLine($"   scl {t.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback>("ParameterInfo")?.GetOrDefault<FName>("Name").Text} = {t.GetOrDefault<float>("ParameterValue")}");
            if (obj is UMaterial m)
            {
                var cached = m.GetOrDefault<CUE4Parse.UE4.Assets.Objects.FStructFallback?>("CachedExpressionData");
                _output.WriteLine($"   (base material) referenced textures: {string.Join(", ", m.ReferencedTextures.Take(12).Select(t => t?.Name))}");
                break;
            }
            obj = obj.GetOrDefault<FPackageIndex?>("Parent")?.Load();
        }
        if (provider.LoadPackageObject(meshPath) is UStaticMesh sm)
        {
            using var dto = new StaticMeshDto(sm);
            var lod = dto.LODs[0];
            _output.WriteLine($"mesh verts={lod.Vertices.Length} vertexColors={(lod.VertexColors?.Length ?? 0)} extraUVs={lod.ExtraUvs.Length}");
            if (lod.VertexColors is { Length: > 0 } vc)
            {
                var colors = vc[0].Colors.Take(2000).Distinct().Take(10);
                _output.WriteLine($"   vc[0] '{vc[0].Name}' distinct sample: {string.Join(" ", colors)}");
            }
        }
    }
}
