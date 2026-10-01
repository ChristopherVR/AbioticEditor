using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Component.Landscape;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>One drawn mesh instance in a level, in Unreal world space.</summary>
/// <param name="Mesh">Index into <see cref="LevelIndexData.Meshes"/>.</param>
/// <param name="Overrides">Index into <see cref="LevelIndexData.OverrideSets"/>.</param>
/// <param name="Actor">Index into <see cref="LevelIndexData.Actors"/>.</param>
/// <param name="World">World matrix (Unreal row-vector convention).</param>
/// <param name="Centre">World-space centre of the instance's bounding sphere (cm).</param>
/// <param name="Radius">Bounding sphere radius (cm); 0 until the mesh's bounds are known.</param>
internal readonly record struct LevelEntry(int Mesh, int Overrides, int Actor, Matrix4x4 World, Vector3 Centre, float Radius);

/// <summary>Every static mesh instance a level map draws, with string tables shared by the entries.</summary>
internal sealed class LevelIndexData
{
    public required string Map { get; init; }
    public required List<string> Meshes { get; init; }
    public required List<string?[]> OverrideSets { get; init; }
    public required List<string> Actors { get; init; }
    public required List<LevelEntry> Entries { get; init; }

    /// <summary>World-space box around every entry's bounding sphere (cm).</summary>
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    public void ComputeBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var e in Entries)
        {
            min = Vector3.Min(min, e.Centre - new Vector3(e.Radius));
            max = Vector3.Max(max, e.Centre + new Vector3(e.Radius));
        }
        (Min, Max) = Entries.Count == 0 ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }
}

/// <summary>
/// Reads the static geometry out of a cooked level: every visible static mesh component of every
/// actor (plain mesh actors and the parts of placed blueprint actors alike), plus instanced mesh
/// instances, with world transforms composed through the attachment hierarchy.
/// </summary>
/// <remarks>
/// Components of blueprint actors placed in a level are saved as differences from their
/// construction-script templates, so every property falls back to the component's archetype (see
/// <see cref="Props"/>); a plain read would lose most of their meshes. Brush geometry and
/// far-distance LOD proxies are skipped; landscape terrain and spline meshes (which bend at run
/// time) are indexed per component (see <see cref="LandscapeBaker"/> and <see cref="SplineBaker"/>).
/// </remarks>
internal static class LevelIndex
{
    private const uint Magic = 0x3149_4C41; // "ALI1"

    /// <summary>The engine's 1 m square plane (normal +Z), used to draw decals.</summary>
    public const string DecalPlane = "/Engine/BasicShapes/Plane.Plane";
    private const float PlaneSizeCm = 100f;

    /// <summary>The engine's default <c>DecalSize</c> (half size, cm) for a decal that keeps it.</summary>
    public static readonly FVector DefaultDecalSize = new(128, 256, 256);

    /// <summary>
    /// The engine plane placed over a decal's projection box, in the decal component's space:
    /// plane X becomes decal Z and plane Y decal -Y (Unreal's decal UVs: U along +Z, V along -Y),
    /// plane normal Z becomes decal X. A rotation, not a mirror, so instanced lighting falls on
    /// the visible side. <paramref name="size"/> is <c>DecalSize</c>, the box's half size.
    /// </summary>
    public static Matrix4x4 DecalQuad(FVector size) => new(
        0, 0, 2 * size.Z / PlaneSizeCm, 0,
        0, -2 * size.Y / PlaneSizeCm, 0, 0,
        1, 0, 0, 0,
        0, 0, 0, 1);
    public const int FormatVersion = 9; // 4: landscape terrain; 5: spline meshes; 6: absolute component transforms; 7-8: decals; 9: posed skeletal meshes

    public static LevelIndexData Build(IFileProvider provider, string mapPackage)
    {
        var data = new LevelIndexData { Map = mapPackage, Meshes = [], OverrideSets = [], Actors = [], Entries = [] };
        if (!provider.TryLoadPackage(mapPackage, out var package)) return data;
        var world = package.GetExports().OfType<UWorld>().FirstOrDefault();
        var mapObjectPath = world?.GetPathName() ?? mapPackage;
        var level = world?.PersistentLevel.Load<ULevel>();
        if (level is null) return data;

        var meshIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var overrideIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern<T>(Dictionary<string, int> ids, List<T> table, string key, T value)
        {
            if (!ids.TryGetValue(key, out var id)) { id = table.Count; table.Add(value); ids[key] = id; }
            return id;
        }

        var worlds = new Dictionary<UObject, Matrix4x4>(ReferenceEqualityComparer.Instance);
        Matrix4x4 WorldOf(UObject component, int guard)
        {
            if (worlds.TryGetValue(component, out var known)) return known;
            var result = SceneMath.Transform(
                Props.Get(component, "RelativeLocation", FVector.ZeroVector),
                Props.Get(component, "RelativeRotation", FRotator.ZeroRotator),
                Props.Get(component, "RelativeScale3D", FVector.OneVector));
            if (guard < 32 && component.TryGetValue(out FPackageIndex parentIndex, "AttachParent")
                && parentIndex is { IsNull: false } && parentIndex.Load() is { } parent)
            {
                result = SceneMath.Attach(result, WorldOf(parent, guard + 1),
                    Props.Get(component, "bAbsoluteLocation", false), Props.Get(component, "bAbsoluteRotation", false), Props.Get(component, "bAbsoluteScale", false));
            }
            worlds[component] = result;
            return result;
        }

        bool Visible(UObject component, int guard)
        {
            if (!Props.Get(component, "bVisible", true) || Props.Get(component, "bHiddenInGame", false)) return false;
            return guard >= 32 || !component.TryGetValue(out FPackageIndex parentIndex, "AttachParent")
                   || parentIndex is not { IsNull: false } || parentIndex.Load() is not { } parent || Visible(parent, guard + 1);
        }

        var seen = new HashSet<UObject>(ReferenceEqualityComparer.Instance);
        foreach (var actorIndex in level.Actors)
        {
            if (actorIndex is not { IsNull: false } || !actorIndex.TryLoad(out UObject? actor) || actor is null) continue;
            if (Props.Get(actor, "bHidden", false)) continue;
            // Hierarchical LOD proxies are merged stand-ins the game draws only from far away;
            // near a base they would sit on top of the real walls they replace.
            if (actor.ExportType.Equals("LODActor", StringComparison.OrdinalIgnoreCase)) continue;
            var actorId = -1;
            // Landscape terrain: each component is its own mesh (see LandscapeBaker), drawn with the
            // proxy's transform because the baked vertices already include the component's offset.
            if (actor is ALandscapeProxy proxy)
            {
                var proxyMaterial = proxy.LandscapeMaterial is { IsNull: false } lm ? lm.ResolvedObject?.GetPathName() : null;
                foreach (var componentIndex in proxy.LandscapeComponents)
                {
                    if (componentIndex is not { IsNull: false, IsExport: true } || componentIndex.Load<ULandscapeComponent>() is not { } land) continue;
                    if (!seen.Add(land) || !Visible(land, 0)) continue;
                    var material = land.OverrideMaterial is { IsNull: false } om ? om.ResolvedObject?.GetPathName() : proxyMaterial;
                    var key = LandscapeBaker.Key(mapObjectPath, componentIndex.Index - 1);
                    var meshId = Intern(meshIds, data.Meshes, key, key);
                    var overrideId = Intern(overrideIds, data.OverrideSets, material ?? "", [material]);
                    if (actorId < 0) { actorId = data.Actors.Count; data.Actors.Add(actor.Name); }
                    var proxyWorld = land.TryGetValue(out FPackageIndex parentIndex, "AttachParent") && parentIndex is { IsNull: false } && parentIndex.Load() is { } parent
                        ? WorldOf(parent, 0)
                        : Matrix4x4.Identity;
                    data.Entries.Add(new LevelEntry(meshId, overrideId, actorId, proxyWorld, proxyWorld.Translation, 0));
                }
                continue;
            }
            foreach (var (componentIndex, component) in ComponentsOf(actor))
            {
                if (!seen.Add(component)) continue;
                // Decals: a flat quad over the decal's projection box (DecalSize is its half size;
                // it projects along its own X), wearing the decal material. Drawn with the engine's
                // 1 m plane, turned so the plane's normal is the decal's X and sized to its Y and Z.
                if (component.ExportType.Contains("DecalComponent", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Visible(component, 0) || Props.Get<FPackageIndex?>(component, "DecalMaterial", null) is not { IsNull: false } decalMaterial
                        || decalMaterial.ResolvedObject?.GetPathName() is not { Length: > 0 } decalPath) continue;
                    var quad = DecalQuad(Props.Get(component, "DecalSize", DefaultDecalSize));
                    var decalMesh = Intern(meshIds, data.Meshes, DecalPlane, DecalPlane);
                    var decalOverride = Intern(overrideIds, data.OverrideSets, decalPath, [decalPath]);
                    if (actorId < 0) { actorId = data.Actors.Count; data.Actors.Add(actor.Name); }
                    var decalAt = quad * WorldOf(component, 0);
                    data.Entries.Add(new LevelEntry(decalMesh, decalOverride, actorId, decalAt, decalAt.Translation, 0));
                    continue;
                }
                // Skeletal meshes posed by an animation (their own or their leader's): skinned per
                // component (PoseBaker); the rest are drawn in their reference pose below.
                if (PoseBaker.IsSkeletalComponent(component.ExportType) && componentIndex.IsExport && Visible(component, 0)
                    && ClassModelResolver.TryMesh([component], out _) && PoseBaker.PoseOf(component) is not null)
                {
                    var poseOverrides = Props.Get(component, "OverrideMaterials", Array.Empty<FPackageIndex?>())
                        .Select(m => m is { IsNull: false } ? m.ResolvedObject?.GetPathName() : null)
                        .ToArray();
                    var poseKey = PoseBaker.Key(mapObjectPath, componentIndex.Index - 1);
                    var poseMesh = Intern(meshIds, data.Meshes, poseKey, poseKey);
                    var poseOverride = Intern(overrideIds, data.OverrideSets, string.Join('|', poseOverrides), poseOverrides);
                    if (actorId < 0) { actorId = data.Actors.Count; data.Actors.Add(actor.Name); }
                    var posedAt = WorldOf(component, 0);
                    data.Entries.Add(new LevelEntry(poseMesh, poseOverride, actorId, posedAt, posedAt.Translation, 0));
                    continue;
                }
                // Spline meshes bend their mesh along a curve: one mesh per component (SplineBaker),
                // drawn with the component's own transform.
                if (SplineBaker.IsSplineMesh(component.ExportType))
                {
                    if (!componentIndex.IsExport || !Visible(component, 0) || !ClassModelResolver.TryMesh([component], out _)) continue;
                    var splineOverrides = Props.Get(component, "OverrideMaterials", Array.Empty<FPackageIndex?>())
                        .Select(m => m is { IsNull: false } ? m.ResolvedObject?.GetPathName() : null)
                        .ToArray();
                    var splineKey = SplineBaker.Key(mapObjectPath, componentIndex.Index - 1);
                    var splineMesh = Intern(meshIds, data.Meshes, splineKey, splineKey);
                    var splineOverride = Intern(overrideIds, data.OverrideSets, string.Join('|', splineOverrides), splineOverrides);
                    if (actorId < 0) { actorId = data.Actors.Count; data.Actors.Add(actor.Name); }
                    var at = WorldOf(component, 0);
                    data.Entries.Add(new LevelEntry(splineMesh, splineOverride, actorId, at, at.Translation, 0));
                    continue;
                }
                if (!ClassModelResolver.IsMeshComponent(component.ExportType) || !Visible(component, 0)) continue;
                if (!ClassModelResolver.TryMesh([component], out var mesh)) continue;
                var overrides = Props.Get(component, "OverrideMaterials", Array.Empty<FPackageIndex?>())
                    .Select(m => m is { IsNull: false } ? m.ResolvedObject?.GetPathName() : null)
                    .ToArray();
                var meshId = Intern(meshIds, data.Meshes, mesh, mesh);
                var overrideId = Intern(overrideIds, data.OverrideSets, string.Join('|', overrides), overrides);
                if (actorId < 0) { actorId = data.Actors.Count; data.Actors.Add(actor.Name); }
                var placed = WorldOf(component, 0);
                if (component is UInstancedStaticMeshComponent ism)
                {
                    foreach (var instance in ism.GetInstances())
                    {
                        var m = SceneMath.Transform(instance.TransformData) * placed;
                        data.Entries.Add(new LevelEntry(meshId, overrideId, actorId, m, m.Translation, 0));
                    }
                }
                else
                {
                    data.Entries.Add(new LevelEntry(meshId, overrideId, actorId, placed, placed.Translation, 0));
                }
            }
        }
        return data;
    }

    /// <summary>
    /// Fills in each entry's bounding sphere from its mesh's bounds (looked up once per mesh), so a
    /// query finds big pieces (cliffs, floor slabs) whose origin is far from the base standing in them.
    /// </summary>
    public static void ApplyBounds(LevelIndexData data, Func<string, MeshInfo?> meshInfo)
    {
        var infos = data.Meshes.Select(meshInfo).ToArray();
        for (var i = 0; i < data.Entries.Count; i++)
        {
            var e = data.Entries[i];
            if (infos[e.Mesh] is not { } info) continue;
            var localCentre = (info.BoundsMin + info.BoundsMax) / 2;
            var halfDiagonal = (info.BoundsMax - info.BoundsMin).Length() / 2;
            var scale = MathF.Max(
                new Vector3(e.World.M11, e.World.M12, e.World.M13).Length(),
                MathF.Max(new Vector3(e.World.M21, e.World.M22, e.World.M23).Length(), new Vector3(e.World.M31, e.World.M32, e.World.M33).Length()));
            data.Entries[i] = e with { Centre = Vector3.Transform(localCentre, e.World), Radius = halfDiagonal * scale };
        }
        data.ComputeBounds();
    }

    private static IEnumerable<(FPackageIndex Index, UObject Component)> ComponentsOf(UObject actor)
    {
        var indices = new List<FPackageIndex?> { actor.GetOrDefault<FPackageIndex?>("RootComponent") };
        indices.AddRange(actor.GetOrDefault<FPackageIndex?[]>("InstanceComponents", []));
        indices.AddRange(actor.GetOrDefault<FPackageIndex?[]>("BlueprintCreatedComponents", []));
        foreach (var index in indices)
        {
            if (index is { IsNull: false } && index.TryLoad(out UObject? component) && component is not null)
                yield return (index, component);
        }
    }

    // ---- compact on-disk form ----------------------------------------------------------------

    public static void Save(string path, LevelIndexData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = File.Create(temp))
        using (var w = new BinaryWriter(stream))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(data.Map);
            WriteVector(w, data.Min);
            WriteVector(w, data.Max);
            w.Write(data.Meshes.Count);
            foreach (var m in data.Meshes) w.Write(m);
            w.Write(data.OverrideSets.Count);
            foreach (var set in data.OverrideSets)
            {
                w.Write(set.Length);
                foreach (var o in set) w.Write(o ?? string.Empty);
            }
            w.Write(data.Actors.Count);
            foreach (var a in data.Actors) w.Write(a);
            w.Write(data.Entries.Count);
            foreach (var e in data.Entries)
            {
                w.Write(e.Mesh);
                w.Write(e.Overrides);
                w.Write(e.Actor);
                var m = e.World;
                foreach (var f in new[] { m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43 }) w.Write(f);
                WriteVector(w, e.Centre);
                w.Write(e.Radius);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    public static LevelIndexData? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion) return null;
            var map = r.ReadString();
            var min = ReadVector(r);
            var max = ReadVector(r);
            var meshes = new List<string>(r.ReadInt32());
            for (var i = meshes.Capacity; i > 0; i--) meshes.Add(r.ReadString());
            var sets = new List<string?[]>(r.ReadInt32());
            for (var i = sets.Capacity; i > 0; i--)
            {
                var set = new string?[r.ReadInt32()];
                for (var j = 0; j < set.Length; j++) set[j] = r.ReadString() is { Length: > 0 } s ? s : null;
                sets.Add(set);
            }
            var actors = new List<string>(r.ReadInt32());
            for (var i = actors.Capacity; i > 0; i--) actors.Add(r.ReadString());
            var entries = new List<LevelEntry>(r.ReadInt32());
            for (var i = entries.Capacity; i > 0; i--)
            {
                var mesh = r.ReadInt32();
                var overrides = r.ReadInt32();
                var actor = r.ReadInt32();
                var f = new float[12];
                for (var j = 0; j < 12; j++) f[j] = r.ReadSingle();
                var m = new Matrix4x4(f[0], f[1], f[2], 0, f[3], f[4], f[5], 0, f[6], f[7], f[8], 0, f[9], f[10], f[11], 1);
                entries.Add(new LevelEntry(mesh, overrides, actor, m, ReadVector(r), r.ReadSingle()));
            }
            return new LevelIndexData { Map = map, Meshes = meshes, OverrideSets = sets, Actors = actors, Entries = entries, Min = min, Max = max };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static void WriteVector(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    private static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
