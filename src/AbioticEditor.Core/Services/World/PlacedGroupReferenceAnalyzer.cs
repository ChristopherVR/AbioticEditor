using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>What kind of link a reference is.</summary>
public enum GroupReferenceKind
{
    /// <summary>A power socket entry whose id starts with a selected object's key (the socket belongs to that object).</summary>
    PowerSocketOwnedBySelection,

    /// <summary>A power socket (owned by something else) whose plugged-in / extra device is a selected object.</summary>
    PowerSocketTargetsSelection,

    /// <summary>Some other string field elsewhere in a scanned save equals a selected object's key or actor path.</summary>
    KeyReference,
}

/// <summary>One link that touches the selection.</summary>
/// <param name="Kind">See <see cref="GroupReferenceKind"/>.</param>
/// <param name="IsInternal">True when both ends are inside the selection (safe to remap together).</param>
/// <param name="SourceSave">Which scanned save holds the referencing record.</param>
/// <param name="SourceMap">Top-level map of the referencing record.</param>
/// <param name="SourceEntryKey">Key of the referencing map entry.</param>
/// <param name="Field">Field (hash suffix removed) that holds the reference.</param>
/// <param name="SelectedKey">The selected object involved.</param>
/// <param name="OtherEnd">The other end (a device key, or null when unset).</param>
/// <param name="Note">Plain-language explanation.</param>
public sealed record GroupReference(
    GroupReferenceKind Kind,
    bool IsInternal,
    string SourceSave,
    string SourceMap,
    string SourceEntryKey,
    string Field,
    string SelectedKey,
    string? OtherEnd,
    string Note);

/// <summary>Object state that binds to an identity outside the object itself.</summary>
/// <param name="Kind">BedClaim, TeleporterTag or SharedInventory.</param>
/// <param name="ObjectKey">The selected object.</param>
/// <param name="Detail">The claim string, tag, or shared-inventory name.</param>
/// <param name="ExternalPeers">Objects outside the selection bound to the same identity.</param>
/// <param name="Note">Plain-language explanation.</param>
public sealed record GroupIdentityBinding(
    string Kind,
    string ObjectKey,
    string Detail,
    IReadOnlyList<string> ExternalPeers,
    string Note);

/// <summary>The analyzer result.</summary>
public sealed record GroupReferenceReport(
    IReadOnlyList<string> SelectedKeys,
    IReadOnlyList<string> MissingKeys,
    IReadOnlyList<GroupReference> References,
    IReadOnlyList<GroupIdentityBinding> IdentityBindings)
{
    /// <summary>References with both ends inside the selection.</summary>
    public IReadOnlyList<GroupReference> Internal => References.Where(r => r.IsInternal).ToList();

    /// <summary>References that cross the selection boundary (retain, rebind or report before a group operation).</summary>
    public IReadOnlyList<GroupReference> External => References.Where(r => !r.IsInternal).ToList();
}

/// <summary>
/// Read-only analysis for group operations (whole-base copy, cross-world placement, power-network
/// duplication): given a selection of placed objects (<c>DeployedObjectMap</c> keys), lists which
/// references are entirely internal (can be remapped to fresh identities together) and which cross
/// the selection boundary (must be retained, rebound or reported). It never writes.
/// </summary>
/// <remarks>
/// The reference vocabulary is exactly what the fixtures show; see
/// docs/reference/research/base-building-group-operations.md. Anything not listed there is caught only by
/// the generic string scan (<see cref="GroupReferenceKind.KeyReference"/>), so an unknown link kind
/// still surfaces rather than being silently dropped.
/// </remarks>
public static class PlacedGroupReferenceAnalyzer
{
    /// <summary>Length of a deployable GUID key (32 hex chars); a power-socket id is that plus a suffix.</summary>
    private const int GuidKeyLength = 32;

    /// <summary>
    /// Analyzes <paramref name="selectedKeys"/> (keys of <paramref name="primary"/>'s
    /// <c>DeployedObjectMap</c>). <paramref name="otherSaves"/> are additional saves to scan for
    /// references INTO the selection (sibling region saves, the metadata save); pass none for a
    /// single-file analysis.
    /// </summary>
    public static GroupReferenceReport Analyze(
        WorldSaveData primary,
        IReadOnlyCollection<string> selectedKeys,
        string primaryName = "primary",
        IEnumerable<(string Name, WorldSaveData Data)>? otherSaves = null)
    {
        var selection = new HashSet<string>(selectedKeys, StringComparer.Ordinal);
        var present = new HashSet<string>(
            WorldMapAccessor.Entries(primary.Raw, "DeployedObjectMap").Select(e => e.Key), StringComparer.Ordinal);
        var missing = selection.Where(k => !present.Contains(k)).Order(StringComparer.Ordinal).ToList();
        var actorPaths = ActorPathsOf(primary, selection);

        var refs = new List<GroupReference>();
        var scanned = new List<(string Name, WorldSaveData Data)> { (primaryName, primary) };
        if (otherSaves is not null) scanned.AddRange(otherSaves);

        foreach (var (name, data) in scanned)
        {
            ScanPower(name, data, selection, refs);
            ScanGenericStrings(name, data, selection, actorPaths, refs, ReferenceEquals(data, primary));
        }

        return new GroupReferenceReport(
            selection.Order(StringComparer.Ordinal).ToList(), missing, refs,
            IdentityBindings(primary, selection));
    }

    private static Dictionary<string, string> ActorPathsOf(WorldSaveData data, HashSet<string> selection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            if (!selection.Contains(e.Key)) continue;
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is { } a
                && PlacedObjectCensus.FullSoftPath(a.Package, a.Asset, a.SubPath) is { } full)
            {
                result[full] = e.Key;
            }
        }
        return result;
    }

    // ---------- power ----------

    private static void ScanPower(string saveName, WorldSaveData data, HashSet<string> selection, List<GroupReference> refs)
    {
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "PowerSocketMap"))
        {
            var socketId = e.Props.GetString("PowerSocket_") ?? e.Key;
            var owner = OwnerKeyOf(socketId);
            var ownerSelected = owner is not null && selection.Contains(owner);

            var plugged = e.Props.GetString("PluggedInDeviceAssetID_");
            if (string.IsNullOrEmpty(plugged) || plugged == "-1") plugged = null;
            var extras = PlacedObjectCensus.ReadStringArray(e.Props, "ExtraPoweredDeviceAssetIDs_");

            if (ownerSelected)
            {
                // The socket is part of the selected object. Its own state moves with it.
                refs.Add(new GroupReference(
                    GroupReferenceKind.PowerSocketOwnedBySelection, true, saveName, "PowerSocketMap", e.Key,
                    "PowerSocket", owner!, null,
                    "Socket record belongs to a selected object; it is copied with the object and needs a new id."));

                if (plugged is not null)
                {
                    var inside = selection.Contains(plugged);
                    refs.Add(new GroupReference(
                        GroupReferenceKind.PowerSocketOwnedBySelection, inside, saveName, "PowerSocketMap", e.Key,
                        "PluggedInDeviceAssetID", owner!, plugged,
                        inside
                            ? "Plugged device is also selected: remap both ends together."
                            : "Plugged device is outside the selection: retain, rebind or drop this link."));
                }
                foreach (var x in extras)
                {
                    var inside = selection.Contains(x);
                    refs.Add(new GroupReference(
                        GroupReferenceKind.PowerSocketOwnedBySelection, inside, saveName, "PowerSocketMap", e.Key,
                        "ExtraPoweredDeviceAssetIDs", owner!, x,
                        inside
                            ? "Extra powered device is also selected: remap both ends together."
                            : "Extra powered device is outside the selection: retain, rebind or drop this link."));
                }
            }
            else
            {
                // A socket owned by something else that feeds a selected device is an inbound link.
                if (plugged is not null && selection.Contains(plugged))
                {
                    refs.Add(new GroupReference(
                        GroupReferenceKind.PowerSocketTargetsSelection, false, saveName, "PowerSocketMap", e.Key,
                        "PluggedInDeviceAssetID", plugged, owner,
                        "An unselected socket powers a selected device: the copy would lose this feed unless it is rebound."));
                }
                foreach (var x in extras.Where(selection.Contains))
                {
                    refs.Add(new GroupReference(
                        GroupReferenceKind.PowerSocketTargetsSelection, false, saveName, "PowerSocketMap", e.Key,
                        "ExtraPoweredDeviceAssetIDs", x, owner,
                        "An unselected socket lists a selected device as an extra powered device."));
                }
            }
        }
    }

    /// <summary>
    /// The deployable key a power-socket id belongs to: sockets on placed objects are the 32-char
    /// GUID key plus a short suffix (observed: one extra digit). Level-placed sockets use long
    /// path-like ids and return null.
    /// </summary>
    public static string? OwnerKeyOf(string socketId)
    {
        if (socketId.Length <= GuidKeyLength) return null;
        var head = socketId[..GuidKeyLength];
        return head.All(Uri.IsHexDigit) ? head : null;
    }

    // ---------- generic string scan ----------

    private static void ScanGenericStrings(
        string saveName, WorldSaveData data, HashSet<string> selection,
        Dictionary<string, string> actorPaths, List<GroupReference> refs, bool isPrimary)
    {
        foreach (var top in data.Raw.Properties ?? [])
        {
            if (top.Property is not MapProperty mp || mp.Value is null) continue;
            var mapName = top.Name?.Value ?? "?";
            // Power sockets have dedicated handling; the deployable defining record itself is skipped below.
            if (mapName.StartsWith("PowerSocketMap", StringComparison.Ordinal)) continue;

            foreach (var kv in mp.Value)
            {
                var entryKey = WorldSaveReader.ExtractMapKeyString(kv.Key) ?? "";
                var selfEntry = isPrimary && mapName.StartsWith("DeployedObjectMap", StringComparison.Ordinal);
                var selfSelected = selfEntry && selection.Contains(entryKey);

                // Map keys that equal a selected key (e.g. a map keyed by device GUID) in any map but the
                // defining DeployedObjectMap.
                if (!selfEntry && (selection.Contains(entryKey) || actorPaths.ContainsKey(entryKey)))
                {
                    refs.Add(new GroupReference(
                        GroupReferenceKind.KeyReference, false, saveName, mapName, entryKey, "(map key)",
                        selection.Contains(entryKey) ? entryKey : actorPaths[entryKey], null,
                        "A record in another map is keyed by the selected object's identity."));
                }

                Walk(kv.Value, string.Empty, (field, value) =>
                {
                    string? target = null;
                    if (selection.Contains(value)) target = value;
                    else if (actorPaths.TryGetValue(value, out var byPath)) target = byPath;
                    if (target is null) return;
                    // A selected deployable naming itself (its own ActorPath) is not a reference.
                    if (selfSelected && entryKey == target) return;
                    var inside = selfSelected;
                    refs.Add(new GroupReference(
                        GroupReferenceKind.KeyReference, inside, saveName, mapName, entryKey, field, target, null,
                        inside
                            ? "A selected object refers to another selected object."
                            : "A record outside the selection refers to a selected object."));
                });
            }
        }
    }

    private static void Walk(object? node, string field, Action<string, string> onString)
    {
        switch (node)
        {
            case null:
                return;
            case FPropertyTag tag:
                Walk(tag.Property, PlacedObjectCensus.StripHash(tag.Name?.Value ?? "?"), onString);
                return;
            case StructProperty sp:
                Walk(sp.Value, field, onString);
                return;
            case PropertiesStruct ps:
                foreach (var t in ps.Properties) Walk(t, field, onString);
                return;
            case ArrayProperty ap:
                if (ap.Value is { } arr)
                {
                    foreach (var item in arr) Walk(item, field, onString);
                }
                return;
            case MapProperty mp:
                if (mp.Value is { } pairs)
                {
                    foreach (var kv in pairs)
                    {
                        Walk(kv.Key, field, onString);
                        Walk(kv.Value, field, onString);
                    }
                }
                return;
            case FProperty p:
                switch (p.Value)
                {
                    case FString fs when !string.IsNullOrEmpty(fs.Value):
                        onString(field, fs.Value);
                        break;
                    case string s when s.Length > 0:
                        onString(field, s);
                        break;
                    default:
                        break;
                }
                return;
            default:
                return;
        }
    }

    // ---------- identity-bound state ----------

    private static List<GroupIdentityBinding> IdentityBindings(WorldSaveData data, HashSet<string> selection)
    {
        var result = new List<GroupIdentityBinding>();
        var byId = data.Deployables.ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (var key in selection.Order(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(key, out var d)) continue;

            if (d.IsBed && d.HasClaimMarker && d.OwnerId is not null)
            {
                result.Add(new GroupIdentityBinding(
                    "BedClaim", key, d.CustomName ?? "", [],
                    "A claimed bed stores its owner's account id in its name; a copy either keeps the claim "
                    + "(two beds claimed by one player) or must be reset to unclaimed."));
            }
            if (d.ClassName?.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.Add(new GroupIdentityBinding(
                    "SharedInventory", key, "CustomInventoryMap:Void", [],
                    "Every Void chest shows the same shared inventory; copies do not get their own contents."));
            }
        }

        // Teleporter pads: pads sharing a non-zero tag form a network; unselected peers are external.
        var pads = new TeleporterPadFeature();
        if (pads.AppliesTo(data.Raw))
        {
            var entries = pads.Read(data.Raw)
                .Select(e => (e.Key, Freq: FrequencyOf(e)))
                .ToList();
            foreach (var (key, freq) in entries.Where(e => selection.Contains(e.Key) && e.Freq > 0))
            {
                var peers = entries.Where(e => e.Freq == freq && !selection.Contains(e.Key)).Select(e => e.Key).ToList();
                result.Add(new GroupIdentityBinding(
                    "TeleporterTag", key, $"frequency {freq}", peers,
                    peers.Count == 0
                        ? "All pads sharing this tag are inside the selection."
                        : "Pads outside the selection share this tag: a copy with the same tag would join their network."));
            }
        }
        return result;
    }

    private static int FrequencyOf(WorldMapEntry entry)
    {
        var f = entry.Fields.FirstOrDefault(x => string.Equals(x.Id, "frequency", StringComparison.OrdinalIgnoreCase));
        return f is not null && int.TryParse(f.Value?.ToString(), out var v) ? v : 0;
    }
}
