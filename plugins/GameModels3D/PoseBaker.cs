using System.Globalization;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Animations;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Skeletal meshes placed in a level in a pose (the game's corpses and posed people): the
/// component plays one animation (<c>AnimationData.AnimToPlay</c>, at <c>SavedPosition</c>), is
/// driven by an animation blueprint (its resting animation, <see cref="AnimBlueprintPose"/>), or
/// copies the pose of a leader component that does (<c>LeaderPoseComponent</c>, the modular head,
/// torso and legs of one body). The mesh is skinned on the CPU with that pose: each bone's local
/// transform comes from the animation's track for the bone of the same name (the reference pose
/// where there is none), and each vertex moves with its bone weights. Key:
/// <c>&lt;map object path&gt;#pose=&lt;component export index&gt;</c>.
/// </summary>
/// <remarks>
/// The game's corpses are posed by their blueprint's construction script, which copies an actor
/// variable (the death pose) into the mesh's <c>AnimationData</c> at a time past the animation's
/// end, so the body rests in its last frame. A level instance often names its own death pose; when
/// that animation cannot be decoded (ACL compression without CUE4Parse's native decoder) the
/// blueprint's own default for the same variable is tried before the rest pose, so the body lies
/// down instead of standing in a T-pose (<see cref="PoseCandidates"/>).
/// </remarks>
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
        foreach (var candidate in PoseCandidates(component)) return candidate;
        return null;
    }

    /// <summary>
    /// The poses to try for a component, best first: the one it plays (its own, else its leader's),
    /// then, when that animation was copied from a variable of the owning actor (a corpse's death
    /// pose), the values the actor's blueprint class and its parents give that variable by default.
    /// </summary>
    public static IEnumerable<(UAnimSequence Anim, float Time)> PoseCandidates(UObject component)
    {
        for (var (current, depth) = (component, 0); current is not null && depth < 4; depth++)
        {
            var data = Props.Get<FStructFallback?>(current, "AnimationData", null);
            if (data?.GetOrDefault<FPackageIndex?>("AnimToPlay") is { IsNull: false } animIndex && animIndex.Load() is UAnimSequence anim)
            {
                var time = data.GetOrDefault("SavedPosition", 0f);
                yield return (anim, time);
                foreach (var fallback in ClassDefaultPoses(current, anim)) yield return (fallback, time);
                yield break;
            }
            // Driven by an animation blueprint: the animation its graph rests in (AnimBlueprintPose).
            if (Props.Get<FPackageIndex?>(current, "AnimClass", null) is { IsNull: false } animClass
                && AnimBlueprintPose.RestingAnimation(animClass) is { } resting)
            {
                yield return (resting, 0f);
                yield break;
            }
            current = (Props.Get<FPackageIndex?>(current, "LeaderPoseComponent", null) ?? Props.Get<FPackageIndex?>(current, "MasterPoseComponent", null))?.Load();
        }
    }

    /// <summary>
    /// The animations the owning actor's class (and its parents) give by default to the actor
    /// variable that holds <paramref name="anim"/>, other than <paramref name="anim"/> itself.
    /// Nothing is keyed on a variable or class name: the variable is whichever of the actor's own
    /// properties names the same animation.
    /// </summary>
    private static IEnumerable<UAnimSequence> ClassDefaultPoses(UObject component, UAnimSequence anim)
    {
        if (component.Outer?.Load() is not { } actor) yield break;
        var path = anim.GetPathName();
        var variable = actor.Properties.FirstOrDefault(t => t.Tag?.GenericValue is FPackageIndex { IsNull: false } index
                                                            && string.Equals(index.ResolvedObject?.GetPathName(), path, StringComparison.OrdinalIgnoreCase))?.Name.Text;
        if (variable is null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        var cls = actor.Class?.Load() as UStruct;
        for (var depth = 0; cls is UBlueprintGeneratedClass owner && depth < 16; depth++, cls = cls.SuperStruct?.Load<UStruct>())
        {
            if (owner.ClassDefaultObject.Load() is not { } defaults) continue;
            if (defaults.TryGetValue(out FPackageIndex value, variable) && value is { IsNull: false }
                && value.Load() is UAnimSequence fallback && seen.Add(fallback.GetPathName()))
                yield return fallback;
        }
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
    public static byte[]? Bake(UObject component, int lod) => Bake(component, lod, out _);

    /// <summary>
    /// As <see cref="Bake(UObject, int)"/>; <paramref name="exact"/> is false when the mesh is not in
    /// the pose it plays (a stand-in pose, or the rest pose because no pose could be decoded). A run
    /// with the native decoder would draw it properly, so such a bake should not be kept on disk.
    /// </summary>
    public static byte[]? Bake(UObject component, int lod, out bool exact)
    {
        exact = true;
        if (!ClassModelResolver.TryMesh([component], out _)) return null;
        var meshIndex = Props.Get<FPackageIndex?>(component, "SkeletalMesh", null) ?? Props.Get<FPackageIndex?>(component, "SkinnedAsset", null);
        if (meshIndex?.Load() is not USkeletalMesh mesh) return null;
        Matrix4x4[]? skin = null;
        var tried = 0;
        foreach (var pose in PoseCandidates(component))
        {
            tried++;
            try
            {
                skin = SkinMatrices(mesh, pose.Anim, pose.Time);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                           or InvalidOperationException or IndexOutOfRangeException or NotSupportedException or NotImplementedException)
            {
                // Animations compressed with ACL need CUE4Parse's native library (NativeDecoder,
                // shipped next to the plugin); without it the next candidate is tried.
                skin = null;
            }
            if (skin is not null) break;
        }
        exact = tried == 0 || (skin is not null && tried == 1);
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
