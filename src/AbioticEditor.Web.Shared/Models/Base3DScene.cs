using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// One placed object as the 3D view draws it: already converted to viewer space (metres, Y up,
/// right-handed) by <see cref="PlacedSceneSpace"/>. The property names are the JavaScript module's
/// field names (the JS interop serializer camel-cases them).
/// </summary>
public sealed record Base3DObject(
    string Key,
    int Cat,
    double[] P,
    double[] Q,
    double[] S,
    bool Built,
    string Label,
    int Mark = 0,
    string? Cls = null,
    int? Paint = null,
    string? Variant = null)
{
    /// <summary><see cref="Mark"/>: an ordinary object.</summary>
    public const int MarkNone = 0;

    /// <summary><see cref="Mark"/>: staged for deletion (drawn red).</summary>
    public const int MarkDeleted = 1;

    /// <summary><see cref="Mark"/>: a staged copy that does not exist in the save yet (drawn cyan).</summary>
    public const int MarkCopy = 2;
}

/// <summary>A staged copy the scene should draw at its target place (a new object, not in the save).</summary>
/// <remarks><paramref name="ClassPath"/> is the source object's full class path, so the copy can be drawn with the same game model.</remarks>
public sealed record Base3DCopy(string Key, string? ClassName, PlacedObjectTransform Transform, string Label, string? ClassPath = null);

/// <summary>The view's filter state. Everything is applied in C# so it is testable and the JS side just draws.</summary>
public sealed record Base3DFilter
{
    public bool ShowPlayerBuilt { get; init; } = true;
    public bool ShowLevelPlaced { get; init; } = true;

    /// <summary>Categories switched off. Empty means every category is shown.</summary>
    public IReadOnlySet<PlacedObjectCategory> HiddenCategories { get; init; } = new HashSet<PlacedObjectCategory>();

    /// <summary>Lowest saved Z (cm) to show, or null for no lower limit.</summary>
    public double? MinZCm { get; init; }

    /// <summary>Highest saved Z (cm) to show, or null for no upper limit.</summary>
    public double? MaxZCm { get; init; }

    /// <summary>Case-insensitive text matched against name, class, key and owner. Blank matches all.</summary>
    public string? Search { get; init; }
}

/// <summary>An object whose saved location is missing: listed apart, never drawn at the origin.</summary>
/// <summary>One kind of player-built object that can be placed again: its class, label, how many there are, and the one copied from.</summary>
public sealed record Base3DPlaceKind(string ClassPath, string Label, int Count, string DonorKey);

public sealed record Base3DUnresolved(string Key, string Label, string? ClassName, bool Built, string Reason);

/// <summary>Everything the 3D view needs from a region, built from the session's saved and staged state.</summary>
public sealed class Base3DScene
{
    /// <summary>
    /// Drawable objects (staged edits applied). The first <see cref="Placed"/>.Count line up with it; any
    /// staged copies follow them (they have no census row, and the filters never hide them).
    /// </summary>
    public IReadOnlyList<Base3DObject> Objects { get; }

    /// <summary>The census rows behind <see cref="Objects"/>, same order.</summary>
    public IReadOnlyList<PlacedObjectSummary> Placed { get; }

    /// <summary>Saved Z (cm) of each drawable object, same order (for the height band).</summary>
    public IReadOnlyList<double> SavedZCm { get; }

    public IReadOnlyList<Base3DUnresolved> Unresolved { get; }

    public double MinZCm { get; }
    public double MaxZCm { get; }

    /// <summary>Number of staged copies at the end of <see cref="Objects"/>.</summary>
    public int CopyCount => Objects.Count - Placed.Count;

    private Base3DScene(
        List<Base3DObject> objects, List<PlacedObjectSummary> placed, List<double> zs,
        List<Base3DUnresolved> unresolved)
    {
        Objects = objects;
        Placed = placed;
        SavedZCm = zs;
        Unresolved = unresolved;
        MinZCm = zs.Count == 0 ? 0 : zs.Min();
        MaxZCm = zs.Count == 0 ? 0 : zs.Max();
    }

    /// <summary>Builds the scene. <paramref name="current"/> supplies each key's transform with staged edits applied.</summary>
    public static Base3DScene Build(
        IEnumerable<PlacedObjectSummary> placed, Func<string, PlacedObjectTransform?> current,
        IReadOnlySet<string>? deleted = null, IEnumerable<Base3DCopy>? copies = null)
    {
        var objects = new List<Base3DObject>();
        var rows = new List<PlacedObjectSummary>();
        var zs = new List<double>();
        var unresolved = new List<Base3DUnresolved>();
        foreach (var o in placed)
        {
            if (o.Transform?.Translation is not { } saved)
            {
                unresolved.Add(new Base3DUnresolved(o.Key, LabelOf(o), o.ClassName, o.DeployedByPlayer == true,
                    o.Transform is null ? "no saved transform" : "the save omits the location"));
                continue;
            }
            var drawn = ToViewerObject(o, current(o.Key) ?? o.Transform);
            objects.Add(deleted is not null && deleted.Contains(o.Key) ? drawn with { Mark = Base3DObject.MarkDeleted } : drawn);
            rows.Add(o);
            zs.Add(saved.Z);
        }
        foreach (var copy in copies ?? [])
        {
            objects.Add(ToViewerCopy(copy));
        }
        return new Base3DScene(objects, rows, zs, unresolved);
    }

    /// <summary>Converts one census row plus a transform (saved or staged) to the viewer's form.</summary>
    public static Base3DObject ToViewerObject(PlacedObjectSummary o, PlacedObjectTransform transform)
    {
        var p = PlacedSceneSpace.ToViewer(transform.EffectiveTranslation);
        var q = PlacedSceneSpace.ToViewer(PlacedSceneSpace.Normalize(transform.EffectiveRotation));
        var s = PlacedSceneSpace.ScaleToViewer(transform.EffectiveScale);
        var category = PlacedObjectCategoryCatalog.Classify(o.ClassName, o.InventoryCount > 0);
        return new Base3DObject(
            o.Key, (int)category,
            [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z],
            o.DeployedByPlayer == true, LabelOf(o), Cls: o.ClassPath,
            Paint: o.PaintColor is { } paint && paint != DeployablePaintCatalog.NoneValue ? paint : null,
            Variant: VariantOf(o));
    }

    /// <summary>
    /// The part of an object's model key after its class path: its paint colour and the crops
    /// growing in it (see <c>SceneModelHostService.ParseModelKey</c>), or null when it looks like
    /// any other object of its class.
    /// </summary>
    public static string? VariantOf(PlacedObjectSummary o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var text = new System.Text.StringBuilder();
        if (o.PaintColor is { } paint && paint != DeployablePaintCatalog.NoneValue)
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"#paint={paint}");
        if (o.LiquidLevel is { } liquid)
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"#liquid={Math.Max(0, liquid)}");
        if (o.LiquidLevel is > 0 && o.LiquidType is { } fluid && fluid[(fluid.LastIndexOf(':') + 1)..] is { Length: > 0 } fluidName)
            text.Append("#fluid=").Append(fluidName);
        if (o.Crops is { Count: > 0 } crops)
        {
            text.Append("#crops=").AppendJoin(',', crops.OrderBy(c => c.Spot)
                .Select(c => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{c.Spot}.{c.Row}.{c.Stage}")));
        }
        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>A staged copy in the viewer's form (always player-built, marked as a copy).</summary>
    public static Base3DObject ToViewerCopy(Base3DCopy copy)
    {
        var p = PlacedSceneSpace.ToViewer(copy.Transform.EffectiveTranslation);
        var q = PlacedSceneSpace.ToViewer(PlacedSceneSpace.Normalize(copy.Transform.EffectiveRotation));
        var s = PlacedSceneSpace.ScaleToViewer(copy.Transform.EffectiveScale);
        var category = PlacedObjectCategoryCatalog.Classify(copy.ClassName, false);
        return new Base3DObject(copy.Key, (int)category, [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z],
            true, copy.Label, Base3DObject.MarkCopy, copy.ClassPath);
    }

    /// <summary>
    /// The region a world save file belongs to (<c>WorldSave_Facility_Office1.sav</c> gives
    /// <c>Facility_Office1</c>), which names the game level to draw around a base. Null for a file
    /// that is not a region save (the metadata save, or an unrecognised name).
    /// </summary>
    public static string? RegionOf(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return null;
        var name = System.IO.Path.GetFileNameWithoutExtension(savePath);
        const string prefix = "WorldSave_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var region = name[prefix.Length..];
        return region.Length == 0 || region.Equals("MetaData", StringComparison.OrdinalIgnoreCase)
               || !region.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? null
            : region;
    }

    /// <summary>
    /// Actor names of the level-placed objects this save tracks. The level geometry view leaves
    /// them out because the scene already draws them from the save (in their saved state).
    /// </summary>
    public IReadOnlyList<string> LevelActorNames()
        => Placed.Where(p => p.DeployedByPlayer != true && !string.IsNullOrEmpty(p.ActorPath))
            .Select(p => p.ActorPath!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Player-given name when there is one, else the class without its blueprint prefix and suffix.</summary>
    public static string LabelOf(PlacedObjectSummary o)
    {
        if (!string.IsNullOrWhiteSpace(o.OwnerName)) return $"{FriendlyClass(o.ClassName)} ({o.OwnerName})";
        if (!string.IsNullOrWhiteSpace(o.CustomName) && !o.CustomName.Contains(WorldDeployable.ClaimSeparator, StringComparison.Ordinal))
            return o.CustomName;
        return FriendlyClass(o.ClassName);
    }

    /// <summary>The class name as a readable label (mirrors <see cref="WorldDeployable.FriendlyClass"/>).</summary>
    /// <summary>
    /// The kinds that can be placed: player-built objects with a saved position and a fresh-style id (not
    /// staged for deletion). The donor is one with a saved rotation when there is one, so a turn can be applied.
    /// </summary>
    public static IReadOnlyList<Base3DPlaceKind> PlaceKinds(IEnumerable<PlacedObjectSummary> objects, IReadOnlySet<string> deleted)
        => objects
            .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.ClassPath is { Length: > 0 }
                        && o.Transform?.Translation is not null && !deleted.Contains(o.Key))
            .GroupBy(o => o.ClassPath!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var donor = g.OrderBy(o => o.Transform?.Rotation is null ? 1 : 0).ThenBy(o => o.Key, StringComparer.Ordinal).First();
                return new Base3DPlaceKind(g.Key, FriendlyClass(donor.ClassName), g.Count(), donor.Key);
            })
            .OrderBy(k => k.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static string FriendlyClass(string? className)
    {
        if (string.IsNullOrEmpty(className)) return "(unknown class)";
        var name = className.EndsWith("_C", StringComparison.Ordinal) ? className[..^2] : className;
        return name.Replace("Deployed_", "", StringComparison.Ordinal)
            .Replace("Deployable_", "", StringComparison.Ordinal)
            .Replace('_', ' ');
    }

    /// <summary>Indexes (into <see cref="Objects"/>) that pass <paramref name="filter"/>.</summary>
    public int[] Apply(Base3DFilter filter)
    {
        var result = new List<int>(Objects.Count);
        var search = filter.Search?.Trim();
        for (var i = 0; i < Objects.Count; i++)
        {
            var o = Objects[i];
            if (i >= Placed.Count) { result.Add(i); continue; } // staged copies are always shown
            if (o.Built ? !filter.ShowPlayerBuilt : !filter.ShowLevelPlaced) continue;
            if (filter.HiddenCategories.Contains((PlacedObjectCategory)o.Cat)) continue;
            var z = SavedZCm[i];
            if (filter.MinZCm is { } lo && z < lo) continue;
            if (filter.MaxZCm is { } hi && z > hi) continue;
            if (!string.IsNullOrEmpty(search) && !Matches(Placed[i], o, search)) continue;
            result.Add(i);
        }
        return [.. result];
    }

    private static bool Matches(PlacedObjectSummary row, Base3DObject o, string search)
        => Contains(o.Label, search) || Contains(row.ClassName, search) || Contains(row.Key, search)
           || Contains(row.OwnerName, search) || Contains(row.CustomName, search);

    private static bool Contains(string? text, string search)
        => text is not null && text.Contains(search, StringComparison.OrdinalIgnoreCase);
}
