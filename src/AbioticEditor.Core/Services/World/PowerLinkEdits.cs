using System.Globalization;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One staged power change: plug <see cref="DeviceKey"/> into <see cref="SocketId"/>, or unplug the
/// socket when the device is null.
/// </summary>
/// <param name="Id">Staging id (for reverting one change).</param>
/// <param name="SocketId">
/// A <c>PowerSocketMap</c> key: a level socket's actor path, or an outlet (owner GUID plus outlet digit).
/// An outlet with no record yet is allowed when its number is one the same kind of device already uses.
/// </param>
/// <param name="DeviceKey">The device's GUID (its <c>DeployedObjectMap</c> key), or null to unplug.</param>
public sealed record StagedPowerLink(int Id, string SocketId, string? DeviceKey);

/// <summary>One removal of a leftover outlet record (see <see cref="PowerRepair"/>).</summary>
public sealed record StagedSocketCleanup(string SocketId);

/// <summary>What one staged power change would do.</summary>
/// <param name="Id">The staging id.</param>
/// <param name="SocketId">The socket.</param>
/// <param name="SocketLabel">The socket as a player reads it ("PlugStrip outlet 2", "wall socket").</param>
/// <param name="CreatesRecord">True when the outlet has no record yet and one is created.</param>
/// <param name="DeviceBefore">The device plugged in now (null for none).</param>
/// <param name="DeviceAfter">The device plugged in after (null for none).</param>
/// <param name="DeviceLabel">The new device as a player reads it.</param>
/// <param name="FeedsCleared">Other sockets that fed <paramref name="DeviceAfter"/> and are unplugged, since a device has one feed.</param>
/// <param name="DistanceCm">Distance between the socket's owner and the device (null when not known).</param>
/// <param name="Issues">Findings for this change.</param>
public sealed record PowerLinkPreviewRow(
    int Id, string SocketId, string SocketLabel, bool CreatesRecord, string? DeviceBefore, string? DeviceAfter,
    string? DeviceLabel, IReadOnlyList<string> FeedsCleared, double? DistanceCm, IReadOnlyList<BaseEditIssue> Issues)
{
    public bool Blocked => Issues.Any(i => i.IsBlocking);
}

/// <summary>A decided set of power changes, ready to commit.</summary>
internal sealed record PowerLinkPlan(
    IReadOnlyList<PowerLinkPreviewRow> Rows,
    IReadOnlyList<BaseEditIssue> Issues,
    IReadOnlyList<string> Cleanups);

/// <summary>
/// Plans and writes power plug changes. The game stores a plug in exactly one place, the socket
/// record's <c>PluggedInDeviceAssetID_</c> (the device's GUID, or <c>-1</c> for none); the device
/// record has no power member, and on load each socket plugs in the device its record names
/// (<c>PowerSocket_ParentBP_C.DelayedPlugedInDeviceFromSave</c>). The game creates an outlet's record
/// the first time something is plugged into it, and leaves it at <c>-1</c> after an unplug. See
/// docs/reference/research/research-power-network-links.md section 6 for the evidence.
/// </summary>
public static class PowerLinkEdits
{
    /// <summary>Beyond this the preview mentions the cable length (a hint; the game has no known range limit).</summary>
    public const double LongCableHintCm = 1500;

    internal static PowerLinkPlan Plan(
        WorldSaveData data, IReadOnlyList<StagedPowerLink> links, IReadOnlyCollection<string> cleanups,
        IReadOnlySet<string> deleting, Func<string, PlacedVector?> positionOf,
        IReadOnlyList<(string Name, WorldSaveData Data)>? otherSaves = null,
        IReadOnlyList<DuplicationPreviewRow>? copies = null)
    {
        var sockets = PlacedPowerRecords.Read(data).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var objects = ObjectClasses(data);
        var outletNumbers = OutletNumbersByClass(sockets.Values, objects);

        // Objects staged to be placed (copies) count as already there: the copies and their outlet
        // records are written before any plug change, so a new battery or cable reroute can be wired
        // up in the same SAVE. Their outlet records are the ones the copy makes.
        var pendingSockets = new Dictionary<string, string?>(StringComparer.Ordinal);
        var pendingPositions = new Dictionary<string, PlacedVector>(StringComparer.Ordinal);
        foreach (var copy in copies ?? [])
        {
            if (copy.Blocked || copy.NewKey.Length == 0) continue;
            objects[copy.NewKey] = copy.ClassName;
            if (copy.After?.Translation is { } at) pendingPositions[copy.NewKey] = at;
            foreach (var s in copy.Sockets)
                pendingSockets[s.NewId] = s.PluggedAfter is { } after && after != WorldSaveWriter.NoPluggedDevice ? after : null;
        }
        var basePosition = positionOf;
        positionOf = key => pendingPositions.TryGetValue(key, out var at) ? at : basePosition(key);
        var rows = new List<PowerLinkPreviewRow>();
        var all = new List<BaseEditIssue>();

        // The rest of the world, read only: player-built devices live in the Facility save while wall
        // sockets live in the region save of their level, so a plug often joins two files. Only the
        // socket's own record (in this save) is written; a device's feed in another file is not.
        var labels = new Dictionary<string, string?>(objects, StringComparer.Ordinal);
        var externalFeeds = new Dictionary<string, List<(string Socket, string File)>>(StringComparer.Ordinal);
        var worldPlugged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, other) in otherSaves ?? [])
        {
            foreach (var (key, cls) in ObjectClasses(other)) labels.TryAdd(key, cls);
            foreach (var s in PlacedPowerRecords.Read(other))
            {
                worldPlugged[s.Id] = s.Plugged;
                if (s.Plugged is { } d)
                {
                    if (!externalFeeds.TryGetValue(d, out var list)) externalFeeds[d] = list = [];
                    list.Add((s.Id, name));
                }
            }
        }

        // Later changes see earlier ones (plug A into S1, then B into S1 replaces A), so work on a copy.
        var plugged = sockets.Values.ToDictionary(s => s.Id, s => s.Plugged, StringComparer.Ordinal);
        foreach (var (id, device) in pendingSockets) plugged[id] = device;

        foreach (var link in links)
        {
            var issues = new List<BaseEditIssue>();
            var exists = sockets.ContainsKey(link.SocketId) || pendingSockets.ContainsKey(link.SocketId);
            var owner = PlacedGroupReferenceAnalyzer.OwnerKeyOf(link.SocketId);
            var socketLabel = SocketLabel(link.SocketId, owner, objects);
            var createsRecord = false;

            if (!exists)
            {
                if (owner is null)
                {
                    issues.Add(Block("unknown-socket", $"{socketLabel} is not in this save.", link.SocketId));
                }
                else if (!objects.TryGetValue(owner, out var ownerClass))
                {
                    issues.Add(Block("socket-owner-missing", $"{socketLabel}: the device it belongs to is not in this save.", link.SocketId));
                }
                else if (link.DeviceKey is null)
                {
                    issues.Add(Block("nothing-to-unplug", $"{socketLabel} has nothing plugged in.", link.SocketId));
                }
                else if (!outletNumbers.TryGetValue(ownerClass ?? "", out var numbers) || !numbers.Contains(link.SocketId[^1]))
                {
                    issues.Add(Block("unknown-outlet",
                        $"{socketLabel}: no {Friendly(ownerClass)} in this save uses outlet {link.SocketId[^1]}, so it is not known to exist.", link.SocketId));
                }
                else
                {
                    createsRecord = true;
                }
            }
            else if (owner is not null && !objects.ContainsKey(owner) && link.DeviceKey is not null)
            {
                issues.Add(Block("socket-owner-missing", $"{socketLabel}: the device it belongs to is no longer in this save.", link.SocketId));
            }

            if (owner is not null && deleting.Contains(owner))
                issues.Add(Block("socket-owner-deleted", $"{socketLabel} belongs to an object staged for deletion.", link.SocketId));

            var before = plugged.GetValueOrDefault(link.SocketId);
            var feedsCleared = new List<string>();
            double? distance = null;
            string? deviceLabel = null;

            if (link.DeviceKey is { } device)
            {
                deviceLabel = labels.TryGetValue(device, out var cls) ? $"{Friendly(cls)} ({Short(device)})" : Short(device);
                if (PlacedObjectCensus.KeyShape(device) != "guid32" || !labels.ContainsKey(device))
                    issues.Add(Block("device-missing", $"The device {Short(device)} is not a placed object in this world's saves.", link.SocketId));
                if (externalFeeds.TryGetValue(device, out var elsewhere))
                {
                    var (feedSocket, feedFile) = elsewhere[0];
                    issues.Add(Block("feed-in-other-save",
                        $"{deviceLabel} is plugged into {SocketLabel(feedSocket, PlacedGroupReferenceAnalyzer.OwnerKeyOf(feedSocket), labels)} in {feedFile}. "
                        + "Unplug it there first (a device takes power from one place, and only this save is written).", link.SocketId));
                }
                if (deleting.Contains(device))
                    issues.Add(Block("device-deleted", $"{deviceLabel} is staged for deletion.", link.SocketId));
                if (string.Equals(device, owner, StringComparison.Ordinal))
                    issues.Add(Block("self-plug", $"{deviceLabel} cannot be plugged into its own outlet.", link.SocketId));
                if (owner is not null && IsUpstream(device, owner, Merge(worldPlugged, plugged)))
                    issues.Add(Block("power-loop", $"{deviceLabel} already powers {socketLabel}'s device, so plugging it in there would make a loop.", link.SocketId));

                if (before is { } previous && previous != device)
                    issues.Add(Warn("replaces-device",
                        $"{socketLabel} currently powers {DeviceLabel(previous, labels)}; that device is unplugged.", link.SocketId));

                // A device has one feed: every other socket naming it is unplugged.
                foreach (var (id, p) in plugged)
                {
                    if (id != link.SocketId && p == device) feedsCleared.Add(id);
                }
                if (feedsCleared.Count > 0)
                    issues.Add(Info("feed-moved",
                        $"{deviceLabel} is unplugged from {string.Join(", ", feedsCleared.Select(f => SocketLabel(f, PlacedGroupReferenceAnalyzer.OwnerKeyOf(f), objects)))} first.", link.SocketId));

                if (owner is not null && positionOf(owner) is { } a && positionOf(device) is { } b)
                {
                    distance = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
                    if (distance > LongCableHintCm)
                        issues.Add(Info("long-cable", string.Create(CultureInfo.InvariantCulture,
                            $"The cable runs {distance.Value / 100:F0} m. The game draws it stretched to the device."), link.SocketId));
                }
            }
            else if (exists && before is null)
            {
                issues.Add(Info("already-unplugged", $"{socketLabel} has nothing plugged in.", link.SocketId));
            }

            if (!issues.Any(i => i.IsBlocking))
            {
                foreach (var f in feedsCleared) plugged[f] = null;
                plugged[link.SocketId] = link.DeviceKey;
            }
            rows.Add(new PowerLinkPreviewRow(
                link.Id, link.SocketId, socketLabel, createsRecord, before, link.DeviceKey, deviceLabel,
                feedsCleared, distance, issues));
            all.AddRange(issues);
        }

        foreach (var id in cleanups)
        {
            if (!sockets.TryGetValue(id, out var s))
                all.Add(Block("cleanup-missing", $"Outlet record {Short(id)} is not in this save.", id));
            else if (s.Plugged is { } device)
                all.Add(Warn("cleanup-plugged", $"Outlet record {Short(id)} is removed; {DeviceLabel(device, objects)} loses that feed.", id));
        }
        return new PowerLinkPlan(rows, all, [.. cleanups]);
    }

    /// <summary>Writes a plan into the save (the caller validated it). Returns records created, removed and changed.</summary>
    internal static (int Created, int Removed, int Changed) Commit(WorldSaveData data, PowerLinkPlan plan)
    {
        int created = 0, removed = 0, changed = 0;
        var templates = new TemplateSource(data.Raw);
        foreach (var row in plan.Rows)
        {
            foreach (var feed in row.FeedsCleared)
            {
                if (WorldMapAccessor.FindEntry(data.Raw, "PowerSocketMap", feed) is { } fp
                    && row.DeviceAfter is { } d
                    && WorldSaveWriter.UnplugDevices(fp, new HashSet<string>(StringComparer.Ordinal) { d })) changed++;
            }
            var props = WorldMapAccessor.FindEntry(data.Raw, "PowerSocketMap", row.SocketId);
            if (props is null)
            {
                if (!row.CreatesRecord || row.DeviceAfter is null) continue;
                WorldSaveWriter.AppendMapPair(data.Raw, "PowerSocketMap", NewOutletRecord(templates.NextRecord(), row.SocketId, row.DeviceAfter));
                created++;
                continue;
            }
            if (SetPlugged(props, row.DeviceAfter ?? WorldSaveWriter.NoPluggedDevice, templates)) changed++;
        }
        foreach (var id in plan.Cleanups)
        {
            if (WorldSaveWriter.RemovePowerSocketRecord(data, id)) removed++;
        }
        return (created, removed, changed);
    }

    /// <summary>
    /// Detached socket records to copy from, taken from a serialized copy of the save (the same "donor"
    /// idea the object copier uses), so every member keeps the game's own name and type. Each record is
    /// handed out once; a new donor is made when they run out.
    /// </summary>
    private sealed class TemplateSource(SaveGame live)
    {
        private readonly Queue<KeyValuePair<FProperty, FProperty>> _records = new();

        public KeyValuePair<FProperty, FProperty> NextRecord()
        {
            if (_records.Count == 0) Refill();
            return _records.Dequeue();
        }

        private void Refill()
        {
            var donor = PlacedObjectCloner.CreateDonor(live);
            var pairs = WorldMapAccessor.GetPairs(donor, "PowerSocketMap") ?? [];
            // Prefer outlet records that carry the plugged-device member: the shape the game writes for an outlet.
            var usable = pairs
                .Where(p => p.Value is StructProperty { Value: PropertiesStruct sp } && sp.Properties.FindByPrefix("PluggedInDeviceAssetID_") is not null)
                .OrderBy(p => PlacedGroupReferenceAnalyzer.OwnerKeyOf(WorldSaveReader.ExtractMapKeyString(p.Key) ?? "") is null ? 1 : 0)
                .ToList();
            if (usable.Count == 0) throw new InvalidOperationException("No socket record in this save carries a plugged-device member to copy.");
            foreach (var p in usable) _records.Enqueue(p);
        }
    }

    /// <summary>
    /// Sets <c>PluggedInDeviceAssetID_</c>. A record that omits it (delta serialization: the field is at its
    /// default) gets the member copied, with its exact name, from another record.
    /// </summary>
    private static bool SetPlugged(IList<FPropertyTag> props, string value, TemplateSource templates)
    {
        if (props.FindByPrefix("PluggedInDeviceAssetID_")?.Property is { } leaf)
        {
            if (leaf.Value?.ToString() == value) return false;
            leaf.Value = new FString(value);
            return true;
        }
        var donorProps = ((PropertiesStruct)((StructProperty)templates.NextRecord().Value).Value!).Properties;
        var tag = donorProps.First(t => t.Name?.Value?.StartsWith("PluggedInDeviceAssetID_", StringComparison.Ordinal) == true);
        tag.Property!.Value = new FString(value);
        var none = props.FirstOrDefault(t => t.IsNone);
        props.Insert(none is null ? props.Count : props.IndexOf(none), tag);
        return true;
    }

    /// <summary>A new outlet record from a detached donor record: id, device and timer reset.</summary>
    private static KeyValuePair<FProperty, FProperty> NewOutletRecord(KeyValuePair<FProperty, FProperty> pair, string socketId, string device)
    {
        pair.Key.Value = new FString(socketId);
        var props = ((PropertiesStruct)((StructProperty)pair.Value).Value!).Properties;
        if (props.FindByPrefix("PowerSocket_")?.Property is { } id) id.Value = new FString(socketId);
        if (props.FindByPrefix("PluggedInDeviceAssetID_")?.Property is { } plug) plug.Value = new FString(device);
        if (props.FindByPrefix("ExtraPoweredDeviceAssetIDs_")?.Property is ArrayProperty extras)
            WorldSaveWriter.FilterStringArray(extras, _ => false);
        if (props.FindByPrefix("HasTimer_")?.Property is { } timer) timer.Value = false;
        return pair;
    }

    private static Dictionary<string, string?> Merge(Dictionary<string, string?> world, Dictionary<string, string?> local)
    {
        var merged = new Dictionary<string, string?>(world, StringComparer.Ordinal);
        foreach (var (k, v) in local) merged[k] = v;
        return merged;
    }

    /// <summary>True when <paramref name="device"/> feeds <paramref name="target"/>, directly or through a chain.</summary>
    public static bool IsUpstream(string device, string target, IReadOnlyDictionary<string, string?> plugged)
        => IsUpstreamIn(device, target, FeedOf(plugged));

    /// <summary>
    /// Device to the device that feeds it, from a socket-to-device map. Build it once and use the
    /// <see cref="IsUpstreamIn"/> when
    /// checking many devices against the same state.
    /// </summary>
    public static Dictionary<string, string> FeedOf(IReadOnlyDictionary<string, string?> plugged)
    {
        var feedOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (socket, p) in plugged)
        {
            if (p is not null && PlacedGroupReferenceAnalyzer.OwnerKeyOf(socket) is { } owner) feedOf.TryAdd(p, owner);
        }
        return feedOf;
    }

    /// <summary>True when <paramref name="device"/> feeds <paramref name="target"/>, directly or through other devices.</summary>
    public static bool IsUpstreamIn(string device, string target, IReadOnlyDictionary<string, string> feedOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var at = target; feedOf.TryGetValue(at, out var up) && seen.Add(at); at = up)
        {
            if (up == device) return true;
        }
        return false;
    }

    /// <summary>
    /// The outlet digits each device class uses in this save, read from the outlet records present. The
    /// game creates an outlet's record on first plug, so any digit seen for a class is a real outlet of
    /// that class. Nothing here is a list of class names.
    /// </summary>
    /// <summary>The outlet digits each device class uses in <paramref name="data"/> (see the overload).</summary>
    public static Dictionary<string, HashSet<char>> OutletNumbersByClass(WorldSaveData data)
        => OutletNumbersByClass(PlacedPowerRecords.Read(data), ObjectClasses(data));

    internal static Dictionary<string, HashSet<char>> OutletNumbersByClass(
        IEnumerable<SocketRecord> sockets, IReadOnlyDictionary<string, string?> objects)
    {
        var result = new Dictionary<string, HashSet<char>>(StringComparer.Ordinal);
        foreach (var s in sockets)
        {
            if (s.OwnerKey is null || !objects.TryGetValue(s.OwnerKey, out var cls) || cls is null) continue;
            if (!result.TryGetValue(cls, out var set)) result[cls] = set = [];
            set.Add(s.Id[^1]);
        }
        return result;
    }

    /// <summary>Every placed object's class name by key.</summary>
    public static Dictionary<string, string?> ObjectClasses(WorldSaveData data)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            result[e.Key] = PlacedObjectCensus.ClassNameOf(PlacedObjectCensus.ClassPathOf(e.Props));
        }
        return result;
    }

    /// <summary>"PlugStrip outlet 2" for an outlet, "wall socket" for a level socket.</summary>
    public static string SocketLabel(string socketId, string? owner, IReadOnlyDictionary<string, string?> objects)
        => owner is null
            ? $"wall socket {socketId[(socketId.LastIndexOf('.') + 1)..]}"
            : $"{Friendly(objects.GetValueOrDefault(owner))} ({Short(owner)}) outlet {socketId[^1]}";

    private static string DeviceLabel(string key, Dictionary<string, string?> objects)
        => objects.TryGetValue(key, out var cls) ? $"{Friendly(cls)} ({Short(key)})" : Short(key);

    /// <summary>A class name as players read it ("Deployed_PlugStrip_C" gives "PlugStrip").</summary>
    public static string Friendly(string? cls)
    {
        if (string.IsNullOrEmpty(cls)) return "device";
        if (cls.EndsWith("_C", StringComparison.Ordinal)) cls = cls[..^2];
        return cls.Replace("Deployed_", "", StringComparison.Ordinal).Replace("Deployable_", "", StringComparison.Ordinal).Replace('_', ' ');
    }

    private static string Short(string key) => key.Length > 8 && !key.Contains('/', StringComparison.Ordinal) ? key[..8] : key[(key.LastIndexOf('.') + 1)..];

    private static BaseEditIssue Block(string code, string message, string key) => new(BaseEditSeverity.Blocking, code, message, key);
    private static BaseEditIssue Warn(string code, string message, string key) => new(BaseEditSeverity.Warning, code, message, key);
    private static BaseEditIssue Info(string code, string message, string key) => new(BaseEditSeverity.Info, code, message, key);
}
