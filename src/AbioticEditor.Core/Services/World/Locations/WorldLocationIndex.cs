namespace AbioticEditor.Core.WorldSaves;

/// <summary>Looks up a static actor placement by (map, actor name). Null when unknown or unavailable.</summary>
public delegate DoorWorldLocation? StaticPlacementLookup(string? mapName, string actorName);

/// <summary>
/// The shared location index for one region save: every locatable entity the save knows about,
/// keyed by region, level, actor path and save identity. Built purely from what the loaded
/// <see cref="WorldSaveData"/> already holds plus an optional static-placement lookup
/// (typically <see cref="DoorLocationResolver"/>), so it works without game assets: entries
/// simply come back unresolved with an explicit reason.
/// </summary>
public sealed class WorldLocationIndex
{
    /// <summary>The persistent level's name, used when an actor path names no sub-level.</summary>
    public const string PersistentLevel = "Facility";

    private readonly List<LocationEntry> _entries = new();
    private readonly Dictionary<string, List<LocationEntry>> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LocationEntry>> _byLevelActor = new(StringComparer.OrdinalIgnoreCase);

    public WorldLocationIndex(string region) => Region = region;

    public string Region { get; }

    public IReadOnlyList<LocationEntry> Entries => _entries;

    /// <summary>Adds an entry. Entries with the same key are all kept (see <see cref="Find"/>).</summary>
    public void Add(LocationEntry entry)
    {
        _entries.Add(entry);
        Bucket(_byKey, entry.Key).Add(entry);
        Bucket(_byLevelActor, LevelActorKey(entry.Level, entry.ActorName)).Add(entry);
    }

    /// <summary>Entries for a full key (region|level|path). More than one means the save itself repeats the path.</summary>
    public IReadOnlyList<LocationEntry> Find(string level, string actorPath)
        => _byKey.TryGetValue(LocationEntry.MakeKey(Region, level, actorPath), out var l) ? l : Array.Empty<LocationEntry>();

    /// <summary>
    /// Entries sharing an actor name within one level. Actor names such as <c>SimpleDoor_C_12</c>
    /// repeat across levels, so a bare name is never a lookup key on its own.
    /// </summary>
    public IReadOnlyList<LocationEntry> FindByActorName(string level, string actorName)
        => _byLevelActor.TryGetValue(LevelActorKey(level, actorName), out var l) ? l : Array.Empty<LocationEntry>();

    /// <summary>The entry for a save identity (the id the editors already use), or null.</summary>
    public LocationEntry? FindBySaveIdentity(string? saveIdentity)
        => saveIdentity is null ? null : _entries.FirstOrDefault(e =>
            string.Equals(e.SaveIdentity, saveIdentity, StringComparison.Ordinal));

    /// <summary>Actor names that occur in more than one level (proof the level context matters).</summary>
    public IReadOnlyList<string> ActorNamesSharedAcrossLevels()
        => _entries.GroupBy(e => e.ActorName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(e => e.Level).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Builds the index from a loaded region save. <paramref name="staticPlacements"/> supplies
    /// level-baked positions (doors); pass null when no game assets are available.
    /// </summary>
    public static WorldLocationIndex Build(
        string region, WorldSaveData data, StaticPlacementLookup? staticPlacements = null)
    {
        var index = new WorldLocationIndex(region);

        foreach (var d in data.Doors)
        {
            var (map, actor) = DoorIdParser.Parse(d.Id);
            var level = LevelOf(map);
            var cls = DoorIdParser.ClassNameFromActor(actor);
            if (d.X is { } dx && d.Y is { } dy && d.Z is { } dz)
            {
                index.Add(Positioned(region, level, d.Id, actor, LocatedCategory.Door, cls, LocationKind.CurrentLivePosition, dx, dy, dz));
            }
            else if (staticPlacements?.Invoke(string.IsNullOrEmpty(map) ? null : map, actor) is { } p)
            {
                index.Add(Positioned(region, level, d.Id, actor, LocatedCategory.Door, cls, LocationKind.StaticPlacement, p.X, p.Y, p.Z));
            }
            else
            {
                index.Add(Unresolved(region, level, d.Id, actor, LocatedCategory.Door, cls,
                    staticPlacements is null ? UnresolvedReason.MissingGeometry : UnresolvedReason.ObsoletePath,
                    staticPlacements is null
                        ? "Door placement lives in the game's level package, which is not available."
                        : "The level package loaded but holds no actor with this name."));
            }
        }

        foreach (var n in data.Npcs)
        {
            index.AddSaved(region, n.Id, n.ActorName, n.IsPet ? LocatedCategory.Creature : LocatedCategory.Npc, n.NpcClass, n.X, n.Y, n.Z);
        }
        foreach (var p in data.Pets)
        {
            index.AddSaved(region, p.Id, p.Id, LocatedCategory.Creature, p.NpcClass, p.X, p.Y, p.Z);
        }
        foreach (var v in data.Vehicles)
        {
            index.AddSaved(region, v.Id, v.Id, LocatedCategory.Vehicle, v.VehicleClass, v.X, v.Y, v.Z);
        }
        foreach (var i in data.DroppedItems)
        {
            index.AddSaved(region, i.Id, i.Id, LocatedCategory.DroppedItem, i.Slot.ItemId, i.X, i.Y, i.Z);
        }
        foreach (var dep in data.Deployables)
        {
            index.AddSaved(region, dep.Id, dep.Id, LocatedCategory.Deployable, dep.ClassName, dep.X, dep.Y, dep.Z);
        }

        var vehicleByContainer = data.Vehicles
            .Where(v => !string.IsNullOrEmpty(v.ContainerId))
            .GroupBy(v => v.ContainerId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        foreach (var c in data.Containers)
        {
            if (vehicleByContainer.TryGetValue(c.Id, out var owner))
            {
                var (cm, ca) = DoorIdParser.Parse(c.Id);
                index.Add(new LocationEntry(region, LevelOf(cm), c.Id, ca, c.Id, LocatedCategory.Container, c.ClassName,
                    LocationKind.CarriedOrContained, OwnerActorPath: owner,
                    Note: "Stored in a vehicle; it has no floor position of its own."));
                continue;
            }
            index.AddSaved(region, c.Id, c.Id, LocatedCategory.Container, c.ClassName, c.X, c.Y, c.Z,
                savedKind: c.Source == WorldContainerSource.Live ? LocationKind.CurrentLivePosition : LocationKind.LastSavedPosition);
        }

        return index;
    }

    /// <summary>
    /// Registers an entity the save models do not carry (resource nodes, power sockets, live-only
    /// actors). A null position registers it as unresolved with <paramref name="reason"/>.
    /// </summary>
    public void AddExternal(
        LocatedCategory category, string level, string actorPath, string? className,
        (double X, double Y, double Z)? position, LocationKind kind, UnresolvedReason reason = UnresolvedReason.MissingGeometry)
    {
        var name = actorPath[(actorPath.LastIndexOf('.') + 1)..];
        if (position is { } p && kind is not (LocationKind.Unresolved or LocationKind.CarriedOrContained))
        {
            Add(Positioned(Region, level, actorPath, name, category, className, kind, p.X, p.Y, p.Z));
        }
        else
        {
            Add(Unresolved(Region, level, actorPath, name, category, className, reason, null));
        }
    }

    /// <summary>Per level and category counts of resolved versus unresolved entries, so partial coverage is explicit.</summary>
    public LocationCoverageReport Coverage() => LocationCoverageReport.From(this);

    private void AddSaved(
        string region, string id, string nameHint, LocatedCategory category, string? className,
        double x, double y, double z, LocationKind savedKind = LocationKind.LastSavedPosition)
    {
        var (map, actor) = DoorIdParser.Parse(id);
        var level = LevelOf(map);
        var name = string.IsNullOrEmpty(actor) ? nameHint : actor;
        // The saved models default absent coordinates to 0, so an exact (0,0,0) is "no position",
        // not a real spot: it must never become a marker at the world origin.
        if (x == 0 && y == 0 && z == 0)
        {
            Add(Unresolved(region, level, id, name, category, className, UnresolvedReason.NoSavedPosition,
                "The save holds no position for this entry."));
            return;
        }
        Add(Positioned(region, level, id, name, category, className, savedKind, x, y, z));
    }

    private static string LevelOf(string map) => string.IsNullOrEmpty(map) ? PersistentLevel : map;

    private static LocationEntry Positioned(
        string region, string level, string path, string name, LocatedCategory cat, string? cls,
        LocationKind kind, double x, double y, double z)
        => new(region, level, path, name, path, cat, cls, kind, x, y, z);

    private static LocationEntry Unresolved(
        string region, string level, string path, string name, LocatedCategory cat, string? cls,
        UnresolvedReason reason, string? note)
        => new(region, level, path, name, path, cat, cls, LocationKind.Unresolved, Reason: reason, Note: note);

    private static string LevelActorKey(string level, string actor) => level + "|" + actor;

    private static List<LocationEntry> Bucket(Dictionary<string, List<LocationEntry>> d, string key)
    {
        if (!d.TryGetValue(key, out var l)) d[key] = l = new List<LocationEntry>();
        return l;
    }
}

/// <summary>Resolved versus unresolved counts for one level and category.</summary>
public sealed record CoverageRow(
    string Level, LocatedCategory Category, int Total, int Resolved, int Carried,
    IReadOnlyDictionary<UnresolvedReason, int> UnresolvedByReason)
{
    public int Unresolved => UnresolvedByReason.Values.Sum();
}

/// <summary>Coverage per level and class. Never claims completeness: it reports what is missing.</summary>
public sealed record LocationCoverageReport(IReadOnlyList<CoverageRow> Rows)
{
    public int Total => Rows.Sum(r => r.Total);
    public int Resolved => Rows.Sum(r => r.Resolved);
    public int Unresolved => Rows.Sum(r => r.Unresolved);

    /// <summary>True only when every entry either has a position or is legitimately carried.</summary>
    public bool IsComplete => Unresolved == 0;

    public static LocationCoverageReport From(WorldLocationIndex index)
    {
        var rows = index.Entries
            .GroupBy(e => (e.Level, e.Category))
            .OrderBy(g => g.Key.Level, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.Category)
            .Select(g => new CoverageRow(
                g.Key.Level, g.Key.Category, g.Count(),
                g.Count(e => e.HasPosition),
                g.Count(e => e.Kind == LocationKind.CarriedOrContained),
                g.Where(e => e.Kind == LocationKind.Unresolved)
                    .GroupBy(e => e.Reason).ToDictionary(x => x.Key, x => x.Count())))
            .ToList();
        return new LocationCoverageReport(rows);
    }

    /// <summary>A plain-text table that states unresolved counts and reasons alongside resolved ones.</summary>
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in Rows)
        {
            sb.Append(r.Level).Append(" / ").Append(r.Category).Append(": ")
              .Append(r.Resolved).Append('/').Append(r.Total).Append(" located");
            if (r.Carried > 0) sb.Append(", ").Append(r.Carried).Append(" carried/contained");
            foreach (var kv in r.UnresolvedByReason.OrderBy(k => k.Key))
            {
                sb.Append(", ").Append(kv.Value).Append(' ').Append(kv.Key);
            }
            sb.AppendLine();
        }
        sb.Append("Total: ").Append(Resolved).Append('/').Append(Total).Append(" located, ")
          .Append(Unresolved).Append(" unresolved. ")
          .Append(IsComplete ? "Every entry is accounted for." : "Coverage is PARTIAL.");
        return sb.ToString();
    }
}
