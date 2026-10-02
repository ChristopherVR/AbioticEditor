using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Models;

/// <summary>One outlet (or wall socket) as the power panel shows it, with staged changes applied.</summary>
/// <param name="SocketId">The socket record key (an outlet may not have a record yet).</param>
/// <param name="Label">"PlugStrip (1A2B3C4D) outlet 2", "wall socket PowerSocket_ParentBP_C_3".</param>
/// <param name="DeviceKey">The device it powers after staged changes, or null.</param>
/// <param name="DeviceLabel">That device as a player reads it.</param>
/// <param name="Recorded">False for an outlet the game has not recorded yet (nothing was ever plugged in).</param>
/// <param name="Staged">True when a staged change affects this socket.</param>
public sealed record PowerOutletView(string SocketId, string Label, string? DeviceKey, string? DeviceLabel, bool Recorded, bool Staged);

/// <summary>A device that could be plugged into a socket, nearest first.</summary>
/// <param name="Key">The device key.</param>
/// <param name="Label">The device as players read it.</param>
/// <param name="DistanceCm">Distance from the socket, when both positions are known.</param>
/// <param name="CurrentlyPowered">Plugged in somewhere in this save (it would move here).</param>
/// <param name="PoweredInOtherSave">The other save it is plugged in, if any (it must be unplugged there first).</param>
/// <param name="InOtherSave">The save the device itself is kept in, when it is not this one.</param>
public sealed record PowerCandidate(string Key, string Label, double? DistanceCm, bool CurrentlyPowered,
    string? PoweredInOtherSave = null, string? InOtherSave = null);

// Power rerouting and repair for the Power Sockets tab (and the 3D inspector). Everything stages in the
// same StagedBaseEdits the base editor uses, so it previews, validates and saves with the other base
// edits in one all-or-nothing apply that keeps a .bak.
public sealed partial class WorldSaveSession
{
    /// <summary>The device whose outlets a socket record belongs to (null for a wall socket).</summary>
    public static string? SocketOwner(string socketId) => PlacedGroupReferenceAnalyzer.OwnerKeyOf(socketId);

    /// <summary>
    /// Everything the power helpers read, computed once per state of the edits (the POWER card asks
    /// several of them on every render, and each used to rescan the whole save): object classes (staged
    /// new objects included), what each socket powers after staging, which device feeds which, the
    /// outlet digits each class uses, and the staged new objects by key.
    /// </summary>
    private sealed record PowerSnapshot(
        Dictionary<string, string?> Classes,
        Dictionary<string, string?> Plugged,
        Dictionary<string, string> FeedOf,
        Dictionary<string, HashSet<char>> Digits,
        Dictionary<string, DuplicationPreviewRow> NewObjects);

    private (int Revision, object Data, int Others, PowerSnapshot Snapshot)? _powerSnapshot;

    private PowerSnapshot Power()
    {
        if (_powerSnapshot is { } cached && cached.Revision == PlacedTransformsRevision && ReferenceEquals(cached.Data, _data)
            && cached.Others == _baseEdits.OtherSaves.Count)
            return cached.Snapshot;

        var newObjects = (HasStagedBaseEdits ? PreviewBaseEdits().Duplications : [])
            .Where(r => !r.Blocked && r.NewKey.Length == 32)
            .GroupBy(r => r.NewKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var classes = PowerLinkEdits.ObjectClasses(_data);
        var digits = PowerLinkEdits.OutletNumbersByClass(_data);
        foreach (var row in newObjects.Values) classes[row.NewKey] = row.ClassName;

        var plugged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(_data.Raw, "PowerSocketMap"))
        {
            var p = e.Props.GetString("PluggedInDeviceAssetID_");
            plugged[e.Key] = string.IsNullOrEmpty(p) || p == "-1" ? null : p;
        }
        foreach (var cleaned in _baseEdits.SocketCleanups) plugged.Remove(cleaned);
        // A staged new object's outlet records (copied from its donor, unplugged unless kept).
        foreach (var row in newObjects.Values)
        {
            foreach (var socket in row.Sockets)
                plugged[socket.NewId] = socket.PluggedAfter is { Length: 32 } device ? device : null;
        }
        if (HasStagedBaseEdits)
        {
            foreach (var row in PreviewBaseEdits().PowerLinks.Where(r => !r.Blocked))
            {
                foreach (var f in row.FeedsCleared) plugged[f] = null;
                plugged[row.SocketId] = row.DeviceAfter;
            }
        }

        var snapshot = new PowerSnapshot(classes, plugged, PowerLinkEdits.FeedOf(plugged), digits, newObjects);
        _powerSnapshot = (PlacedTransformsRevision, _data, _baseEdits.OtherSaves.Count, snapshot);
        return snapshot;
    }

    /// <summary>Objects staged to be placed (copies and new objects) that will be written on SAVE.</summary>
    private Dictionary<string, DuplicationPreviewRow>.ValueCollection StagedNewObjects() => Power().NewObjects.Values;

    /// <summary>The save's object classes plus those of objects staged to be placed (a copy the caller may change).</summary>
    private Dictionary<string, string?> ClassesWithStaged() => new(Power().Classes, StringComparer.Ordinal);

    /// <summary>Where an object is after staged edits: a saved object's current place, or a staged new object's planned place.</summary>
    public PlacedVector? PositionAfterStaging(string key)
        => CurrentPlacedTransform(key)?.Translation
           ?? (Power().NewObjects.TryGetValue(key, out var row) ? row.After?.Translation : null);

    /// <summary>Which device each socket powers after the staged power changes (a what-if over the save; a copy).</summary>
    private Dictionary<string, string?> PluggedAfterStaging() => new(Power().Plugged, StringComparer.Ordinal);

    /// <summary>
    /// Every outlet of the device that owns <paramref name="socketId"/> (recorded or not yet recorded),
    /// or just the socket itself for a wall socket, with what each powers after staged changes.
    /// </summary>
    public IReadOnlyList<PowerOutletView> PowerOutletsAround(string socketId)
    {
        var classes = ClassesWithStaged();
        foreach (var (_, other) in _baseEdits.OtherSaves)
        {
            foreach (var (k, c) in PowerLinkEdits.ObjectClasses(other)) classes.TryAdd(k, c);
        }
        var plugged = PluggedAfterStaging();
        var staged = _baseEdits.PowerLinks.Select(l => l.SocketId).Concat(_baseEdits.SocketCleanups)
            .Concat(PreviewBaseEdits().PowerLinks.SelectMany(r => r.FeedsCleared)).ToHashSet(StringComparer.Ordinal);
        var owner = SocketOwner(socketId);
        var ids = new SortedSet<string>(StringComparer.Ordinal) { socketId };
        if (owner is not null)
        {
            foreach (var id in plugged.Keys.Where(k => SocketOwner(k) == owner)) ids.Add(id);
            if (classes.TryGetValue(owner, out var cls) && cls is not null
                && Power().Digits.TryGetValue(cls, out var digits))
            {
                foreach (var d in digits) ids.Add(owner + d);
            }
        }
        return ids.Select(id =>
        {
            var device = plugged.GetValueOrDefault(id);
            return new PowerOutletView(
                id, PowerLinkEdits.SocketLabel(id, SocketOwner(id), classes), device,
                device is null ? null : DeviceName(device, classes),
                plugged.ContainsKey(id) || _baseEdits.PowerLinks.Any(l => l.SocketId == id), staged.Contains(id));
        }).ToList();
    }

    /// <summary>
    /// Player-built devices that could be plugged into <paramref name="socketId"/>, nearest to its device
    /// first. Devices that would make a loop, and the socket's own device, are left out.
    /// </summary>
    public IReadOnlyList<PowerCandidate> PowerCandidatesFor(string socketId, bool includeAll = false, int max = 40, PlacedVector? socketPosition = null)
    {
        var classes = PowerLinkEdits.ObjectClasses(_data);
        var plugged = PluggedAfterStaging();
        var feedOf = Power().FeedOf;
        var usesPower = includeAll ? null : PoweredClasses(classes);
        var powered = plugged.Values.Where(v => v is not null).ToHashSet(StringComparer.Ordinal);
        var owner = SocketOwner(socketId);
        // A wall socket has no saved position; the caller passes its world position from the level.
        var origin = owner is null ? socketPosition : PositionAfterStaging(owner);
        var deleting = StagedPlacedDeletions.Keys.ToHashSet(StringComparer.Ordinal);

        // Devices from the world's other saves (a wall socket in a region save powers devices kept in the
        // Facility save), and where each is plugged in there.
        var elsewhere = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = PlacedObjects.Where(o => o.DeployedByPlayer == true && o.Key.Length == 32)
            .Select(o => (Row: o, Position: CurrentPlacedTransform(o.Key)?.Translation, File: (string?)null)).ToList();
        foreach (var row in StagedNewObjects())
        {
            classes[row.NewKey] = row.ClassName;
            if (FindPlacedObject(row.SourceKey) is { } donor)
                candidates.Add((donor with { Key = row.NewKey, Transform = row.After }, row.After?.Translation, null));
        }
        foreach (var (name, other) in _baseEdits.OtherSaves)
        {
            foreach (var e in WorldMapAccessor.Entries(other.Raw, "PowerSocketMap"))
            {
                if (e.Props.GetString("PluggedInDeviceAssetID_") is { Length: 32 } p) elsewhere.TryAdd(p, name);
            }
            foreach (var o in OtherSaveObjects(name, other)) candidates.Add((o, o.Transform?.Translation, name));
            foreach (var (k, c) in PowerLinkEdits.ObjectClasses(other)) classes.TryAdd(k, c);
        }

        return candidates
            .Where(c => c.Row.Key != owner && !deleting.Contains(c.Row.Key)
                        && (usesPower is null || (c.Row.ClassName is { } cn && usesPower.Contains(cn)))
                        && (owner is null || !PowerLinkEdits.IsUpstreamIn(c.Row.Key, owner, feedOf)))
            .DistinctBy(c => c.Row.Key)
            .Select(c =>
            {
                double? d = origin is { } a && c.Position is { } b
                    ? Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2))
                    : null;
                return new PowerCandidate(c.Row.Key, DeviceName(c.Row.Key, classes), d,
                    powered.Contains(c.Row.Key), elsewhere.GetValueOrDefault(c.Row.Key), c.File);
            })
            .OrderBy(c => c.DistanceCm ?? double.MaxValue)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The kinds of object that take power, going by what this world shows: every class that is plugged
    /// in somewhere (in this save or the other saves already read), and every class that has outlets of
    /// its own. Read from the saves, so no list of class names is kept here; a kind nobody has plugged
    /// in yet appears with "show all objects".
    /// </summary>
    private HashSet<string> PoweredClasses(Dictionary<string, string?> classes)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Add(WorldSaveData data, Dictionary<string, string?> cls)
        {
            foreach (var e in WorldMapAccessor.Entries(data.Raw, "PowerSocketMap"))
            {
                var p = e.Props.GetString("PluggedInDeviceAssetID_");
                if (p is { Length: 32 } && cls.GetValueOrDefault(p) is { } pc) result.Add(pc);
                if (SocketOwner(e.Key) is { } o && cls.GetValueOrDefault(o) is { } oc) result.Add(oc);
            }
        }
        Add(_data, classes);
        foreach (var (_, other) in _baseEdits.OtherSaves) Add(other, PowerLinkEdits.ObjectClasses(other));
        return result;
    }

    private readonly Dictionary<string, IReadOnlyList<PlacedObjectSummary>> _otherSaveObjects = new(StringComparer.Ordinal);

    /// <summary>Player-built objects of another save of the world (cached; those saves are read only).</summary>
    private IReadOnlyList<PlacedObjectSummary> OtherSaveObjects(string name, WorldSaveData other)
    {
        if (!_otherSaveObjects.TryGetValue(name, out var list))
        {
            list = (PlacedObjectCensus.Build(other, name, includeObjects: true).Objects ?? [])
                .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32).ToList();
            _otherSaveObjects[name] = list;
        }
        return list;
    }

    private static string DeviceName(string key, Dictionary<string, string?> classes)
        => $"{PowerLinkEdits.Friendly(classes.GetValueOrDefault(key))} ({key[..Math.Min(8, key.Length)]})";

    /// <summary>Where a device takes power from after staged changes: the socket, and the other save it is in (null for this one).</summary>
    public (string SocketId, string Label, string? File)? PowerFeedOf(string deviceKey)
    {
        var classes = ClassesWithStaged();
        foreach (var (socket, device) in PluggedAfterStaging())
        {
            if (device == deviceKey) return (socket, PowerLinkEdits.SocketLabel(socket, SocketOwner(socket), classes), null);
        }
        foreach (var (name, other) in _baseEdits.OtherSaves)
        {
            foreach (var e in WorldMapAccessor.Entries(other.Raw, "PowerSocketMap"))
            {
                if (e.Props.GetString("PluggedInDeviceAssetID_") == deviceKey)
                    return (e.Key, PowerLinkEdits.SocketLabel(e.Key, SocketOwner(e.Key), PowerLinkEdits.ObjectClasses(other)), name);
            }
        }
        return null;
    }

    /// <summary>
    /// True when the object takes or gives power: it is plugged in, or it has outlets of its own
    /// (recorded, or ones its kind uses in this save), or <paramref name="classRequiresPower"/>
    /// (the game's own answer for its kind, when known) says it runs on power. With no game answer,
    /// another object of the same kind plugged in somewhere in this save counts.
    /// </summary>
    public bool IsPowerDevice(string deviceKey, bool? classRequiresPower)
    {
        if (PowerFeedOf(deviceKey) is not null || FirstOutletOf(deviceKey) is not null) return true;
        if (classRequiresPower is { } known) return known;
        var snapshot = Power();
        if (snapshot.Classes.GetValueOrDefault(deviceKey) is not { } cls) return false;
        return snapshot.Plugged.Values.Any(device => device is not null
            && string.Equals(snapshot.Classes.GetValueOrDefault(device), cls, StringComparison.Ordinal));
    }

    /// <summary>The first outlet id of a device that has outlets (recorded, or a number its kind uses), or null.</summary>
    public string? FirstOutletOf(string deviceKey)
    {
        var classes = ClassesWithStaged();
        var recorded = PluggedAfterStaging().Keys.Where(k => SocketOwner(k) == deviceKey).Order(StringComparer.Ordinal).FirstOrDefault();
        if (recorded is not null) return recorded;
        return classes.GetValueOrDefault(deviceKey) is { } cls && Power().Digits.TryGetValue(cls, out var digits) && digits.Count > 0
            ? deviceKey + digits.Min()
            : null;
    }

    /// <summary>
    /// Outlets (recorded or not yet) of other devices, and this save's wall sockets, that could power
    /// <paramref name="deviceKey"/>, nearest first. Outlets of devices it powers (a loop) are left out.
    /// </summary>
    public IReadOnlyList<(string SocketId, string Label, double? DistanceCm, string? Powers)> PowerSocketsNear(string deviceKey, int max = 30)
    {
        var classes = ClassesWithStaged();
        var plugged = PluggedAfterStaging();
        var feedOf = Power().FeedOf;
        var digitsByClass = Power().Digits;
        var here = PositionAfterStaging(deviceKey);
        var sockets = new HashSet<string>(plugged.Keys, StringComparer.Ordinal);
        foreach (var (key, cls) in classes.Where(kv => kv.Key.Length == 32 && kv.Value is not null))
        {
            if (digitsByClass.TryGetValue(cls!, out var digits))
                foreach (var d in digits) sockets.Add(key + d);
        }
        return sockets
            .Where(s => SocketOwner(s) is not { } owner
                        || (owner != deviceKey && classes.ContainsKey(owner) && !PowerLinkEdits.IsUpstreamIn(deviceKey, owner, feedOf)))
            .Select(s =>
            {
                var owner = SocketOwner(s);
                double? d = owner is not null && here is { } a && PositionAfterStaging(owner) is { } b
                    ? Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2))
                    : null;
                var powers = plugged.GetValueOrDefault(s);
                return (s, PowerLinkEdits.SocketLabel(s, owner, classes), d, powers is null ? null : DeviceName(powers, classes));
            })
            .OrderBy(x => x.Item4 is null ? 0 : 1)
            .ThenBy(x => x.d ?? double.MaxValue)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The power cables touching an object after staged changes, as (from device, to device) pairs of
    /// this save's objects: the one feeding it and the ones its outlets feed. For drawing in 3D.
    /// </summary>
    public IReadOnlyList<(string From, string To)> PowerLinksAround(string key)
    {
        // A routed cable runs through cable reroutes (a chain of plugs): the whole run is followed, so
        // selecting any piece of it shows the cable from the source to the device.
        var classes = ClassesWithStaged();
        bool IsReroute(string k) => classes.GetValueOrDefault(k)?.Contains("CableReroute", StringComparison.Ordinal) ?? false;
        var links = PluggedAfterStaging()
            .Where(kv => kv.Value is not null && SocketOwner(kv.Key) is not null)
            .Select(kv => (From: SocketOwner(kv.Key)!, To: kv.Value!))
            .ToList();
        var result = new HashSet<(string, string)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { key };
        var queue = new Queue<string>([key]);
        while (queue.Count > 0 && result.Count < 200)
        {
            var current = queue.Dequeue();
            foreach (var (from, to) in links.Where(l => l.From == current || l.To == current))
            {
                result.Add((from, to));
                var other = from == current ? to : from;
                if (IsReroute(other) && visited.Add(other)) queue.Enqueue(other);
            }
        }
        return [.. result];
    }

    /// <summary>A cable reroute this save can copy (the game's scrap-built cable hook), or null when none is built here.</summary>
    public string? CableRerouteDonor()
        => PlacedObjects.Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.Transform?.Translation is not null
                                    && (o.ClassName?.Contains("CableReroute", StringComparison.Ordinal) ?? false)
                                    && !StagedPlacedDeletions.ContainsKey(o.Key))
            .OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key).FirstOrDefault();

    /// <summary>Spacing used when a route is laid in a straight line (the game's cable rests at 1.6 m and stretches).</summary>
    public const double RouteSpacingCm = 400;

    /// <summary>
    /// Evenly spaced points on the straight line between two places, every <see cref="RouteSpacingCm"/> at most,
    /// not counting the ends (none when they are closer than that).
    /// </summary>
    public static IReadOnlyList<PlacedVector> StraightRoute(PlacedVector from, PlacedVector to)
    {
        var length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2) + Math.Pow(to.Z - from.Z, 2));
        var segments = (int)Math.Ceiling(length / RouteSpacingCm);
        var points = new List<PlacedVector>();
        for (var i = 1; i < segments; i++)
        {
            var t = (double)i / segments;
            points.Add(new PlacedVector(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t), from.Z + ((to.Z - from.Z) * t)));
        }
        return points;
    }

    /// <summary>
    /// Lays a cable route: places a cable reroute (a copy of <paramref name="rerouteDonorKey"/>) at each point,
    /// then plugs <paramref name="socketId"/> into the first, each reroute's outlet into the next, and the last
    /// into <paramref name="deviceKey"/>, the way the game records a routed cable (a chain of plugs). With no
    /// points it is a plain plug. Returns the new reroutes' keys, or null when a reroute could not be placed.
    /// </summary>
    public IReadOnlyList<string>? StagePowerRoute(string socketId, string deviceKey, IReadOnlyList<PlacedVector> points, string rerouteDonorKey)
    {
        var placed = new List<string>();
        foreach (var point in points)
        {
            if (StagePlacedNew(rerouteDonorKey, point) is not { } staged || staged.NewKeys.Values.FirstOrDefault() is not { } key)
            {
                foreach (var dup in _baseEdits.Duplications.Where(d => d.NewKeys.Values.Any(placed.Contains)).ToList()) _baseEdits.RevertDuplication(dup.Id);
                return null;
            }
            placed.Add(key);
        }
        var outlet = FirstOutletOf(rerouteDonorKey) is { } donorOutlet ? donorOutlet[^1..] : "1";
        var from = socketId;
        foreach (var reroute in placed)
        {
            _baseEdits.StagePlug(from, reroute);
            from = reroute + outlet;
        }
        _baseEdits.StagePlug(from, deviceKey);
        PlacedTransformsRevision++;
        UpdateStatus();
        return placed;
    }

    /// <summary>Stages plugging a device into a socket (its current feed is unplugged on SAVE).</summary>
    public StagedPowerLink StagePowerPlug(string socketId, string deviceKey)
    {
        var link = _baseEdits.StagePlug(socketId, deviceKey);
        PlacedTransformsRevision++;
        UpdateStatus();
        return link;
    }

    /// <summary>Stages unplugging whatever a socket powers.</summary>
    public StagedPowerLink StagePowerUnplug(string socketId)
    {
        var link = _baseEdits.StageUnplug(socketId);
        PlacedTransformsRevision++;
        UpdateStatus();
        return link;
    }

    /// <summary>Drops one staged power change.</summary>
    public bool RevertPowerLink(int id)
    {
        var removed = _baseEdits.RevertPowerLink(id);
        if (removed) { PlacedTransformsRevision++; UpdateStatus(); }
        return removed;
    }

    /// <summary>Drops one staged leftover-record removal.</summary>
    public bool RevertSocketCleanup(string socketId)
    {
        var removed = _baseEdits.RevertSocketCleanup(socketId);
        if (removed) { PlacedTransformsRevision++; UpdateStatus(); }
        return removed;
    }

    /// <summary>
    /// Looks for broken power links and leftover outlet records. Reads the other saves of the world first
    /// (read only), since "the device is in another region" is only distinguishable from "the device is
    /// gone" with them; without them only fixes that need no other save are offered.
    /// </summary>
    public async Task<IReadOnlyList<PowerRepairFix>> FindPowerRepairsAsync()
    {
        await LoadOtherSavesAsync().ConfigureAwait(true);
        return PowerRepair.Find(_data, _baseEdits.OtherSaves, key => CurrentPlacedTransform(key)?.Translation);
    }

    /// <summary>Stages the chosen repairs.</summary>
    public void StagePowerRepairs(IEnumerable<PowerRepairFix> fixes)
    {
        _baseEdits.StageRepairs(fixes);
        PlacedTransformsRevision++;
        UpdateStatus();
    }
}
