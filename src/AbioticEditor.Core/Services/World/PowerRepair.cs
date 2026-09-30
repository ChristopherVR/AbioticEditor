using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>What a power repair fixes.</summary>
public enum PowerRepairKind
{
    /// <summary>A socket names a device that exists in no save of the world: unplug it.</summary>
    UnplugMissingDevice,

    /// <summary>A device is plugged into its own outlet: unplug it.</summary>
    UnplugSelfLink,

    /// <summary>Links run in a circle (A powers B powers A): unplug the link that closes it.</summary>
    UnplugLoop,

    /// <summary>A device is fed by more than one socket: keep the nearest, unplug the rest.</summary>
    UnplugExtraFeed,

    /// <summary>An unplugged outlet record whose device no longer exists anywhere: remove the leftover.</summary>
    RemoveLeftoverRecord,
}

/// <summary>One suggested repair. Nothing is changed until it is staged and saved.</summary>
/// <param name="Kind">What it fixes.</param>
/// <param name="SocketId">The socket record it changes.</param>
/// <param name="Description">What is wrong and what the fix does, for players.</param>
/// <param name="Recommended">
/// True when the fix only undoes something the game could not have meant (a device that does not exist,
/// a loop). False when the stored state might be intended (two feeds for one device).
/// </param>
public sealed record PowerRepairFix(PowerRepairKind Kind, string SocketId, string Description, bool Recommended)
{
    /// <summary>Stable id for choosing fixes (the socket and the kind).</summary>
    public string Id => $"{Kind}:{SocketId}";
}

/// <summary>
/// Finds broken power links and leftover outlet records in a world save and proposes a fix for each.
/// Only this save's records are changed; the other saves of the world are read to tell "the device is
/// in another region" (fine) from "the device exists nowhere" (broken). Without the other saves nothing
/// that depends on them is proposed.
/// </summary>
public static class PowerRepair
{
    /// <summary>Proposes fixes. <paramref name="positionOf"/> gives saved positions (cm) for choosing the nearest feed.</summary>
    public static IReadOnlyList<PowerRepairFix> Find(
        WorldSaveData data, IReadOnlyList<(string Name, WorldSaveData Data)> otherSaves, Func<string, PlacedVector?> positionOf)
    {
        var sockets = PlacedPowerRecords.Read(data);
        var objects = PowerLinkEdits.ObjectClasses(data);
        var everywhere = new HashSet<string>(objects.Keys, StringComparer.Ordinal);
        foreach (var (_, other) in otherSaves)
        {
            foreach (var e in WorldMapAccessor.Entries(other.Raw, "DeployedObjectMap")) everywhere.Add(e.Key);
        }
        var scannedWorld = otherSaves.Count > 0;
        var fixes = new List<PowerRepairFix>();
        string Label(string socket) => PowerLinkEdits.SocketLabel(socket, PlacedGroupReferenceAnalyzer.OwnerKeyOf(socket), objects);
        string Device(string key) => objects.TryGetValue(key, out var c) ? $"{PowerLinkEdits.Friendly(c)} ({key[..Math.Min(8, key.Length)]})" : key[..Math.Min(8, key.Length)];

        foreach (var s in sockets)
        {
            // An outlet of a device that exists nowhere: the record is a leftover, plugged or not.
            if (scannedWorld && s.OwnerKey is { } gone && !everywhere.Contains(gone))
            {
                fixes.Add(new(PowerRepairKind.RemoveLeftoverRecord, s.Id, s.Plugged is { } fed && everywhere.Contains(fed)
                    ? $"Outlet {s.Id[^1]} of a device ({gone[..8]}) that is no longer in any save of this world still claims to power {Device(fed)}. Remove the leftover record."
                    : $"Outlet {s.Id[^1]} of a device ({gone[..8]}) that is no longer in any save of this world is still recorded. Remove the leftover record.", true));
                continue;
            }
            if (s.Plugged is { } device)
            {
                if (s.OwnerKey == device)
                {
                    fixes.Add(new(PowerRepairKind.UnplugSelfLink, s.Id, $"{Label(s.Id)} is plugged into itself. Unplug it.", true));
                }
                else if (scannedWorld && PlacedObjectCensus.KeyShape(device) == "guid32" && !everywhere.Contains(device))
                {
                    fixes.Add(new(PowerRepairKind.UnplugMissingDevice, s.Id,
                        $"{Label(s.Id)} powers a device ({device[..8]}) that is not in any save of this world. Unplug it.", true));
                }
            }
        }

        // Two or more sockets feeding one device: keep the nearest (a wall socket when distances are unknown).
        foreach (var group in sockets.Where(s => s.Plugged is not null && s.OwnerKey != s.Plugged).GroupBy(s => s.Plugged!, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var devicePos = positionOf(group.Key);
            double Distance(SocketRecord r)
                => r.OwnerKey is null ? -1
                   : devicePos is { } d && positionOf(r.OwnerKey) is { } o ? Math.Sqrt(Math.Pow(d.X - o.X, 2) + Math.Pow(d.Y - o.Y, 2) + Math.Pow(d.Z - o.Z, 2))
                   : double.MaxValue;
            var keep = group.OrderBy(Distance).First();
            foreach (var extra in group.Where(r => r.Id != keep.Id))
            {
                fixes.Add(new(PowerRepairKind.UnplugExtraFeed, extra.Id,
                    $"{Device(group.Key)} is fed by {group.Count()} sockets. Keep {Label(keep.Id)} and unplug {Label(extra.Id)}.", false));
            }
        }

        // Loops: follow each device's feed upward; a walk that comes back to where it started is a loop.
        var feed = sockets.Where(s => s.Plugged is not null && s.OwnerKey is not null && s.OwnerKey != s.Plugged)
            .GroupBy(s => s.Plugged!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in feed.Keys)
        {
            var path = new List<SocketRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var at = start; feed.TryGetValue(at, out var link) && seen.Add(at); at = link.OwnerKey!)
            {
                path.Add(link);
                if (link.OwnerKey == start)
                {
                    var closing = path.OrderBy(p => p.Id, StringComparer.Ordinal).First();
                    if (reported.Add(closing.Id) && path.All(p => !reported.Contains(p.Id) || p.Id == closing.Id))
                        fixes.Add(new(PowerRepairKind.UnplugLoop, closing.Id,
                            $"{path.Count} devices power each other in a circle. Unplug {Label(closing.Id)} to break it.", true));
                    break;
                }
            }
        }
        return fixes;
    }
}
