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
public sealed record PowerCandidate(string Key, string Label, double? DistanceCm, bool CurrentlyPowered);

// Power rerouting and repair for the Power Sockets tab (and the 3D inspector). Everything stages in the
// same StagedBaseEdits the base editor uses, so it previews, validates and saves with the other base
// edits in one all-or-nothing apply that keeps a .bak.
public sealed partial class WorldSaveSession
{
    /// <summary>The device whose outlets a socket record belongs to (null for a wall socket).</summary>
    public static string? SocketOwner(string socketId) => PlacedGroupReferenceAnalyzer.OwnerKeyOf(socketId);

    /// <summary>Which device each socket powers after the staged power changes (a what-if over the save).</summary>
    private Dictionary<string, string?> PluggedAfterStaging()
    {
        var plugged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(_data.Raw, "PowerSocketMap"))
        {
            var p = e.Props.GetString("PluggedInDeviceAssetID_");
            plugged[e.Key] = string.IsNullOrEmpty(p) || p == "-1" ? null : p;
        }
        foreach (var cleaned in _baseEdits.SocketCleanups) plugged.Remove(cleaned);
        foreach (var row in PreviewBaseEdits().PowerLinks.Where(r => !r.Blocked))
        {
            foreach (var f in row.FeedsCleared) plugged[f] = null;
            plugged[row.SocketId] = row.DeviceAfter;
        }
        return plugged;
    }

    /// <summary>
    /// Every outlet of the device that owns <paramref name="socketId"/> (recorded or not yet recorded),
    /// or just the socket itself for a wall socket, with what each powers after staged changes.
    /// </summary>
    public IReadOnlyList<PowerOutletView> PowerOutletsAround(string socketId)
    {
        var classes = PowerLinkEdits.ObjectClasses(_data);
        var plugged = PluggedAfterStaging();
        var staged = _baseEdits.PowerLinks.Select(l => l.SocketId).Concat(_baseEdits.SocketCleanups)
            .Concat(PreviewBaseEdits().PowerLinks.SelectMany(r => r.FeedsCleared)).ToHashSet(StringComparer.Ordinal);
        var owner = SocketOwner(socketId);
        var ids = new SortedSet<string>(StringComparer.Ordinal) { socketId };
        if (owner is not null)
        {
            foreach (var id in plugged.Keys.Where(k => SocketOwner(k) == owner)) ids.Add(id);
            if (classes.TryGetValue(owner, out var cls) && cls is not null
                && PowerLinkEdits.OutletNumbersByClass(_data).TryGetValue(cls, out var digits))
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
    public IReadOnlyList<PowerCandidate> PowerCandidatesFor(string socketId, bool includeAll = false, int max = 40)
    {
        var classes = PowerLinkEdits.ObjectClasses(_data);
        var plugged = PluggedAfterStaging();
        var usesPower = includeAll ? null : PoweredClasses(classes);
        var powered = plugged.Values.Where(v => v is not null).ToHashSet(StringComparer.Ordinal);
        var owner = SocketOwner(socketId);
        var origin = owner is null ? null : CurrentPlacedTransform(owner)?.Translation;
        var deleting = StagedPlacedDeletions.Keys.ToHashSet(StringComparer.Ordinal);
        return PlacedObjects
            .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.Key != owner && !deleting.Contains(o.Key)
                        && (usesPower is null || (o.ClassName is { } cn && usesPower.Contains(cn)))
                        && (owner is null || !PowerLinkEdits.IsUpstream(o.Key, owner, plugged)))
            .Select(o =>
            {
                double? d = origin is { } a && CurrentPlacedTransform(o.Key)?.Translation is { } b
                    ? Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2))
                    : null;
                return new PowerCandidate(o.Key, DeviceName(o.Key, classes), d, powered.Contains(o.Key));
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

    private static string DeviceName(string key, Dictionary<string, string?> classes)
        => $"{PowerLinkEdits.Friendly(classes.GetValueOrDefault(key))} ({key[..Math.Min(8, key.Length)]})";

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
