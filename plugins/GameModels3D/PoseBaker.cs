using System.Globalization;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Animations;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Skeletal meshes placed in a level in a pose (the game's corpses and posed people): the
/// component plays one animation (<c>AnimationData.AnimToPlay</c>, at <c>SavedPosition</c>), or
/// copies the pose of a leader component that does (<c>LeaderPoseComponent</c>, the modular head,
/// torso and legs of one body). The mesh is skinned on the CPU with that pose: each bone's local
/// transform comes from the animation's track for the bone of the same name (the reference pose
/// where there is none), and each vertex moves with its bone weights. Key:
/// <c>&lt;map object path&gt;#pose=&lt;component export index&gt;</c>.
/// </summary>
internal static class PoseBaker
{
    public const string Marker = "#pose=";

    public static string Key(string mapObjectPath, int exportIndex)
        => mapObjectPath + Marker + exportIndex.ToString(CultureInfo.InvariantCulture);

    public static bool IsKey(string mesh) => mesh.Contains(Marker, StringComparison.Ordinal);

    public static bool IsSkeletalComponent(string exportType) => exportType.Contains("SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase);

    /// <summary>The component a key names, or null when it is not a skeletal mesh component.</summary>
    public static UObject? Load(IFileProvider provider, string key)
    {
        var at = key.IndexOf(Marker, StringComparison.Ordinal);
        if (at <= 0 || !int.TryParse(key.AsSpan(at + Marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return null;
        var objectPath = key[..at];
        var dot = objectPath.LastIndexOf('.');
        var package = dot > 0 ? objectPath[..dot] : objectPath;
        if (!provider.TryLoadPackage(package, out var pkg)) return null;
        return pkg.GetExport(index) is { } component && IsSkeletalComponent(component.ExportType) ? component : null;
    }

    /// <summary>The animation and time a component is posed with (its own, else its leader's), or null when it is not posed.</summary>
    public static (UAnimSequence Anim, float Time)? PoseOf(UObject component)
    {
        for (var (current, depth) = (component, 0); current is not null && depth < 4; depth++)
        {
            var data = Props.Get<FStructFallback?>(current, "AnimationData", null);
            if (data?.GetOrDefault<FPackageIndex?>("AnimToPlay") is { IsNull: false } animIndex && animIndex.Load() is UAnimSequence anim)
                return (anim, data.GetOrDefault("SavedPosition", 0f));
            current = (Props.Get<FPackageIndex?>(current, "LeaderPoseComponent", null) ?? Props.Get<FPackageIndex?>(current, "MasterPoseComponent", null))?.Load();
        }
        return null;
    }

    /// <summary>The skinned mesh's material slots and a sphere (cm, centred on the origin) that holds it in any pose about its root.</summary>
    public static MeshInfo Describe(MeshInfo restPose)
    {
        var reach = 0f;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? restPose.BoundsMin.X : restPose.BoundsMax.X,
                (i & 2) == 0 ? restPose.BoundsMin.Y : restPose.BoundsMax.Y, (i & 4) == 0 ? restPose.BoundsMin.Z : restPose.BoundsMax.Z);
            reach = MathF.Max(reach, corner.Length());
        }
        return new MeshInfo(restPose.Materials, new Vector3(-reach), new Vector3(reach));
    }

    /// <summary>Bakes the component's mesh in its pose, or null when it has no mesh or pose.</summary>
    public static byte[]? Bake(UObject component, int lod)
    {
        if (!ClassModelResolver.TryMesh([component], out var meshPath)) return null;
        var meshIndex = Props.Get<FPackageIndex?>(component, "SkeletalMesh", null) ?? Props.Get<FPackageIndex?>(component, "SkinnedAsset", null);
        if (meshIndex?.Load() is not USkeletalMesh mesh) return null;
        Matrix4x4[]? skin = null;
        try
        {
            if (PoseOf(component) is { } pose) skin = SkinMatrices(mesh, pose.Anim, pose.Time);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                       or InvalidOperationException or IndexOutOfRangeException or NotSupportedException or NotImplementedException)
        {
            // Animations compressed with ACL need CUE4Parse's native library, which the editor does
            // not ship (the security doors' open animation is one); those keep their rest pose.
            skin = null;
        }
        return skin is not null ? MeshBaker.BakeSkinned(mesh, lod, skin) : MeshBaker.Bake(mesh, lod);
    }

    /// <summary>
    /// Per mesh bone, the matrix taking a rest-pose vertex to its posed place (Unreal space):
    /// inverse(rest global) times posed global, globals built parent first.
    /// </summary>
    internal static Matrix4x4[]? SkinMatrices(USkeletalMesh mesh, UAnimSequence anim, float time)
    {
        var set = anim.ConvertAnims();
        if (set.Sequences.Count == 0) return null;
        var sequence = set.Sequences[0];
        var skeletonBones = set.Skeleton.ReferenceSkeleton.FinalNameToIndexMap;
        var bones = mesh.ReferenceSkeleton.FinalRefBoneInfo;
        var restPose = mesh.ReferenceSkeleton.FinalRefBonePose;
        var frame = Math.Clamp(time * sequence.FramesPerSecond, 0f, Math.Max(0, sequence.NumFrames - 1));

        var restGlobal = new Matrix4x4[bones.Length];
        var posedGlobal = new Matrix4x4[bones.Length];
        var skin = new Matrix4x4[bones.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            var rest = restPose[i];
            var rotation = rest.Rotation;
            var position = rest.Translation;
            var scale = rest.Scale3D;
            if (skeletonBones.TryGetValue(bones[i].Name.Text, out var trackIndex) && trackIndex < sequence.Tracks.Count)
                sequence.Tracks[trackIndex].GetBoneTransform(frame, sequence.NumFrames, ref rotation, ref position, ref scale);
            var restLocal = SceneMath.Transform(rest);
            var posedLocal = SceneMath.Transform(position, rotation, scale);
            var parent = bones[i].ParentIndex;
            restGlobal[i] = parent >= 0 && parent < i ? restLocal * restGlobal[parent] : restLocal;
            posedGlobal[i] = parent >= 0 && parent < i ? posedLocal * posedGlobal[parent] : posedLocal;
            skin[i] = Matrix4x4.Invert(restGlobal[i], out var inverse) ? inverse * posedGlobal[i] : Matrix4x4.Identity;
        }
        return skin;
    }
}
