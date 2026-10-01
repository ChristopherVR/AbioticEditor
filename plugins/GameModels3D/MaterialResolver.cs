using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>A simplified material in Unreal terms, before texture ids are minted.</summary>
/// <param name="Effect">
/// A see-through glow (additive, or translucent and unlit): fake light beams, glow cards and fog
/// planes. They only make sense with the game's lighting, so the view leaves them out.
/// </param>
/// <param name="TileCm">
/// The material's own world tiling (its <c>Scale</c> parameter, centimetres per repeat, as the
/// liquid surface masters use it), or 0.
/// </param>
/// <param name="Decal">The material's domain is a decal (<c>MD_DeferredDecal</c>).</param>
internal sealed record ResolvedMaterial(string? BaseColorTexture, float[] Color, float Opacity, bool TwoSided, bool Masked, bool Emissive, bool Effect = false, float TileCm = 0, bool Decal = false)
{
    public static readonly ResolvedMaterial Fallback = new(null, [0.62f, 0.62f, 0.6f], 1f, false, false, false);
}

/// <summary>
/// Reduces a game material to "a base colour texture and a tint", which is what a quick
/// preview needs. Material graphs are not evaluated; instead the material instance chain's own
/// parameter overrides are read, leaf first, and the base colour is picked by parameter name and
/// then by texture naming conventions.
/// </summary>
/// <remarks>
/// Abiotic Factor's item and furniture materials are instances of shared master materials that
/// take the diffuse as a parameter named <c>Texture</c> (see the research note); the other names
/// cover engine and marketplace masters used by level geometry.
/// </remarks>
internal static class MaterialResolver
{
    private static readonly string[] BaseColorParameters =
    [
        "Texture", "BaseColor", "Base Color", "BaseColorTexture", "BaseColor Texture", "Base_Color", "BaseTexture",
        "Diffuse", "DiffuseTexture", "Diffuse Texture", "Albedo", "AlbedoTexture", "Color Texture", "ColorTexture",
        "MainTexture", "Main Texture", "BC", "D", "Tex", "Texture1", "Texture_A",
    ];

    private static readonly string[] BaseColorSuffixes = ["_D", "_BC", "_Diffuse", "_Albedo", "_BaseColor", "_Base_Color", "_Color", "_Col", "_C", "_Diff", "_ALB"];

    private static readonly string[] NotBaseColor =
    [
        "normal", "_n", "rough", "metal", "_orm", "_arm", "_rma", "_mra", "mask", "spec", "emiss", "_em", "_e",
        "durability", "noise", "cracked", "height", "_h", "displace", "opacity", "_ao", "occlusion", "bump", "checker",
        "grid", "whitegeneric", "rendertarget", "tc_", "lut", "cubemap", "gradient",
    ];

    private static readonly string[] TintParameters = ["Color", "BaseColor", "Base Color", "Tint", "TintColor", "Tint Color", "Albedo Tint", "BaseColorTint", "Color1", "Diffuse Color", "DiffuseColor", "PaintColor"];

    private const float MinTileCm = 50f;
    private const float MaxTileCm = 10000f;

    public static ResolvedMaterial Resolve(UMaterialInterface? material)
    {
        if (material is null) return ResolvedMaterial.Fallback;

        var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vectors = new Dictionary<string, FLinearColor>(StringComparer.OrdinalIgnoreCase);
        var scalars = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        UMaterial? baseMaterial = null;
        var chain = new List<UObject>();
        UObject? current = material;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            chain.Add(current);
            foreach (var t in current.GetOrDefault<FStructFallback[]>("TextureParameterValues", []))
            {
                var name = ParameterName(t);
                var path = t.GetOrDefault<FPackageIndex?>("ParameterValue")?.ResolvedObject?.GetPathName();
                if (name is not null && path is not null) textures.TryAdd(name, path);
            }
            foreach (var v in current.GetOrDefault<FStructFallback[]>("VectorParameterValues", []))
            {
                if (ParameterName(v) is { } name) vectors.TryAdd(name, v.GetOrDefault<FLinearColor>("ParameterValue"));
            }
            foreach (var s in current.GetOrDefault<FStructFallback[]>("ScalarParameterValues", []))
            {
                if (ParameterName(s) is { } name) scalars.TryAdd(name, s.GetOrDefault<float>("ParameterValue"));
            }
            if (current is UMaterial m) { baseMaterial = m; break; }
            current = current.GetOrDefault<FPackageIndex?>("Parent")?.Load();
        }

        var texture = PickBaseColor(textures);
        if (texture is null && baseMaterial is not null)
        {
            // A plain material (no instance): its referenced textures are all we have.
            var referenced = baseMaterial.ReferencedTextures
                .Where(t => t is not null)
                .Select(t => t!.GetPathName())
                .ToList();
            texture = referenced.FirstOrDefault(IsBaseColorName) ?? referenced.FirstOrDefault(p => !LooksLikeData(p));
        }

        var tint = PickTint(vectors, texture is null);
        // Enum properties are stored as names (EBlendMode::BLEND_Translucent), so they are read as
        // text; only serialized values count (an unset one is the engine default, opaque and lit).
        var blend = Props.EnumText(chain, "BlendMode") ?? "BLEND_Opaque";
        var shading = Props.EnumText(chain, "ShadingModel") ?? "MSM_DefaultLit";
        var overrides = Props.Get<FStructFallback?>(chain, "BasePropertyOverrides", null);
        if (overrides is not null)
        {
            if (overrides.GetOrDefault("bOverride_BlendMode", false)) blend = Props.EnumText(overrides.Properties, "BlendMode") ?? blend;
            if (overrides.GetOrDefault("bOverride_ShadingModel", false)) shading = Props.EnumText(overrides.Properties, "ShadingModel") ?? shading;
        }
        var twoSided = Props.Get(chain, "TwoSided", false)
            || (overrides?.GetOrDefault("bOverride_TwoSided", false) == true && overrides.GetOrDefault("TwoSided", false));

        var additive = blend.Contains("Additive", StringComparison.OrdinalIgnoreCase);
        var translucent = additive
                          || blend.Contains("Translucent", StringComparison.OrdinalIgnoreCase)
                          || blend.Contains("Modulate", StringComparison.OrdinalIgnoreCase);
        var opacity = translucent
            ? Math.Clamp(scalars.TryGetValue("Opacity", out var o) ? o : 0.35f, 0.12f, 0.85f)
            : 1f;
        var masked = blend.Contains("Masked", StringComparison.OrdinalIgnoreCase);
        var unlit = shading.Contains("Unlit", StringComparison.OrdinalIgnoreCase);
        var tile = scalars.TryGetValue("Scale", out var scale) && scale is >= MinTileCm and <= MaxTileCm ? scale : 0f;
        var decal = (Props.EnumText(chain, "MaterialDomain") ?? "").Contains("Decal", StringComparison.OrdinalIgnoreCase);
        // A decal's see-through parts come from its texture's alpha, not a material opacity.
        return new ResolvedMaterial(texture, tint, decal ? 1f : opacity, twoSided || decal, masked, unlit,
            !decal && (additive || (translucent && unlit)), tile, decal);
    }

    private static string? ParameterName(FStructFallback parameter)
        => parameter.GetOrDefault<FStructFallback?>("ParameterInfo")?.GetOrDefault<FName>("Name").Text
           ?? parameter.GetOrDefault<FName>("ParameterName").Text;

    private static string? PickBaseColor(Dictionary<string, string> textures)
    {
        foreach (var name in BaseColorParameters)
        {
            if (textures.TryGetValue(name, out var path) && !LooksLikeData(path)) return path;
        }
        foreach (var (name, path) in textures)
        {
            if ((name.Contains("color", StringComparison.OrdinalIgnoreCase) || name.Contains("diffuse", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("albedo", StringComparison.OrdinalIgnoreCase)) && !LooksLikeData(path))
                return path;
        }
        return textures.Values.FirstOrDefault(IsBaseColorName)
               ?? textures.Values.FirstOrDefault(p => !LooksLikeData(p));
    }

    private static float[] PickTint(Dictionary<string, FLinearColor> vectors, bool flat)
    {
        foreach (var name in TintParameters)
        {
            if (vectors.TryGetValue(name, out var c))
            {
                var tint = new[] { Math.Clamp(c.R, 0f, 4f), Math.Clamp(c.G, 0f, 4f), Math.Clamp(c.B, 0f, 4f) };
                // A black tint on a textured material is almost always an unused default.
                if (!flat && tint.Max() < 0.02f) break;
                return tint;
            }
        }
        return flat ? [0.62f, 0.62f, 0.6f] : [1f, 1f, 1f];
    }

    private static string AssetName(string path)
    {
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        return dot > slash ? path[(dot + 1)..] : path[(slash + 1)..];
    }

    private static bool IsBaseColorName(string path)
    {
        var name = AssetName(path);
        return BaseColorSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) && !LooksLikeData(path);
    }

    /// <summary>A texture that is data (normals, masks, noise) rather than colour, going by its name.</summary>
    private static bool LooksLikeData(string path)
    {
        var name = AssetName(path);
        if (path.StartsWith("/Engine/", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var marker in NotBaseColor)
        {
            if (marker.StartsWith('_'))
            {
                if (name.EndsWith(marker, StringComparison.OrdinalIgnoreCase) || name.Contains(marker + "_", StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
