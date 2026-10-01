using System.Numerics;
using CUE4Parse.UE4.Objects.Core.Math;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Unreal-space transforms to the 3D view's space. Composition happens in Unreal space with
/// System.Numerics row-vector matrices (the same convention and quaternion formula Unreal uses,
/// so a component's world matrix is <c>relative * parentWorld</c>); only the final matrix is
/// converted.
/// </summary>
/// <remarks>
/// Viewer space is <c>(X, Z, Y) / 100</c> of Unreal space (see <c>PlacedSceneSpace</c> in Core). For
/// a row-vector point map <c>v' = v M</c> and the change of basis <c>u = v C</c>, the same map in
/// viewer space is <c>C^-1 M C</c>. Its row-major element order is the column-major order
/// <c>THREE.Matrix4.fromArray</c> reads, so it is sent as-is.
/// </remarks>
internal static class SceneMath
{
    private static readonly Matrix4x4 ToViewerBasis = new(
        0.01f, 0, 0, 0,
        0, 0, 0.01f, 0,
        0, 0.01f, 0, 0,
        0, 0, 0, 1);

    private static readonly Matrix4x4 FromViewerBasis = new(
        100f, 0, 0, 0,
        0, 0, 100f, 0,
        0, 100f, 0, 0,
        0, 0, 0, 1);

    /// <summary>An Unreal relative transform (scale, then rotate, then translate).</summary>
    public static Matrix4x4 Transform(FVector location, FRotator rotation, FVector scale)
        => Transform(location, rotation.Quaternion(), scale);

    public static Matrix4x4 Transform(FVector location, FQuat rotation, FVector scale)
        => Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
           * Matrix4x4.CreateFromQuaternion(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W))
           * Matrix4x4.CreateTranslation(location.X, location.Y, location.Z);

    public static Matrix4x4 Transform(FTransform t) => Transform(t.Translation, t.Rotation, t.Scale3D);

    /// <summary>An Unreal-space matrix as the viewer's 16 floats.</summary>
    public static float[] ToViewer(Matrix4x4 unreal)
    {
        var m = FromViewerBasis * unreal * ToViewerBasis;
        return
        [
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        ];
    }

    /// <summary>An Unreal-space point (cm) in viewer space (m).</summary>
    public static Vector3 PointToViewer(Vector3 unreal) => new(unreal.X / 100f, unreal.Z / 100f, unreal.Y / 100f);

    /// <summary>A viewer-space point (m) in Unreal space (cm).</summary>
    public static Vector3 PointFromViewer(Vector3 viewer) => new(viewer.X * 100f, viewer.Z * 100f, viewer.Y * 100f);

    /// <summary>Grows a viewer-space box by an Unreal-space box carried through <paramref name="unreal"/>.</summary>
    public static void Encapsulate(ref Vector3 min, ref Vector3 max, Vector3 boxMin, Vector3 boxMax, Matrix4x4 unreal)
    {
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? boxMin.X : boxMax.X, (i & 2) == 0 ? boxMin.Y : boxMax.Y, (i & 4) == 0 ? boxMin.Z : boxMax.Z);
            var p = PointToViewer(Vector3.Transform(corner, unreal));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }

    /// <summary>
    /// A component's world transform from its relative one and its parent's world transform. A
    /// component flagged absolute in location, rotation or scale ignores that part of its parent
    /// (the tram rails' spline is absolute, so its points are world positions).
    /// </summary>
    public static Matrix4x4 Attach(Matrix4x4 local, Matrix4x4 parentWorld, bool absoluteLocation, bool absoluteRotation, bool absoluteScale)
    {
        if (!absoluteLocation && !absoluteRotation && !absoluteScale) return local * parentWorld;
        if (!Matrix4x4.Decompose(parentWorld, out var scale, out var rotation, out var translation)) return local * parentWorld;
        var parent = Matrix4x4.CreateScale(absoluteScale ? Vector3.One : scale)
                     * Matrix4x4.CreateFromQuaternion(absoluteRotation ? Quaternion.Identity : rotation)
                     * Matrix4x4.CreateTranslation(absoluteLocation ? Vector3.Zero : translation);
        var world = local * parent;
        if (absoluteLocation) world.Translation = local.Translation;
        return world;
    }
}
