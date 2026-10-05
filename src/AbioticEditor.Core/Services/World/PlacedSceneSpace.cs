namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Conversion between a save's placed-object transforms and the coordinate space a right-handed,
/// Y-up viewer (such as a Three.js scene) uses. Pure math: nothing here depends on any renderer.
/// </summary>
/// <remarks>
/// <para>
/// Save space (Unreal): left-handed, Z up, X forward, Y right, units are centimetres, and the
/// rotation is a quaternion (X, Y, Z, W) stored as-is.
/// </para>
/// <para>
/// Viewer space: right-handed, Y up, units are metres. The mapping is the axis swap
/// <c>viewer = (X, Z, Y) / 100</c>. Swapping two axes is a reflection, which is exactly what turns a
/// left-handed frame into a right-handed one, so no axis needs negating for positions.
/// </para>
/// <para>
/// Rotations conjugate by that reflection. A rotation quaternion's vector part is a pseudovector,
/// which flips sign under a reflection, so the viewer quaternion is <c>(-x, -z, -y, w)</c> for the
/// save quaternion <c>(x, y, z, w)</c>. A pure yaw of +a degrees in the save (X turning toward Y)
/// is therefore a rotation of -a degrees about the viewer's +Y axis, which is what turns viewer +X
/// toward viewer +Z (the image of save Y). Both maps are involutions, so the inverse uses the same
/// swaps.
/// </para>
/// <para>
/// Scale is per axis, so it swaps the same way as a position (no sign, magnitude only).
/// </para>
/// </remarks>
public static class PlacedSceneSpace
{
    /// <summary>Centimetres per viewer unit (the viewer works in metres).</summary>
    public const double CentimetresPerUnit = 100.0;

    /// <summary>Save-space location (cm) to viewer space (m).</summary>
    public static PlacedVector ToViewer(PlacedVector saveCm)
        => new(saveCm.X / CentimetresPerUnit, saveCm.Z / CentimetresPerUnit, saveCm.Y / CentimetresPerUnit);

    /// <summary>Viewer-space location (m) back to save space (cm).</summary>
    public static PlacedVector FromViewer(PlacedVector viewer)
        => new(viewer.X * CentimetresPerUnit, viewer.Z * CentimetresPerUnit, viewer.Y * CentimetresPerUnit);

    /// <summary>Save-space rotation to viewer space (see the type remarks for the derivation).</summary>
    public static PlacedQuaternion ToViewer(PlacedQuaternion save)
        => new(-save.X, -save.Z, -save.Y, save.W);

    /// <summary>Viewer-space rotation back to save space.</summary>
    public static PlacedQuaternion FromViewer(PlacedQuaternion viewer)
        => new(-viewer.X, -viewer.Z, -viewer.Y, viewer.W);

    /// <summary>Per-axis scale to viewer space (no unit change, axes swapped like a position).</summary>
    public static PlacedVector ScaleToViewer(PlacedVector save) => new(save.X, save.Z, save.Y);

    /// <summary>
    /// The save-space quaternion of an Unreal rotator (pitch, yaw, roll in degrees), as the engine's
    /// <c>FRotator::Quaternion</c> builds it.
    /// </summary>
    public static PlacedQuaternion FromRotator(double pitch, double yaw, double roll)
    {
        var (sp, cp) = Math.SinCos(pitch * Math.PI / 360.0);
        var (sy, cy) = Math.SinCos(yaw * Math.PI / 360.0);
        var (sr, cr) = Math.SinCos(roll * Math.PI / 360.0);
        return Normalize(new PlacedQuaternion(
            (cr * sp * sy) - (sr * cp * cy),
            (-cr * sp * cy) - (sr * cp * sy),
            (cr * cp * sy) - (sr * sp * cy),
            (cr * cp * cy) + (sr * sp * sy)));
    }

    /// <summary>The unit-length form of <paramref name="q"/>; identity for a degenerate input.</summary>
    public static PlacedQuaternion Normalize(PlacedQuaternion q)
    {
        var length = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
        return length < 1e-12 ? PlacedQuaternion.Identity : new PlacedQuaternion(q.X / length, q.Y / length, q.Z / length, q.W / length);
    }

    /// <summary>Applies a quaternion to a vector (standard Hamilton rotation, no handedness assumption).</summary>
    public static PlacedVector Rotate(PlacedQuaternion q, PlacedVector v)
    {
        var (x, y, z, w) = (q.X, q.Y, q.Z, q.W);
        // t = 2 * cross(q.xyz, v); v' = v + w * t + cross(q.xyz, t)
        var tx = 2 * ((y * v.Z) - (z * v.Y));
        var ty = 2 * ((z * v.X) - (x * v.Z));
        var tz = 2 * ((x * v.Y) - (y * v.X));
        return new PlacedVector(
            v.X + (w * tx) + ((y * tz) - (z * ty)),
            v.Y + (w * ty) + ((z * tx) - (x * tz)),
            v.Z + (w * tz) + ((x * ty) - (y * tx)));
    }

    /// <summary>
    /// Composes an extra world-space yaw onto an existing rotation (the same product
    /// <see cref="StagedPlacedTransforms.RotateYawBy"/> uses), keeping any pitch and roll the piece has.
    /// </summary>
    public static PlacedQuaternion ComposeYaw(PlacedQuaternion q, double degrees)
    {
        var yaw = YawQuaternion(degrees);
        var (sz, cz) = (yaw.Z, yaw.W);
        return Normalize(new PlacedQuaternion(
            (cz * q.X) - (sz * q.Y),
            (cz * q.Y) + (sz * q.X),
            (cz * q.Z) + (sz * q.W),
            (cz * q.W) - (sz * q.Z)));
    }

    /// <summary>
    /// A rotation of <paramref name="yawDegrees"/> about the save's vertical (Z) axis, in the same
    /// sense <see cref="PlacedQuaternion.YawDegrees"/> reports.
    /// </summary>
    public static PlacedQuaternion YawQuaternion(double yawDegrees)
    {
        var half = yawDegrees * Math.PI / 360.0;
        return new PlacedQuaternion(0, 0, Math.Sin(half), Math.Cos(half));
    }
}
