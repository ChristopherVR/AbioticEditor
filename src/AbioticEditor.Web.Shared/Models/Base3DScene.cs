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
    string Label);

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
public sealed record Base3DUnresolved(string Key, string Label, string? ClassName, bool Built, string Reason);

/// <summary>Everything the 3D view needs from a region, built from the session's saved and staged state.</summary>
public sealed class Base3DScene
{
    /// <summary>Drawable objects (staged edits applied), aligned with <see cref="Placed"/>.</summary>
    public IReadOnlyList<Base3DObject> Objects { get; }

    /// <summary>The census rows behind <see cref="Objects"/>, same order.</summary>
    public IReadOnlyList<PlacedObjectSummary> Placed { get; }

    /// <summary>Saved Z (cm) of each drawable object, same order (for the height band).</summary>
    public IReadOnlyList<double> SavedZCm { get; }

    public IReadOnlyList<Base3DUnresolved> Unresolved { get; }

    public double MinZCm { get; }
    public double MaxZCm { get; }

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
        IEnumerable<PlacedObjectSummary> placed, Func<string, PlacedObjectTransform?> current)
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
            objects.Add(ToViewerObject(o, current(o.Key) ?? o.Transform));
            rows.Add(o);
            zs.Add(saved.Z);
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
            o.DeployedByPlayer == true, LabelOf(o));
    }

    /// <summary>Player-given name when there is one, else the class without its blueprint prefix and suffix.</summary>
    public static string LabelOf(PlacedObjectSummary o)
    {
        if (!string.IsNullOrWhiteSpace(o.OwnerName)) return $"{FriendlyClass(o.ClassName)} ({o.OwnerName})";
        if (!string.IsNullOrWhiteSpace(o.CustomName) && !o.CustomName.Contains(WorldDeployable.ClaimSeparator, StringComparison.Ordinal))
            return o.CustomName;
        return FriendlyClass(o.ClassName);
    }

    /// <summary>The class name as a readable label (mirrors <see cref="WorldDeployable.FriendlyClass"/>).</summary>
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
