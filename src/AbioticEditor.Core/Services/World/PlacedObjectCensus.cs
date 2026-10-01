using System.Text.RegularExpressions;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>How a placed object's blueprint class path relates to the base game.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum PlacedClassOrigin
{
    /// <summary>Path under <c>/Game/Blueprints/</c>: a class the base game ships.</summary>
    GameBlueprint,

    /// <summary>Under <c>/Game/</c> but outside <c>/Game/Blueprints/</c>: probably base game.</summary>
    GameOther,

    /// <summary>A package root other than <c>/Game/</c> (a mod or plugin). Preserved, never dropped.</summary>
    NonGamePath,

    /// <summary>No <c>Class_</c> value at all (delta-serialized away or a different struct shape).</summary>
    Missing,
}

/// <summary>Whether a struct member is expected to matter across saves.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum PlacedFieldPersistence
{
    /// <summary>Identity or user-authored state; an existing writer or reader already treats it as meaningful.</summary>
    Persistent,

    /// <summary>Probably re-derived by the running game (transient bookkeeping); unproven, not a claim.</summary>
    LikelyRuntime,

    /// <summary>No evidence either way.</summary>
    Unknown,
}

/// <summary>One placed object as the census sees it.</summary>
public sealed record PlacedObjectSummary(
    string Key,
    string? ClassPath,
    string? ClassName,
    PlacedClassOrigin Origin,
    string? ActorPath,
    string? SubLevel,
    PlacedObjectTransform? Transform,
    string? ConstructionMode,
    string? ConstructionLevel,
    bool? DeployedByPlayer,
    string? CustomName,
    string? OwnerId,
    string? OwnerName,
    int? PaintColor,
    int InventoryCount,
    int StoredItemCount,
    IReadOnlyList<string> PoweredBySocketIds,
    IReadOnlyList<string> FieldNames,
    IReadOnlyList<PlacedCrop>? Crops = null,
    int? LiquidLevel = null,
    string? LiquidType = null);

/// <summary>
/// One planting spot of a garden plot: the spot (<c>SpotIndex_</c>), the crop's item row
/// (<c>ItemRow_.RowName</c>, e.g. <c>Plant_Corn</c>) and its growth stage (the spot's
/// <c>EDynamicProperty::GrowthStage</c>, 0 Sprout to 7 Dead; missing means 0, the default).
/// </summary>
public sealed record PlacedCrop(int Spot, string Row, int Stage);

/// <summary>Per-class rollup.</summary>
public sealed record PlacedClassCount(
    string ClassName,
    string? ClassPath,
    PlacedClassOrigin Origin,
    int Count,
    int WithTranslation,
    int WithRotation,
    int WithScale,
    int WithInventory,
    int Painted,
    int Named,
    IReadOnlyList<string> ConstructionModes,
    IReadOnlyList<string> ConstructionLevels);

/// <summary>Frequency of one struct member (hash suffix removed) across the census.</summary>
public sealed record PlacedFieldStat(
    string Field,
    IReadOnlyList<string> Types,
    int Count,
    PlacedFieldPersistence Persistence,
    string Basis);

/// <summary>One top-level world/region map (any map property) and its value layout.</summary>
public sealed record WorldMapCensusEntry(
    string Map,
    int Entries,
    string? KeyShape,
    IReadOnlyList<string> SampleKeys,
    IReadOnlyList<string> ValueFields,
    bool HasLocationLikeField,
    IReadOnlyList<string> LocationFields);

/// <summary>A power socket and the devices it references.</summary>
public sealed record PowerLinkRecord(
    string SocketId,
    string? PluggedInDeviceId,
    IReadOnlyList<string> ExtraDeviceIds,
    bool PluggedResolvesInSave,
    int ExtraResolvingInSave);

/// <summary>Everything the census learned about one world/region save.</summary>
public sealed record PlacedObjectCensusReport(
    string? Source,
    int ObjectCount,
    int DistinctClasses,
    int NonGameClassCount,
    int MissingClassCount,
    int KeysAreActorPaths,
    int KeysAreGuids,
    int KeysOther,
    IReadOnlyList<PlacedClassCount> Classes,
    IReadOnlyList<PlacedFieldStat> Fields,
    IReadOnlyList<WorldMapCensusEntry> Maps,
    IReadOnlyList<PowerLinkRecord> PowerLinks,
    IReadOnlyList<PlacedObjectSummary>? Objects);

/// <summary>
/// Phase-1 schema inventory for the base-building roadmap: a read-only census of the objects
/// placed in a world/region save. Nothing here writes; unknown or modded classes are counted and
/// listed, never filtered. Fields are grouped by their un-suffixed name because the blueprint
/// compiler hash suffixes change between game builds.
/// </summary>
public static partial class PlacedObjectCensus
{
    /// <summary>Members whose meaning is established by existing readers/writers.</summary>
    private static readonly Dictionary<string, string> PersistentBasis = new(StringComparer.Ordinal)
    {
        ["Class"] = "identifies the blueprint; used by every reader",
        ["ActorPath"] = "soft path of the actor; deployable resolution uses it",
        ["Transform"] = "location/rotation; read for base detection, written for vehicles",
        ["ConstructionMode"] = "build mode enum; part of the placed piece",
        ["ConstructionLevel"] = "build tier enum; part of the placed piece",
        ["ContainerInventories"] = "container contents; edited by the container writers",
        ["CustomTextDisplay"] = "player-given name / bed claim string; edited by the bed-claim writers",
        ["ChangableData"] = "item data (paint, dynamic properties); edited by the paint writer",
        ["DeployedByPlayer"] = "player-built vs level-placed marker",
        ["HasBeenPackaged"] = "packaged (picked up) marker",
    };

    /// <summary>Members that look like transient bookkeeping. This is a heuristic, not proven.</summary>
    private static readonly Dictionary<string, string> RuntimeBasis = new(StringComparer.Ordinal)
    {
        ["ActiveSeats"] = "occupancy of seats at save time; the game rebuilds it from live players",
        ["ItemProxies"] = "spawned proxy actors; reconstructable from the inventory",
        ["CustomSpawnedTime"] = "a spawn timestamp; not user state",
        ["NoResetVignette"] = "level-reset bookkeeping flag",
        ["BrokeWhenPackaged"] = "pickup bookkeeping flag",
        ["DeployableDestroyed"] = "destruction marker used while the level streams",
        ["FoundByPlayer"] = "discovery marker for level-placed objects",
        ["Supports"] = "structural support links recalculated by the building system",
    };

    [GeneratedRegex("^(?<n>.+?)_\\d+_[0-9A-Fa-f]{32}$")]
    private static partial Regex HashSuffix();

    /// <summary>Removes the blueprint hash suffix (<c>Hunger_2_A6C5...</c> becomes <c>Hunger</c>).</summary>
    public static string StripHash(string name)
    {
        var m = HashSuffix().Match(name);
        return m.Success ? m.Groups["n"].Value : name;
    }

    /// <summary>Classifies a (suffix-stripped) member name.</summary>
    public static (PlacedFieldPersistence Persistence, string Basis) ClassifyField(string strippedName)
    {
        if (PersistentBasis.TryGetValue(strippedName, out var p))
        {
            return (PlacedFieldPersistence.Persistent, p);
        }
        if (RuntimeBasis.TryGetValue(strippedName, out var r))
        {
            return (PlacedFieldPersistence.LikelyRuntime, r);
        }
        return (PlacedFieldPersistence.Unknown, "no evidence either way");
    }

    /// <summary>Origin of a class path (see <see cref="PlacedClassOrigin"/>).</summary>
    public static PlacedClassOrigin ClassOriginOf(string? classPath)
    {
        if (string.IsNullOrWhiteSpace(classPath)) return PlacedClassOrigin.Missing;
        if (classPath.StartsWith("/Game/Blueprints/", StringComparison.Ordinal)) return PlacedClassOrigin.GameBlueprint;
        if (classPath.StartsWith("/Game/", StringComparison.Ordinal)) return PlacedClassOrigin.GameOther;
        return PlacedClassOrigin.NonGamePath;
    }

    /// <summary>Reads the <c>Transform_</c> struct of a map entry (Translation/Rotation/Scale3D).</summary>
    public static PlacedObjectTransform? ReadTransform(IList<FPropertyTag> entryProps)
    {
        if (entryProps.FindByPrefix("Transform_")?.Property is not StructProperty tsp
            || tsp.Value is not PropertiesStruct tps)
        {
            return null;
        }
        PlacedVector? t = null, s = null;
        PlacedQuaternion? r = null;
        if (tps.Properties.FindByPrefix("Translation")?.Property is StructProperty a && a.Value is VectorStruct av)
        {
            t = new PlacedVector(av.Value.X, av.Value.Y, av.Value.Z);
        }
        if (tps.Properties.FindByPrefix("Rotation")?.Property is StructProperty b && b.Value is QuatStruct bq)
        {
            r = new PlacedQuaternion(bq.Value.X, bq.Value.Y, bq.Value.Z, bq.Value.W);
        }
        if (tps.Properties.FindByPrefix("Scale3D")?.Property is StructProperty c && c.Value is VectorStruct cv)
        {
            s = new PlacedVector(cv.Value.X, cv.Value.Y, cv.Value.Z);
        }
        return new PlacedObjectTransform(t, r, s);
    }

    /// <summary>Type label for a tag, e.g. <c>StructProperty&lt;Transform&gt;</c>.</summary>
    private static string TypeLabel(FPropertyTag tag)
    {
        var name = tag.Type?.Name?.Value ?? tag.Property?.GetType().Name ?? "?";
        var first = tag.Type?.Parameters is { Count: > 0 } ps ? ps[0].Name?.Value : null;
        return first is null ? name : $"{name}<{ShortTypeName(first)}>";
    }

    private static string ShortTypeName(string s)
    {
        var i = s.LastIndexOfAny(['/', '.']);
        return i >= 0 && i < s.Length - 1 ? s[(i + 1)..] : s;
    }

    private static string? EnumText(IList<FPropertyTag> props, string prefix)
    {
        var v = props.FindByPrefix(prefix)?.Property?.Value;
        return v switch
        {
            null => null,
            FString fs => fs.Value,
            _ => v.ToString(),
        };
    }

    /// <summary>Shape of a map key: <c>actor-path</c>, <c>guid32</c> or <c>other</c>.</summary>
    /// <summary>The planted spots of a garden plot (its <c>ItemProxies_</c>), or null when it has none.</summary>
    private static List<PlacedCrop>? ReadCrops(IList<FPropertyTag> props)
    {
        if (props.FindByPrefix("ItemProxies_")?.Property is not ArrayProperty { Value: { Length: > 0 } elements }) return null;
        var crops = new List<PlacedCrop>();
        foreach (var element in elements.OfType<StructProperty>().Select(e => e.Value).OfType<PropertiesStruct>())
        {
            if (element.Properties.FindByPrefix("SpotIndex_")?.Property?.Value is not int spot) continue;
            var row = (element.Properties.FindByPrefix("ItemRow_")?.Property as StructProperty)?.Value is PropertiesStruct handle
                ? handle.Properties.FindByPrefix("RowName")?.Property?.Value?.ToString()
                : null;
            if (string.IsNullOrEmpty(row) || row == "None") continue;
            var stage = (element.Properties.FindByPrefix("ChangeableData_")?.Property as StructProperty)?.Value is PropertiesStruct data
                ? PetDynamicProperties.Read(data.Properties, "GrowthStage") ?? 0
                : 0;
            crops.Add(new PlacedCrop(spot, row, stage));
        }
        return crops.Count == 0 ? null : crops;
    }

    public static string KeyShape(string key)
    {
        if (key.StartsWith('/') && key.Contains(':', StringComparison.Ordinal)) return "actor-path";
        if (key.Length == 32 && key.All(Uri.IsHexDigit)) return "guid32";
        return "other";
    }

    /// <summary>
    /// Builds the census for one loaded region/world save. <paramref name="includeObjects"/> adds
    /// the per-object list (large for the Facility region).
    /// </summary>
    public static PlacedObjectCensusReport Build(WorldSaveData data, string? source = null, bool includeObjects = true)
    {
        var save = data.Raw;
        var storedById = data.Deployables.ToDictionary(d => d.Id, d => d.StoredItemCount, StringComparer.Ordinal);

        var deployKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
        {
            deployKeys.Add(e.Key);
        }

        var powerLinks = new List<PowerLinkRecord>();
        var poweredBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(save, "PowerSocketMap"))
        {
            var socketId = e.Props.GetString("PowerSocket_") ?? e.Key;
            var plugged = e.Props.GetString("PluggedInDeviceAssetID_");
            if (string.IsNullOrEmpty(plugged) || plugged == "-1") plugged = null;
            var extras = ReadStringArray(e.Props, "ExtraPoweredDeviceAssetIDs_");
            powerLinks.Add(new PowerLinkRecord(
                socketId, plugged, extras,
                plugged is not null && deployKeys.Contains(plugged),
                extras.Count(deployKeys.Contains)));
            if (plugged is not null) AddTo(poweredBy, plugged, socketId);
            foreach (var x in extras) AddTo(poweredBy, x, socketId);
        }

        var objects = new List<PlacedObjectSummary>();
        var fieldTypes = new Dictionary<string, (HashSet<string> Types, int Count)>(StringComparer.Ordinal);
        int keyPath = 0, keyGuid = 0, keyOther = 0;

        foreach (var entry in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
        {
            var props = entry.Props;
            switch (KeyShape(entry.Key))
            {
                case "actor-path": keyPath++; break;
                case "guid32": keyGuid++; break;
                default: keyOther++; break;
            }

            var names = new List<string>(props.Count);
            foreach (var tag in props)
            {
                var stripped = StripHash(tag.Name?.Value ?? "?");
                names.Add(stripped);
                if (!fieldTypes.TryGetValue(stripped, out var acc))
                {
                    acc = (new HashSet<string>(StringComparer.Ordinal), 0);
                }
                acc.Types.Add(TypeLabel(tag));
                fieldTypes[stripped] = (acc.Types, acc.Count + 1);
            }

            var classPath = ClassPathOf(props);
            var className = ClassNameOf(classPath);
            string? actorPath = null;
            if (WorldMapAccessor.GetSoftObjectPath(props, "ActorPath_") is { } a)
            {
                actorPath = FullSoftPath(a.Package, a.Asset, a.SubPath);
            }
            var subLevel = DoorIdParser.Parse(actorPath ?? entry.Key).Map;

            var customName = props.GetString("CustomTextDisplay_");
            var claim = WorldDeployable.ParseClaim(customName);

            int? paint = null;
            int? liquid = null;
            string? liquidType = null;
            if (props.FindByPrefix("ChangableData_")?.Property is StructProperty cs && cs.Value is PropertiesStruct cps)
            {
                paint = PetDynamicProperties.Read(cps.Properties, DeployablePaintCatalog.DynamicPropertyKey);
                // How much a liquid container holds (garden plots, barrels, cauldrons).
                if (cps.Properties.FindByPrefix("LiquidLevel_")?.Property?.Value is int level) liquid = level;
                // Which liquid (an E_LiquidType name such as "E_LiquidType::NewEnumerator16").
                liquidType = cps.Properties.FindByPrefix("CurrentLiquid_")?.Property?.Value?.ToString();
            }

            var crops = ReadCrops(props);

            var invCount = props.FindByPrefix("ContainerInventories_")?.Property is ArrayProperty ia && ia.Value is { } iv
                ? iv.Length
                : 0;

            objects.Add(new PlacedObjectSummary(
                entry.Key, classPath, className, ClassOriginOf(classPath), actorPath,
                string.IsNullOrEmpty(subLevel) ? null : subLevel,
                ReadTransform(props),
                EnumText(props, "ConstructionMode_"),
                EnumText(props, "ConstructionLevel_"),
                props.TryGetBool("DeployedByPlayer_"),
                string.IsNullOrWhiteSpace(customName) ? null : customName,
                claim.OwnerId, claim.Name, paint, invCount,
                storedById.TryGetValue(entry.Key, out var stored) ? stored : 0,
                poweredBy.TryGetValue(entry.Key, out var pb) ? pb : [],
                names,
                crops,
                liquid,
                string.IsNullOrEmpty(liquidType) ? null : liquidType));
        }

        var classes = objects
            .GroupBy(o => o.ClassPath ?? o.ClassName ?? "(missing)", StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return new PlacedClassCount(
                    first.ClassName ?? "(missing)", first.ClassPath, first.Origin, g.Count(),
                    g.Count(o => o.Transform?.Translation is not null),
                    g.Count(o => o.Transform?.Rotation is not null),
                    g.Count(o => o.Transform?.Scale3D is not null),
                    g.Count(o => o.InventoryCount > 0),
                    g.Count(o => o.PaintColor is { } p && p != DeployablePaintCatalog.NoneValue),
                    g.Count(o => o.CustomName is not null),
                    g.Select(o => o.ConstructionMode ?? "(omitted)").Distinct().Order().ToList(),
                    g.Select(o => o.ConstructionLevel ?? "(omitted)").Distinct().Order().ToList());
            })
            .OrderByDescending(c => c.Count).ThenBy(c => c.ClassName, StringComparer.Ordinal)
            .ToList();

        var fields = fieldTypes
            .Select(kv =>
            {
                var (persistence, basis) = ClassifyField(kv.Key);
                return new PlacedFieldStat(kv.Key, kv.Value.Types.Order().ToList(), kv.Value.Count, persistence, basis);
            })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Field, StringComparer.Ordinal)
            .ToList();

        return new PlacedObjectCensusReport(
            source, objects.Count, classes.Count,
            objects.Count(o => o.Origin == PlacedClassOrigin.NonGamePath),
            objects.Count(o => o.Origin == PlacedClassOrigin.Missing),
            keyPath, keyGuid, keyOther,
            classes, fields, CensusMaps(save), powerLinks,
            includeObjects ? objects : null);
    }

    private static void AddTo(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(value);
    }

    /// <summary>Reads an array-of-string leaf; skips empty and the game's "-1" placeholder.</summary>
    internal static List<string> ReadStringArray(IList<FPropertyTag> props, string prefix)
    {
        var result = new List<string>();
        if (props.FindByPrefix(prefix)?.Property is ArrayProperty arr && arr.Value is { } items)
        {
            foreach (var item in items)
            {
                var text = (item as FProperty)?.Value?.ToString() ?? item?.ToString();
                if (!string.IsNullOrEmpty(text) && text != "-1") result.Add(text);
            }
        }
        return result;
    }

    internal static string? ClassPathOf(IList<FPropertyTag> props)
    {
        var tag = props.FindByPrefix("Class_");
        if (tag?.Property is null) return null;
        if (WorldMapAccessor.GetSoftObjectPath(props, "Class_") is { } s)
        {
            return FullSoftPath(s.Package, s.Asset, s.SubPath);
        }
        var v = tag.Property.Value;
        if (v is UeSaveGame.DataTypes.SoftObjectPath sop)
        {
            return FullSoftPath(sop.PackageName?.Value, sop.AssetName?.Value, sop.SubPathString?.Value);
        }
        var text = v is FString fs ? fs.Value : v?.ToString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Joins soft-object-path parts as <c>Package.Asset:SubPath</c>; null when all empty.</summary>
    public static string? FullSoftPath(string? package, string? asset, string? subPath)
    {
        var text = package ?? string.Empty;
        if (!string.IsNullOrEmpty(asset)) text += "." + asset;
        if (!string.IsNullOrEmpty(subPath)) text += ":" + subPath;
        return text.Length == 0 ? null : text;
    }

    /// <summary>The blueprint class name from a path such as <c>/Game/.../Deployed_X.Deployed_X_C</c>.</summary>
    public static string? ClassNameOf(string? classPath)
    {
        if (string.IsNullOrEmpty(classPath)) return null;
        var i = classPath.LastIndexOf('.');
        return i >= 0 && i < classPath.Length - 1 ? classPath[(i + 1)..] : classPath;
    }

    private static List<WorldMapCensusEntry> CensusMaps(SaveGame save)
    {
        var result = new List<WorldMapCensusEntry>();
        foreach (var tag in save.Properties ?? [])
        {
            if (tag.Property is not MapProperty mp || mp.Value is null) continue;
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var locations = new SortedSet<string>(StringComparer.Ordinal);
            var keys = new List<string>();
            string? shape = null;
            foreach (var kv in mp.Value)
            {
                var key = WorldSaveReader.ExtractMapKeyString(kv.Key) ?? "";
                if (keys.Count < 3) keys.Add(key);
                shape ??= KeyShape(key);
                if (kv.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
                {
                    foreach (var f in ps.Properties)
                    {
                        var n = StripHash(f.Name?.Value ?? "?");
                        var label = TypeLabel(f);
                        fields[n] = label;
                        if (label.Contains("<Vector>", StringComparison.Ordinal)
                            || label.Contains("<Transform>", StringComparison.Ordinal)
                            || label.Contains("<Quat>", StringComparison.Ordinal)
                            || n.Contains("Location", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("Transform", StringComparison.OrdinalIgnoreCase))
                        {
                            locations.Add($"{n}:{label}");
                        }
                    }
                }
            }
            result.Add(new WorldMapCensusEntry(
                tag.Name?.Value ?? "?", mp.Value.Count, shape, keys,
                fields.Select(kv => $"{kv.Key}: {kv.Value}").ToList(),
                locations.Count > 0, locations.ToList()));
        }
        return result;
    }
}
