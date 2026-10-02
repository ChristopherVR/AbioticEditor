using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Tests;

/// <summary>
/// Research: the materials a placed story character draws with (Jimmy in Facility_Botanical), and
/// which parameters set their skin, hair and face, so the 3D view can colour them. Set
/// <c>CHARACTER_PROBE_OUT</c> to a file. Not part of the normal test run.
/// </summary>
public sealed class CharacterMaterialProbe
{
    [Fact]
    public void Dump_character_materials()
    {
        var output = Environment.GetEnvironmentVariable("CHARACTER_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var lines = new List<string>();
        assets.UseFileProvider(p =>
        {
            if (!p.TryLoadPackage("/Game/Maps/Facility_Botanical", out var package)) { lines.Add("no package"); return 0; }
            var actor = package.GetExportOrNull("NarrativeNPC_Human_ParentBP_C_2");
            if (actor is null) { lines.Add("no actor"); return 0; }
            lines.Add("actor props: " + string.Join(", ", actor.Properties.Select(x => x.Name.Text)));
            foreach (var export in package.GetExports())
            {
                if (export.Outer?.Name != actor.Name) continue;
                lines.Add($"== {export.Name} ({export.ExportType})");
                foreach (var prop in export.Properties) lines.Add($"   {prop.Name.Text} = {Short(prop.Tag?.GenericValue)}");
                var mats = new List<UObject?>();
                if (export.TryGetValue(out FPackageIndex[] overrides, "OverrideMaterials")) mats.AddRange(overrides.Select(o => o.Load()));
                if (export.TryGetValue(out FPackageIndex meshIndex, "SkeletalMesh") || export.TryGetValue(out meshIndex, "SkinnedAsset"))
                {
                    var mesh = meshIndex.Load();
                    lines.Add($"   mesh {mesh?.GetPathName()}");
                }
                foreach (var m in mats.Where(m => m is not null)) DumpMaterial(m!, lines, "   ");
            }
            return 0;
        });
        File.WriteAllLines(output, lines);
    }

    private static void DumpMaterial(UObject material, List<string> lines, string indent)
    {
        for (UObject? current = material; current is not null; current = current.GetOrDefault<FPackageIndex?>("Parent")?.Load())
        {
            lines.Add($"{indent}material {current.GetPathName()} ({current.ExportType})");
            foreach (var t in current.GetOrDefault<FStructFallback[]>("TextureParameterValues", []))
                lines.Add($"{indent}  tex {Name(t)} = {t.GetOrDefault<FPackageIndex?>("ParameterValue")?.ResolvedObject?.GetPathName()}");
            foreach (var v in current.GetOrDefault<FStructFallback[]>("VectorParameterValues", []))
                lines.Add($"{indent}  vec {Name(v)} = {v.GetOrDefault<CUE4Parse.UE4.Objects.Core.Math.FLinearColor>("ParameterValue")}");
            foreach (var s in current.GetOrDefault<FStructFallback[]>("ScalarParameterValues", []))
                lines.Add($"{indent}  scalar {Name(s)} = {s.GetOrDefault<float>("ParameterValue")}");
            if (current is UMaterial) break;
        }
    }

    private static string? Name(FStructFallback parameter)
        => parameter.GetOrDefault<FStructFallback?>("ParameterInfo")?.GetOrDefault<FName>("Name").Text ?? parameter.GetOrDefault<FName>("ParameterName").Text;

    private static string Short(object? value)
    {
        var text = value?.ToString() ?? "null";
        return text.Length > 160 ? text[..160] + "..." : text;
    }
}
