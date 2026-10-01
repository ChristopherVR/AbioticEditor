using AbioticEditor.Plugins.Scene;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// A landscape material as blended texture layers: the terrain master's five slots
/// (<c>Primary</c> to <c>Quinary</c>), each with its texture, its <c>DiffuseColor_</c> tint times
/// <c>DiffuseIntensity_</c>, and its <c>_Scale</c> tiling, read from the material instance chain
/// (the leaf's values win). Slot order matches <see cref="LandscapeBaker.SlotLayers"/>.
/// </summary>
internal static class TerrainMaterial
{
    private static readonly string[] Slots = ["Primary", "Secondary", "Tertiary", "Quaternary", "Quinary"];

    /// <summary>Metres per texture repeat at a <c>_Scale</c> of 1 (an approximation of the master's world-position tiling).</summary>
    private const float MetresPerScale = 3f;

    public static IReadOnlyList<(string? Texture, float[] Color, float RepeatMetres)> Read(IFileProvider provider, string materialPath)
    {
        var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scalars = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var vectors = new Dictionary<string, FLinearColor>(StringComparer.OrdinalIgnoreCase);
        UObject? current = provider.TryLoadPackageObject(materialPath, out var start) ? start : null;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            foreach (var t in current.GetOrDefault<FStructFallback[]>("TextureParameterValues", []))
            {
                if (Name(t) is { } n && t.GetOrDefault<FPackageIndex?>("ParameterValue")?.ResolvedObject?.GetPathName() is { } path) textures.TryAdd(n, path);
            }
            foreach (var v in current.GetOrDefault<FStructFallback[]>("ScalarParameterValues", []))
            {
                if (Name(v) is { } n) scalars.TryAdd(n, v.GetOrDefault<float>("ParameterValue"));
            }
            foreach (var v in current.GetOrDefault<FStructFallback[]>("VectorParameterValues", []))
            {
                if (Name(v) is { } n) vectors.TryAdd(n, v.GetOrDefault<FLinearColor>("ParameterValue"));
            }
            current = current.GetOrDefault<FPackageIndex?>("Parent")?.Load();
        }

        var layers = new List<(string?, float[], float)>();
        foreach (var slot in Slots)
        {
            // The master spells the fourth slot's colour and intensity "Quarternary".
            var colourSlot = slot == "Quaternary" ? "Quarternary" : slot;
            var tint = vectors.TryGetValue("DiffuseColor_" + colourSlot, out var c) ? c : new FLinearColor(1, 1, 1, 1);
            var intensity = scalars.TryGetValue("DiffuseIntensity_" + colourSlot, out var k) ? k : 1f;
            var scale = scalars.TryGetValue(slot + "_Scale", out var sc) && sc > 0 ? sc : 1f;
            layers.Add((textures.GetValueOrDefault(slot),
                [Math.Clamp(tint.R * intensity, 0f, 4f), Math.Clamp(tint.G * intensity, 0f, 4f), Math.Clamp(tint.B * intensity, 0f, 4f)],
                scale * MetresPerScale));
        }
        return layers;
    }

    private static string? Name(FStructFallback parameter)
        => parameter.GetOrDefault<FStructFallback?>("ParameterInfo")?.GetOrDefault<FName>("Name").Text;
}
