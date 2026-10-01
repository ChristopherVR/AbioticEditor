using System.Globalization;
using System.Numerics;
using AbioticEditor.Plugins.Scene;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Component.Landscape;
using CUE4Parse_Conversion.Dto;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Landscape terrain as level meshes. Each <c>LandscapeComponent</c> becomes one mesh whose key is
/// its map plus export index (<c>/Game/Maps/V_Alps.V_Alps#land=812</c>), drawn with its landscape
/// proxy's transform. Heights come from CUE4Parse's landscape reader; the mesh is in the proxy's
/// local units (one unit per landscape quad, scaled by the proxy), like the engine's own export.
/// </summary>
internal static class LandscapeBaker
{
    /// <summary>Separates the map's object path from the component's export index in a mesh key.</summary>
    public const string Marker = "#land=";

    /// <summary>Landscape quads per texture repeat: layer textures tile every few metres in the game.</summary>
    private const float QuadsPerTextureRepeat = 4f;

    public static string Key(string mapObjectPath, int exportIndex)
        => mapObjectPath + Marker + exportIndex.ToString(CultureInfo.InvariantCulture);

    public static bool IsKey(string mesh) => mesh.Contains(Marker, StringComparison.Ordinal);

    /// <summary>The component a key names, or null when the key, map or export is not a landscape component.</summary>
    public static ULandscapeComponent? Load(IFileProvider provider, string key)
    {
        var at = key.IndexOf(Marker, StringComparison.Ordinal);
        if (at <= 0 || !int.TryParse(key.AsSpan(at + Marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return null;
        var objectPath = key[..at];
        var dot = objectPath.LastIndexOf('.');
        var package = dot > 0 ? objectPath[..dot] : objectPath;
        return provider.TryLoadPackage(package, out var pkg) ? pkg.GetExport(index) as ULandscapeComponent : null;
    }

    /// <summary>Bounds (proxy-local units) and the one material slot of a component.</summary>
    public static MeshInfo Describe(ULandscapeComponent component, string? material)
    {
        var box = component.CachedLocalBox;
        var offset = component.GetRelativeLocation();
        var min = new Vector3(box.Min.X + offset.X, box.Min.Y + offset.Y, box.Min.Z + offset.Z);
        var max = new Vector3(box.Max.X + offset.X, box.Max.Y + offset.Y, box.Max.Z + offset.Z);
        return new MeshInfo([material], min, max);
    }

    /// <summary>
    /// The component as an ABM1 mesh; <paramref name="lod"/> above zero keeps every other row and
    /// column (a quarter of the triangles), which is plenty for terrain seen around a base.
    /// </summary>
    public static byte[]? Bake(ULandscapeComponent component, int lod)
    {
        var size = component.ComponentSizeQuads;
        if (size <= 0) return null;
        using var dto = new LandscapeMeshDto(component);
        if (dto.LODs.Count == 0 || dto.LODs[0].Vertices.Length == 0) return null;
        var source = dto.LODs[0];

        // Rebuild the vertex grid from each vertex's landscape coordinates (its first UV is the
        // quad position plus the section base), so the layout does not depend on reader internals.
        var grid = new Vector3[size + 1, size + 1];
        var normalGrid = new Vector3[size + 1, size + 1];
        var filled = new bool[size + 1, size + 1];
        foreach (var v in source.Vertices)
        {
            var x = (int)MathF.Round(v.Uv.U - component.SectionBaseX);
            var y = (int)MathF.Round(v.Uv.V - component.SectionBaseY);
            if (x < 0 || y < 0 || x > size || y > size) continue;
            grid[x, y] = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
            normalGrid[x, y] = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
            filled[x, y] = true;
        }

        // Rows and columns kept: every one, or every other one at a lower detail. The last row and
        // column are always kept so neighbouring components still meet edge to edge.
        var keep = Enumerable.Range(0, size + 1).Where(i => lod <= 0 || i % 2 == 0 || i == size).ToArray();
        var side = keep.Length;
        var cells = side - 1;
        var positions = new float[side * side * 3];
        var normals = new float[side * side * 3];
        var uvs = new float[side * side * 2];
        for (var gy = 0; gy < side; gy++)
        for (var gx = 0; gx < side; gx++)
        {
            int x = keep[gx], y = keep[gy];
            if (!filled[x, y]) return null;
            var i = (gy * side) + gx;
            var p = grid[x, y];
            // Unreal (X, Y, Z) -> viewer (X, Z, Y), divided by 100 like every other baked mesh; the
            // proxy's transform (applied as the instance matrix) brings in the real scale.
            positions[i * 3] = p.X / 100f;
            positions[(i * 3) + 1] = p.Z / 100f;
            positions[(i * 3) + 2] = p.Y / 100f;
            var n = normalGrid[x, y];
            var viewer = new Vector3(n.X, n.Z, n.Y);
            viewer = viewer.LengthSquared() > 1e-8f ? Vector3.Normalize(viewer) : Vector3.UnitY;
            normals[i * 3] = viewer.X;
            normals[(i * 3) + 1] = viewer.Y;
            normals[(i * 3) + 2] = viewer.Z;
            uvs[i * 2] = (x + component.SectionBaseX) / QuadsPerTextureRepeat;
            uvs[(i * 2) + 1] = (y + component.SectionBaseY) / QuadsPerTextureRepeat;
        }

        // Same triangle order as the engine's landscape export; the axis swap above is a
        // reflection, which keeps these counter-clockwise in the viewer (see MeshBaker).
        var indices = new uint[cells * cells * 6];
        var k = 0;
        for (var y = 0; y < cells; y++)
        for (var x = 0; x < cells; x++)
        {
            uint V(int cx, int cy) => (uint)((cy * side) + cx);
            indices[k++] = V(x, y);
            indices[k++] = V(x + 1, y + 1);
            indices[k++] = V(x + 1, y);
            indices[k++] = V(x, y);
            indices[k++] = V(x, y + 1);
            indices[k++] = V(x + 1, y + 1);
        }
        return SceneMeshFormat.Write(positions, normals, uvs, indices, [new SceneMeshFormat.Section(0, 0, indices.Length)]);
    }
}
