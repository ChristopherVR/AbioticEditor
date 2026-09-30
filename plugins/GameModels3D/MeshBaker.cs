using System.Numerics;
using AbioticEditor.Plugins.Scene;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>What the scene needs to know about a mesh without baking it.</summary>
/// <param name="Materials">The mesh's own material per slot (object paths; null for an empty slot).</param>
/// <param name="BoundsMin">Unreal-space bounding box minimum (cm).</param>
/// <param name="BoundsMax">Unreal-space bounding box maximum (cm).</param>
internal sealed record MeshInfo(IReadOnlyList<string?> Materials, Vector3 BoundsMin, Vector3 BoundsMax);

/// <summary>
/// Converts a game mesh into <see cref="SceneMeshFormat"/> in the viewer's space. Skeletal meshes
/// are drawn in their bind pose (the view shows where things are, not how they animate).
/// </summary>
internal static class MeshBaker
{
    public static MeshInfo? Describe(UObject mesh)
    {
        switch (mesh)
        {
            case UStaticMesh sm when sm.RenderData?.Bounds is { } b:
            {
                var box = b.GetBox();
                return new MeshInfo(
                    sm.StaticMaterials.Select(m => m.MaterialInterface?.ResolvedObject?.GetPathName()).ToArray(),
                    new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z));
            }
            case USkeletalMesh sk:
            {
                var box = sk.ImportedBounds.GetBox();
                return new MeshInfo(
                    sk.SkeletalMaterials.Select(m => m.Material?.ResolvedObject?.GetPathName()).ToArray(),
                    new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z));
            }
            default:
                return null;
        }
    }

    /// <summary>Bakes LOD <paramref name="lod"/> (clamped to what the mesh has), or null when it has no geometry.</summary>
    public static byte[]? Bake(UObject mesh, int lod)
    {
        switch (mesh)
        {
            case UStaticMesh sm:
            {
                using var dto = new StaticMeshDto(sm, EMeshQuality.All);
                return Bake(dto, lod);
            }
            case USkeletalMesh sk:
            {
                using var dto = new SkeletalMeshDto(sk, EMeshQuality.All);
                return Bake(dto, lod);
            }
            default:
                return null;
        }
    }

    private static byte[]? Bake<TVertex>(MeshDto<TVertex> dto, int lod) where TVertex : struct, IMeshVertex
    {
        var lods = dto.LODs.Where(l => !l.IsNanite && l.Vertices.Length > 0 && l.Indices.Length > 0).ToList();
        if (lods.Count == 0) lods = dto.LODs.Where(l => l.Vertices.Length > 0 && l.Indices.Length > 0).ToList();
        if (lods.Count == 0) return null;
        var source = lods[Math.Clamp(lod, 0, lods.Count - 1)];

        var vertexCount = source.Vertices.Length;
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var uvs = new float[vertexCount * 2];
        for (var i = 0; i < vertexCount; i++)
        {
            var v = source.Vertices[i];
            // Unreal (X, Y, Z) cm -> viewer (X, Z, Y) m. The swap is a reflection, which is exactly
            // what makes Unreal's triangle order counter-clockwise in the right-handed viewer, so the
            // index order is kept (the glTF writer in CUE4Parse does the same).
            positions[i * 3] = v.Position.X / 100f;
            positions[(i * 3) + 1] = v.Position.Z / 100f;
            positions[(i * 3) + 2] = v.Position.Y / 100f;
            var n = new Vector3(v.Normal.X, v.Normal.Z, v.Normal.Y);
            var length = n.Length();
            n = length > 1e-6f ? n / length : Vector3.UnitY;
            normals[i * 3] = n.X;
            normals[(i * 3) + 1] = n.Y;
            normals[(i * 3) + 2] = n.Z;
            uvs[i * 2] = v.Uv.U;
            uvs[(i * 2) + 1] = v.Uv.V;
        }

        var sections = new List<SceneMeshFormat.Section>();
        foreach (var s in source.Sections)
        {
            if (!s.IsValid || s.NumFaces <= 0) continue;
            var count = Math.Min(s.NumFaces * 3, source.Indices.Length - s.FirstIndex);
            if (count <= 0) continue;
            sections.Add(new SceneMeshFormat.Section(s.MaterialIndex, s.FirstIndex, count));
        }
        return sections.Count == 0 ? null : SceneMeshFormat.Write(positions, normals, uvs, source.Indices, sections);
    }
}
