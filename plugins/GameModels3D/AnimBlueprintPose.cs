using System.Collections.Concurrent;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// The resting pose of a skeletal mesh driven by an animation blueprint (<c>AnimClass</c>: story
/// characters, the Anteverse bugs, hexed trees and the body parts that follow them). The blueprint's
/// graph is not run; its default object lists the animations its player nodes hold
/// (<c>AnimGraphNode_SequencePlayer.Sequence</c>, <c>AnimGraphNode_RandomPlayer.Entries[].Sequence</c>,
/// <c>AnimGraphNode_BlendSpacePlayer.BlendSpace</c>), and the one it rests in is picked from them:
/// an animation named "Idle", else the sample of a walk or run blend space nearest standing still,
/// else the first sequence.
/// </summary>
internal static class AnimBlueprintPose
{
    private static readonly ConcurrentDictionary<string, UAnimSequence?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The animation the blueprint class <paramref name="animClass"/> rests in, or null.</summary>
    public static UAnimSequence? RestingAnimation(FPackageIndex animClass)
    {
        var path = animClass.ResolvedObject?.GetPathName();
        if (string.IsNullOrEmpty(path)) return null;
        return Cache.GetOrAdd(path, _ =>
        {
            try { return Pick(animClass); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        });
    }

    private static UAnimSequence? Pick(FPackageIndex animClass)
    {
        if (animClass.Load() is not UClass cls || cls.ClassDefaultObject.Load() is not { } cdo) return null;
        var sequences = new List<UAnimSequence>();
        var blendSpaces = new List<UObject>();
        foreach (var tag in cdo.Properties)
        {
            if (!tag.Name.Text.StartsWith("AnimGraphNode_", StringComparison.Ordinal)) continue;
            Collect(tag.Tag?.GenericValue, sequences, blendSpaces, 0);
        }
        var idle = sequences.FirstOrDefault(s => s.Name.Contains("Idle", StringComparison.OrdinalIgnoreCase)
                                                 && !s.Name.Contains("Alert", StringComparison.OrdinalIgnoreCase));
        if (idle is not null) return idle;
        foreach (var space in blendSpaces.OrderBy(b => b.Name.Contains("Walk", StringComparison.OrdinalIgnoreCase)
                                                        || b.Name.Contains("Run", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (StillSample(space) is { } still) return still;
        }
        return sequences.FirstOrDefault();
    }

    private static void Collect(object? value, List<UAnimSequence> sequences, List<UObject> blendSpaces, int depth)
    {
        if (value is null || depth > 5) return;
        switch (value)
        {
            case FPackageIndex { IsNull: false } index:
                var target = index.Load();
                if (target is UAnimSequence seq) { if (!sequences.Contains(seq)) sequences.Add(seq); }
                else if (target is not null && target.ExportType.Contains("BlendSpace", StringComparison.Ordinal)
                         && !target.ExportType.Contains("AimOffset", StringComparison.Ordinal)) blendSpaces.Add(target);
                break;
            case FScriptStruct script: Collect(script.StructType, sequences, blendSpaces, depth + 1); break;
            case FStructFallback fallback:
                foreach (var t in fallback.Properties) Collect(t.Tag?.GenericValue, sequences, blendSpaces, depth + 1);
                break;
            case UScriptArray array:
                foreach (var item in array.Properties) Collect(item.GenericValue, sequences, blendSpaces, depth + 1);
                break;
        }
    }

    /// <summary>The blend-space sample nearest zero on every axis (standing still for a walk/run space).</summary>
    private static UAnimSequence? StillSample(UObject space)
    {
        var samples = Props.Get(space, "SampleData", Array.Empty<FStructFallback>());
        UAnimSequence? best = null;
        var bestDistance = float.MaxValue;
        foreach (var sample in samples)
        {
            if (sample.GetOrDefault<FPackageIndex?>("Animation")?.Load() is not UAnimSequence anim) continue;
            var at = sample.GetOrDefault("SampleValue", FVector.ZeroVector);
            var distance = at.Size();
            if (distance < bestDistance) { bestDistance = distance; best = anim; }
        }
        return best;
    }
}
