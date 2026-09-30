using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>One mesh a class draws, in the actor's own Unreal space.</summary>
/// <param name="Mesh">The mesh asset's object path (<c>/Game/...SM_X.SM_X</c>).</param>
/// <param name="Local">Part-to-actor transform (Unreal space, row-vector).</param>
/// <param name="MaterialOverrides">Per-slot material overrides the component sets (null keeps the mesh's own).</param>
/// <param name="Name">The component's variable name.</param>
internal sealed record ResolvedPart(string Mesh, Matrix4x4 Local, IReadOnlyList<string?> MaterialOverrides, string Name);

/// <summary>
/// Works out which meshes a blueprint class draws, the way the game builds the actor: every
/// ancestor's construction script (SCS) nodes, root first, with each class's component overrides
/// (the <c>InheritableComponentHandler</c>) replacing the inherited templates, most-derived last.
/// </summary>
/// <remarks>
/// Nothing is keyed on concrete class names: the walk follows the class's own parent chain, so
/// classes added in a game update resolve the same way. Overrides are delta-serialized against
/// the template they replace, so a property is read from the most-derived template that stores it
/// (see <see cref="Props"/>).
/// </remarks>
internal static class ClassModelResolver
{
    private const int MaxChildActorDepth = 2;

    private sealed class Node(string name, string? parent)
    {
        public string Name { get; } = name;
        public string? Parent { get; set; } = parent;

        /// <summary>Templates, most-derived first.</summary>
        public List<UObject> Templates { get; } = [];
    }

    public static IReadOnlyList<ResolvedPart> Resolve(IFileProvider provider, string classPath)
        => provider.TryLoadPackageObject(classPath, out var obj) && obj is UStruct cls
            ? Resolve(cls, 0)
            : [];

    private static List<ResolvedPart> Resolve(UStruct cls, int depth)
    {
        var chain = new List<UBlueprintGeneratedClass>();
        for (UStruct? c = cls; c is UBlueprintGeneratedClass bp && chain.Count < 16; c = c.SuperStruct?.Load<UStruct>())
        {
            chain.Add(bp);
        }

        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        string? rootName = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var scs = chain[i].GetOrDefault<FPackageIndex?>("SimpleConstructionScript")?.Load<USimpleConstructionScript>();
            if (scs is null) continue;
            foreach (var index in scs.AllNodes)
            {
                if (index?.Load<USCS_Node>() is not { } scsNode || scsNode.ComponentTemplate?.Load() is not { } template) continue;
                var name = scsNode.InternalVariableName.Text;
                var parent = scsNode.GetOrDefault<FName>("ParentComponentOrVariableName").Text;
                var node = new Node(name, parent is "None" or "" ? null : parent);
                node.Templates.Add(template);
                nodes[name] = node;
            }
            rootName ??= RootNodeName(scs);
        }

        // Component overrides: apply base-most first so the most-derived ends up at the front.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var handler = chain[i].GetOrDefault<FPackageIndex?>("InheritableComponentHandler")?.Load();
            if (handler is null) continue;
            foreach (var record in handler.GetOrDefault<FStructFallback[]>("Records", []))
            {
                var template = record.GetOrDefault<FPackageIndex?>("ComponentTemplate")?.Load();
                var key = record.GetOrDefault<FStructFallback?>("ComponentKey")?.GetOrDefault<FName>("SCSVariableName").Text;
                if (template is null || string.IsNullOrEmpty(key) || !nodes.TryGetValue(key, out var node)) continue;
                node.Templates.Insert(0, template);
            }
        }

        var locals = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        Matrix4x4 LocalOf(Node node, int guard)
        {
            if (locals.TryGetValue(node.Name, out var known)) return known;
            Matrix4x4 result;
            if (string.Equals(node.Name, rootName, StringComparison.OrdinalIgnoreCase) || guard > 32)
            {
                // The actor's saved transform IS the root component's world transform.
                result = Matrix4x4.Identity;
            }
            else
            {
                var relative = SceneMath.Transform(
                    Props.Get(node.Templates, "RelativeLocation", FVector.ZeroVector),
                    Props.Get(node.Templates, "RelativeRotation", FRotator.ZeroRotator),
                    Props.Get(node.Templates, "RelativeScale3D", FVector.OneVector));
                var parent = node.Parent is not null && nodes.TryGetValue(node.Parent, out var p) ? LocalOf(p, guard + 1) : Matrix4x4.Identity;
                result = relative * parent;
            }
            locals[node.Name] = result;
            return result;
        }

        bool Visible(Node node, int guard)
        {
            if (!Props.Get(node.Templates, "bVisible", true) || Props.Get(node.Templates, "bHiddenInGame", false)) return false;
            return guard > 32 || node.Parent is null || !nodes.TryGetValue(node.Parent, out var parent) || Visible(parent, guard + 1);
        }

        var parts = new List<ResolvedPart>();
        foreach (var node in nodes.Values)
        {
            var type = node.Templates[0].ExportType;
            if (type.Contains("ChildActor", StringComparison.OrdinalIgnoreCase))
            {
                if (depth >= MaxChildActorDepth || !Visible(node, 0)) continue;
                if (Props.TryGet(node.Templates, "ChildActorClass", out FPackageIndex childClass) && childClass.Load<UStruct>() is { } child)
                {
                    var at = LocalOf(node, 0);
                    parts.AddRange(Resolve(child, depth + 1).Select(p => p with { Local = p.Local * at, Name = $"{node.Name}/{p.Name}" }));
                }
                continue;
            }
            if (!IsMeshComponent(type) || !Visible(node, 0)) continue;
            if (!TryMesh(node.Templates, out var mesh)) continue;
            var overrides = Props.Get(node.Templates, "OverrideMaterials", Array.Empty<FPackageIndex?>())
                .Select(m => m is { IsNull: false } ? m.ResolvedObject?.GetPathName() : null)
                .ToArray();
            parts.Add(new ResolvedPart(mesh, LocalOf(node, 0), overrides, node.Name));
        }
        return parts;
    }

    /// <summary>
    /// Mesh components the view can draw as-is. Spline meshes are left out: they are bent along a
    /// curve at run time, and drawing the straight source mesh would be misleading.
    /// </summary>
    internal static bool IsMeshComponent(string exportType)
        => (exportType.Contains("StaticMeshComponent", StringComparison.OrdinalIgnoreCase)
            || exportType.Contains("SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase)
            || exportType.Contains("PoseableMeshComponent", StringComparison.OrdinalIgnoreCase))
           && !exportType.Contains("Spline", StringComparison.OrdinalIgnoreCase);

    internal static bool TryMesh(IEnumerable<UObject> templates, out string path)
    {
        foreach (var name in new[] { "StaticMesh", "SkeletalMesh", "SkinnedAsset" })
        {
            if (Props.TryGet(templates, name, out FPackageIndex? index) && index is { IsNull: false }
                && index.ResolvedObject?.GetPathName() is { Length: > 0 } p)
            {
                path = p;
                return true;
            }
        }
        path = string.Empty;
        return false;
    }

    private static string? RootNodeName(USimpleConstructionScript scs)
    {
        var root = scs.DefaultSceneRootNode?.Load<USCS_Node>() ?? scs.RootNodes.FirstOrDefault()?.Load<USCS_Node>();
        return root?.InternalVariableName.Text;
    }
}
