namespace AbioticEditor.Core.WorldSaves;

/// <summary>A 3-component vector as stored in a save (Unreal units: centimetres for locations).</summary>
public readonly record struct PlacedVector(double X, double Y, double Z);

/// <summary>A rotation quaternion exactly as stored (X, Y, Z, W; no normalisation applied).</summary>
public readonly record struct PlacedQuaternion(double X, double Y, double Z, double W)
{
    /// <summary>The identity rotation.</summary>
    public static PlacedQuaternion Identity { get; } = new(0, 0, 0, 1);

    /// <summary>
    /// Yaw about the vertical (Z) axis in degrees, using the Unreal quaternion-to-rotator
    /// convention. Display only: the save keeps the quaternion, which is what a writer must keep.
    /// </summary>
    public double YawDegrees => Math.Atan2(2 * ((W * Z) + (X * Y)), 1 - (2 * ((Y * Y) + (Z * Z)))) * 180.0 / Math.PI;

    /// <summary>Pitch in degrees (Unreal convention), display only.</summary>
    public double PitchDegrees
    {
        get
        {
            var s = 2 * ((W * Y) - (Z * X));
            return Math.Asin(Math.Clamp(s, -1, 1)) * 180.0 / Math.PI;
        }
    }

    /// <summary>Roll in degrees (Unreal convention), display only.</summary>
    public double RollDegrees => Math.Atan2(2 * ((W * X) + (Y * Z)), 1 - (2 * ((X * X) + (Y * Y)))) * 180.0 / Math.PI;
}

/// <summary>
/// The <c>Transform_</c> struct of a placed object: <c>Translation</c>, <c>Rotation</c> (quaternion)
/// and <c>Scale3D</c>, each nullable because the game delta-serializes default-valued members.
/// </summary>
public sealed record PlacedObjectTransform(
    PlacedVector? Translation,
    PlacedQuaternion? Rotation,
    PlacedVector? Scale3D)
{
    /// <summary>Translation with the Unreal default (0,0,0) for an omitted member.</summary>
    public PlacedVector EffectiveTranslation => Translation ?? new PlacedVector(0, 0, 0);

    /// <summary>Rotation with the Unreal default (identity) for an omitted member.</summary>
    public PlacedQuaternion EffectiveRotation => Rotation ?? PlacedQuaternion.Identity;

    /// <summary>Scale with the Unreal default (1,1,1) for an omitted member.</summary>
    public PlacedVector EffectiveScale => Scale3D ?? new PlacedVector(1, 1, 1);
}
