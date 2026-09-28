using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - placed-object (DeployedObjectMap) transform edits.
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Rewrites the <c>Translation</c> and/or <c>Rotation</c> members of a <c>DeployedObjectMap</c>
    /// entry's <c>Transform_</c> struct in place; <c>Scale3D</c> and every other byte of the save are
    /// left alone. Returns false, changing nothing, when the entry or the member to change is not
    /// present (the game delta-serializes, and creating a Transform member from nothing has no
    /// verified serialized shape, so this method never creates one).
    /// </summary>
    /// <remarks>
    /// Proven by <c>PlacedObjectBuildingTests</c> against the fixture worlds: writing the value
    /// just read reproduces the file byte for byte, and a changed value re-reads equal with the
    /// rest of the file unchanged. In-game behaviour (does the actor appear at the new spot, and does
    /// the streamed level accept it) is NOT verified; see docs/reference/research/base-building-coordinate-spaces.md.
    /// </remarks>
    public static bool ApplyPlacedObjectTransform(
        WorldSaveData data, string key, PlacedVector? translation, PlacedQuaternion? rotation)
    {
        if (WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", key) is not { } props)
        {
            return false;
        }
        return ApplyTransformToProps(props, translation, rotation);
    }

    /// <summary>
    /// The same in-place rewrite as <see cref="ApplyPlacedObjectTransform"/>, on an entry's property list
    /// (used both for live entries and for a detached copy being built).
    /// </summary>
    internal static bool ApplyTransformToProps(
        IList<FPropertyTag> props, PlacedVector? translation, PlacedQuaternion? rotation)
    {
        if (props.FindByPrefix("Transform_")?.Property is not StructProperty tsp || tsp.Value is not PropertiesStruct tps)
        {
            return false;
        }

        VectorStruct? vec = null;
        QuatStruct? quat = null;
        if (translation is not null)
        {
            if (tps.Properties.FindByPrefix("Translation")?.Property is not StructProperty trsp
                || trsp.Value is not VectorStruct v)
            {
                return false;
            }
            vec = v;
        }
        if (rotation is not null)
        {
            if (tps.Properties.FindByPrefix("Rotation")?.Property is not StructProperty rsp
                || rsp.Value is not QuatStruct q)
            {
                return false;
            }
            quat = q;
        }

        if (vec is not null && translation is { } t)
        {
            var fv = vec.Value;
            fv.X = t.X;
            fv.Y = t.Y;
            fv.Z = t.Z;
            vec.Value = fv;
        }
        if (quat is not null && rotation is { } r)
        {
            var fq = quat.Value;
            fq.X = r.X;
            fq.Y = r.Y;
            fq.Z = r.Z;
            fq.W = r.W;
            quat.Value = fq;
        }
        return true;
    }
}
