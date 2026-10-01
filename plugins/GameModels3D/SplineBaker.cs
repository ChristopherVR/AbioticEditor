using System.Globalization;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Spline meshes (pipes and cables bent along a curve when the level loads) as level meshes. Each
/// <c>SplineMeshComponent</c> is one mesh keyed by its map plus export index
/// (<c>/Game/Maps/Facility.Facility#spline=4711</c>), baked by bending its static mesh the way the
/// engine does (<c>USplineMeshComponent::CalcSliceTransform</c>): the mesh's length along the
/// forward axis is mapped onto a cubic Hermite curve between the start and end points, and each
/// slice is turned to the curve's direction, rolled, offset and scaled between the start and end
/// values. Drawn with the component's own transform.
/// </summary>
internal static class SplineBaker
{
    /// <summary>Separates the map's object path from the component's export index in a mesh key.</summary>
    public const string Marker = "#spline=";

    private const int CurveSamples = 9;

    public static string Key(string mapObjectPath, int exportIndex)
        => mapObjectPath + Marker + exportIndex.ToString(CultureInfo.InvariantCulture);

    public static bool IsKey(string mesh) => mesh.Contains(Marker, StringComparison.Ordinal);

    /// <summary>The component a key names, or null when it is not a spline mesh component.</summary>
    public static UObject? Load(IFileProvider provider, string key)
    {
        var at = key.IndexOf(Marker, StringComparison.Ordinal);
        if (at <= 0 || !int.TryParse(key.AsSpan(at + Marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return null;
        var objectPath = key[..at];
        var dot = objectPath.LastIndexOf('.');
        var package = dot > 0 ? objectPath[..dot] : objectPath;
        if (!provider.TryLoadPackage(package, out var pkg)) return null;
        return pkg.GetExport(index) is { } component && IsSplineMesh(component.ExportType) ? component : null;
    }

    public static bool IsSplineMesh(string exportType) => exportType.Contains("SplineMeshComponent", StringComparison.OrdinalIgnoreCase);

    /// <summary>The curve and slice settings, with the engine's defaults for anything not saved.</summary>
    internal sealed record Curve(
        Vector3 StartPos, Vector3 StartTangent, Vector3 EndPos, Vector3 EndTangent,
        Vector2 StartScale, Vector2 EndScale, float StartRoll, float EndRoll, Vector2 StartOffset, Vector2 EndOffset,
        Vector3 Up, int ForwardAxis, bool SmoothRollScale, float BoundaryMin, float BoundaryMax)
    {
        public Vector3 Position(float a)
        {
            float a2 = a * a, a3 = a2 * a;
            return ((2 * a3) - (3 * a2) + 1) * StartPos + (a3 - (2 * a2) + a) * StartTangent
                   + ((-2 * a3) + (3 * a2)) * EndPos + (a3 - a2) * EndTangent;
        }

        public Vector3 Direction(float a)
        {
            var a2 = a * a;
            var d = ((6 * a2) - (6 * a)) * StartPos + ((3 * a2) - (4 * a) + 1) * StartTangent
                    + ((-6 * a2) + (6 * a)) * EndPos + ((3 * a2) - (2 * a)) * EndTangent;
            return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.UnitX;
        }
    }

    public static Curve Read(UObject component)
    {
        var p = Props.Get<FStructFallback?>(component, "SplineParams", null);
        Vector3 V(string name, Vector3 fallback) => p is not null && p.TryGetValue(out FVector v, name) ? new Vector3(v.X, v.Y, v.Z) : fallback;
        Vector2 V2(string name, Vector2 fallback) => p is not null && p.TryGetValue(out FVector2D v, name) ? new Vector2(v.X, v.Y) : fallback;
        float F(string name) => p is not null && p.TryGetValue(out float f, name) ? f : 0f;
        var up = Props.TryGet(component, "SplineUpDir", out FVector u) ? new Vector3(u.X, u.Y, u.Z) : Vector3.UnitZ;
        var axisText = Props.EnumText([component], "ForwardAxis") ?? "X";
        var axis = axisText.EndsWith('Y') ? 1 : axisText.EndsWith('Z') ? 2 : 0;
        return new Curve(
            V("StartPos", Vector3.Zero), V("StartTangent", Vector3.Zero), V("EndPos", Vector3.Zero), V("EndTangent", Vector3.Zero),
            V2("StartScale", Vector2.One), V2("EndScale", Vector2.One), F("StartRoll"), F("EndRoll"),
            V2("StartOffset", Vector2.Zero), V2("EndOffset", Vector2.Zero),
            up.LengthSquared() > 1e-8f ? Vector3.Normalize(up) : Vector3.UnitZ, axis,
            Props.Get(component, "bSmoothInterpRollScale", false),
            Props.Get(component, "SplineBoundaryMin", 0f), Props.Get(component, "SplineBoundaryMax", 0f));
    }

    /// <summary>Bounds of the bent mesh (component space, cm): the curve sampled, padded by the mesh's widest cross-section.</summary>
    public static MeshInfo Describe(Curve curve, MeshInfo straight)
    {
        var half = (straight.BoundsMax - straight.BoundsMin) / 2;
        var scale = MathF.Max(MathF.Max(curve.StartScale.X, curve.StartScale.Y), MathF.Max(curve.EndScale.X, curve.EndScale.Y));
        var pad = new Vector3(half.Length() * MathF.Max(1f, scale));
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < CurveSamples; i++)
        {
            var point = curve.Position(i / (float)(CurveSamples - 1));
            min = Vector3.Min(min, point - pad);
            max = Vector3.Max(max, point + pad);
        }
        return new MeshInfo(straight.Materials, min, max);
    }

    /// <summary>The vertex bend for one component, given the straight mesh's bounds.</summary>
    public static MeshBaker.Deform Bender(Curve curve, MeshInfo straight)
    {
        var axis = curve.ForwardAxis;
        float lo, hi;
        if (curve.BoundaryMax != curve.BoundaryMin) { lo = curve.BoundaryMin; hi = curve.BoundaryMax; }
        else { lo = Component(straight.BoundsMin, axis); hi = Component(straight.BoundsMax, axis); }
        var length = MathF.Abs(hi - lo) < 1e-4f ? 1f : hi - lo;

        return (position, normal) =>
        {
            var alpha = (Component(position, axis) - lo) / length;
            var t = curve.SmoothRollScale ? alpha * alpha * (3 - (2 * alpha)) : alpha;
            var dir = curve.Direction(alpha);
            var baseX = Vector3.Cross(curve.Up, dir);
            baseX = baseX.LengthSquared() > 1e-8f ? Vector3.Normalize(baseX) : Vector3.UnitY;
            var baseY = Vector3.Normalize(Vector3.Cross(dir, baseX));
            var offset = Vector2.Lerp(curve.StartOffset, curve.EndOffset, t);
            var origin = curve.Position(alpha) + (offset.X * baseX) + (offset.Y * baseY);
            var roll = curve.StartRoll + ((curve.EndRoll - curve.StartRoll) * t);
            float cos = MathF.Cos(roll), sin = MathF.Sin(roll);
            var xVec = (cos * baseX) - (sin * baseY);
            var yVec = (cos * baseY) + (sin * baseX);
            var scale = Vector2.Lerp(curve.StartScale, curve.EndScale, t);

            // Engine slice frames per forward axis: X -> (dir, xVec, yVec) scaled (1, s.X, s.Y);
            // Y -> (yVec, dir, xVec) scaled (s.Y, 1, s.X); Z -> (xVec, yVec, dir) scaled (s.X, s.Y, 1).
            var (ax, ay, az, sx, sy, sz) = axis switch
            {
                1 => (yVec, dir, xVec, scale.Y, 1f, scale.X),
                2 => (xVec, yVec, dir, scale.X, scale.Y, 1f),
                _ => (dir, xVec, yVec, 1f, scale.X, scale.Y),
            };
            var local = axis switch
            {
                1 => new Vector3(position.X, 0, position.Z),
                2 => new Vector3(position.X, position.Y, 0),
                _ => new Vector3(0, position.Y, position.Z),
            };
            var bent = origin + (ax * local.X * sx) + (ay * local.Y * sy) + (az * local.Z * sz);
            var turned = (ax * normal.X) + (ay * normal.Y) + (az * normal.Z);
            return (bent, turned.LengthSquared() > 1e-8f ? Vector3.Normalize(turned) : normal);
        };
    }

    private static float Component(Vector3 v, int axis) => axis switch { 1 => v.Y, 2 => v.Z, _ => v.X };
}
