using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Per-kind decisions for deleting placed objects. The defaults are the safe ones: anything outside the
/// deleted set that still points at an object refuses the deletion.
/// </summary>
public sealed record DeletePolicy
{
    /// <summary>
    /// The outlet records the object owns in the power table. <see cref="ReferencePolicy.Drop"/> (default)
    /// removes them with the object; <see cref="ReferencePolicy.Keep"/> leaves them orphaned (the fixtures
    /// show the game already leaves such records behind); <see cref="ReferencePolicy.Refuse"/> stops when
    /// one of them still feeds a device that is not being deleted.
    /// </summary>
    public ReferencePolicy OwnedSocketRecords { get; init; } = ReferencePolicy.Drop;

    /// <summary>
    /// Sockets outside the deleted set that plug into (or list as an extra device) a deleted object.
    /// Default <see cref="ReferencePolicy.Refuse"/>. <see cref="ReferencePolicy.Drop"/> unplugs them (only
    /// possible for records in the save being edited); <see cref="ReferencePolicy.Keep"/> leaves them dangling.
    /// </summary>
    public ReferencePolicy InboundPlugs { get; init; } = ReferencePolicy.Refuse;

    /// <summary>
    /// Any other record that names a deleted object (found by the generic scan). It cannot be rewritten
    /// safely, so only <see cref="ReferencePolicy.Refuse"/> (default) and <see cref="ReferencePolicy.Keep"/> are honoured.
    /// </summary>
    public ReferencePolicy OtherReferences { get; init; } = ReferencePolicy.Refuse;

    /// <summary>A bed that a player has claimed. Default <see cref="ReferencePolicy.Refuse"/>; Drop and Keep both delete it.</summary>
    public ReferencePolicy BedClaims { get; init; } = ReferencePolicy.Refuse;

    /// <summary>A teleporter pad whose tag is shared by pads that are not being deleted. Default <see cref="ReferencePolicy.Refuse"/>.</summary>
    public ReferencePolicy TeleporterPeers { get; init; } = ReferencePolicy.Refuse;

    /// <summary>A Void chest (shared inventory that outlives the chest). Default <see cref="ReferencePolicy.Keep"/>.</summary>
    public ReferencePolicy SharedInventories { get; init; } = ReferencePolicy.Keep;

    /// <summary>The safe default policy.</summary>
    public static DeletePolicy Default { get; } = new();
}

/// <summary>What deleting one object would do.</summary>
public sealed record DeletionPreviewRow(
    string Key,
    string? ClassName,
    PlacedObjectTransform? Transform,
    IReadOnlyList<StoredItemRef> Contents,
    int ItemProxyCount,
    IReadOnlyList<string> OwnedSocketIds,
    IReadOnlyList<string> DownstreamDeviceKeys,
    IReadOnlyList<GroupReference> InboundLinks,
    IReadOnlyList<GroupIdentityBinding> Bindings,
    IReadOnlyList<string> Actions,
    IReadOnlyList<BaseEditIssue> Issues)
{
    /// <summary>True when something stops this object from being deleted as staged.</summary>
    public bool Blocked => Issues.Any(i => i.IsBlocking);
}

/// <summary>The result of planning a set of deletions (reads only).</summary>
public sealed class DeletionPlan
{
    internal DeletionPlan(
        IReadOnlyList<DeletionPreviewRow> rows, IReadOnlyList<BaseEditIssue> issues, GroupReferenceReport report,
        IReadOnlyList<string> deleteKeys, IReadOnlyList<string> socketRecordsToRemove,
        IReadOnlyDictionary<string, HashSet<string>> unplugEdits)
    {
        Rows = rows;
        Issues = issues;
        Report = report;
        DeleteKeys = deleteKeys;
        SocketRecordsToRemove = socketRecordsToRemove;
        UnplugEdits = unplugEdits;
    }

    /// <summary>One row per object, in staging order.</summary>
    public IReadOnlyList<DeletionPreviewRow> Rows { get; }

    /// <summary>Every finding (per-object findings are also on their row) plus scope notes.</summary>
    public IReadOnlyList<BaseEditIssue> Issues { get; }

    /// <summary>The group analyzer's full report for the deleted set.</summary>
    public GroupReferenceReport Report { get; }

    internal IReadOnlyList<string> DeleteKeys { get; }

    internal IReadOnlyList<string> SocketRecordsToRemove { get; }

    internal IReadOnlyDictionary<string, HashSet<string>> UnplugEdits { get; }

    /// <summary>True when no finding blocks the deletion.</summary>
    public bool CanApply => !Issues.Any(i => i.IsBlocking);
}

/// <summary>
/// Plans and performs deletion of player-built placed objects (GUID-keyed <c>DeployedObjectMap</c> entries).
/// Level-placed actor-path entries are refused. Items stored inside the object's own container inventories
/// are deleted with it (and listed in the preview); anything outside the object that still points at it is
/// handled by the per-kind <see cref="DeletePolicy"/>.
/// </summary>
public static class PlacedObjectDeletion
{
    /// <summary>Builds the plan. Never writes.</summary>
    public static DeletionPlan Plan(
        WorldSaveData data,
        IReadOnlyList<(string Key, DeletePolicy Policy)> targets,
        string primaryName = "primary",
        IEnumerable<(string Name, WorldSaveData Data)>? otherSaves = null)
    {
        var others = otherSaves?.ToList() ?? [];
        var keys = targets.Select(t => t.Key).Distinct(StringComparer.Ordinal).ToList();
        var selection = new HashSet<string>(keys, StringComparer.Ordinal);
        var report = PlacedGroupReferenceAnalyzer.Analyze(data, keys, primaryName, others);
        var sockets = PlacedPowerRecords.Read(data);
        var rows = new List<DeletionPreviewRow>();
        var all = new List<BaseEditIssue>();
        var socketsToRemove = new List<string>();
        var unplug = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        if (others.Count == 0)
        {
            all.Add(new BaseEditIssue(BaseEditSeverity.Info, "scan-scope",
                "Only this save was scanned for references. Links stored in other region saves or the metadata save are not visible."));
        }

        foreach (var (key, policy) in targets.DistinctBy(t => t.Key, StringComparer.Ordinal))
        {
            var issues = new List<BaseEditIssue>();
            var actions = new List<string>();
            var props = WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", key);
            if (props is null)
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "not-in-save", "Object is not in this save.", key));
                rows.Add(Row(key, null, null, [], 0, [], [], [], [], actions, issues));
                all.AddRange(issues);
                continue;
            }

            var className = PlacedObjectCensus.ClassNameOf(PlacedObjectCensus.ClassPathOf(props));
            var transform = PlacedObjectCensus.ReadTransform(props);
            if (PlacedObjectCensus.KeyShape(key) != "guid32" || props.TryGetBool("DeployedByPlayer_") != true)
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "level-placed",
                    "Level-placed object: the level recreates it, so deleting its entry is not a deletion. Refused.", key));
            }

            var contents = PlacedObjectContents.ReadItems(props);
            var proxies = PlacedObjectContents.ProxyCount(props);
            if (contents.Count > 0)
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "contents-deleted",
                    $"{contents.Sum(c => Math.Max(c.Count, 1))} stored item(s) in {contents.Count} slot(s) are deleted with the object.", key));
            }
            if (proxies > 0)
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "proxies-deleted",
                    $"{proxies} planted or spawned item proxy(ies) are deleted with the object.", key));
            }
            actions.Add("Remove the object entry.");

            // Outlet records this object owns.
            var owned = sockets.Where(s => s.OwnerKey == key).ToList();
            var downstream = owned
                .SelectMany(s => (s.Plugged is null ? [] : new[] { s.Plugged }).Concat(s.Extras))
                .Where(d => !selection.Contains(d)).Distinct(StringComparer.Ordinal).ToList();
            if (owned.Count > 0)
            {
                switch (policy.OwnedSocketRecords)
                {
                    case ReferencePolicy.Drop:
                        socketsToRemove.AddRange(owned.Select(s => s.Id));
                        actions.Add($"Remove {owned.Count} power outlet record(s) the object owns.");
                        break;
                    case ReferencePolicy.Keep:
                        actions.Add($"Leave {owned.Count} power outlet record(s) behind (orphaned).");
                        break;
                    default:
                        if (downstream.Count > 0)
                        {
                            issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "downstream-device",
                                $"{downstream.Count} device(s) are still plugged into this object's outlets; unplug them or choose to drop the outlet records.", key));
                        }
                        else
                        {
                            socketsToRemove.AddRange(owned.Select(s => s.Id));
                            actions.Add($"Remove {owned.Count} unused power outlet record(s) the object owns.");
                        }
                        break;
                }
                if (downstream.Count > 0 && policy.OwnedSocketRecords != ReferencePolicy.Refuse)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "downstream-device",
                        $"{downstream.Count} device(s) plugged into this object's outlets lose their feed.", key));
                }
            }

            // Inbound: sockets outside the deleted set that feed this object.
            var inbound = report.References
                .Where(r => r.Kind == GroupReferenceKind.PowerSocketTargetsSelection && r.SelectedKey == key)
                .ToList();
            foreach (var link in inbound)
            {
                switch (policy.InboundPlugs)
                {
                    case ReferencePolicy.Refuse:
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "inbound-plug",
                            $"Socket {Short(link.SourceEntryKey)} ({link.SourceSave}) still feeds this object.", key));
                        break;
                    case ReferencePolicy.Drop when link.SourceSave == primaryName && link.SourceMap == "PowerSocketMap":
                        if (!unplug.TryGetValue(link.SourceEntryKey, out var set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            unplug[link.SourceEntryKey] = set;
                        }
                        set.Add(key);
                        actions.Add($"Unplug socket {Short(link.SourceEntryKey)}.");
                        break;
                    case ReferencePolicy.Drop:
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "cross-save-link",
                            $"Socket {Short(link.SourceEntryKey)} lives in {link.SourceSave}, which this edit does not write. Edit that save, or keep the dangling link.", key));
                        break;
                    default:
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "dangling-plug",
                            $"Socket {Short(link.SourceEntryKey)} ({link.SourceSave}) will point at a deleted object.", key));
                        break;
                }
            }

            foreach (var link in report.References.Where(r =>
                r.Kind == GroupReferenceKind.KeyReference && !r.IsInternal && r.SelectedKey == key))
            {
                var where = $"{link.SourceMap}[{Short(link.SourceEntryKey)}].{link.Field} ({link.SourceSave})";
                issues.Add(policy.OtherReferences == ReferencePolicy.Keep
                    ? new BaseEditIssue(BaseEditSeverity.Warning, "external-reference", $"{where} names this object and will dangle.", key)
                    : new BaseEditIssue(BaseEditSeverity.Blocking, "external-reference", $"{where} names this object.", key));
            }

            var bindings = report.IdentityBindings.Where(b => b.ObjectKey == key).ToList();
            foreach (var b in bindings)
            {
                switch (b.Kind)
                {
                    case "BedClaim":
                        issues.Add(policy.BedClaims == ReferencePolicy.Refuse
                            ? new BaseEditIssue(BaseEditSeverity.Blocking, "bed-claim", "The bed is claimed by a player; deleting it discards the claim.", key)
                            : new BaseEditIssue(BaseEditSeverity.Warning, "bed-claim", "The bed is claimed by a player; the claim is discarded with it (player saves are not touched).", key));
                        break;
                    case "TeleporterTag" when b.ExternalPeers.Count > 0:
                        issues.Add(policy.TeleporterPeers == ReferencePolicy.Refuse
                            ? new BaseEditIssue(BaseEditSeverity.Blocking, "teleporter-peers", $"{b.ExternalPeers.Count} pad(s) not being deleted share this pad's tag ({b.Detail}).", key)
                            : new BaseEditIssue(BaseEditSeverity.Warning, "teleporter-peers", $"{b.ExternalPeers.Count} pad(s) sharing tag ({b.Detail}) are left unpaired.", key));
                        break;
                    case "SharedInventory":
                        issues.Add(policy.SharedInventories == ReferencePolicy.Refuse
                            ? new BaseEditIssue(BaseEditSeverity.Blocking, "shared-inventory", "This chest shows a shared inventory that other chests also show.", key)
                            : new BaseEditIssue(BaseEditSeverity.Info, "shared-inventory", "Shared (Void) inventory contents are not stored in the chest and are not deleted.", key));
                        break;
                    default:
                        break;
                }
            }

            rows.Add(Row(key, className, transform, contents, proxies, owned.Select(s => s.Id).ToList(), downstream,
                inbound, bindings, actions, issues));
            all.AddRange(issues);
        }

        return new DeletionPlan(
            rows, all, report, keys, socketsToRemove.Distinct(StringComparer.Ordinal).ToList(), unplug);
    }

    private static DeletionPreviewRow Row(
        string key, string? cls, PlacedObjectTransform? transform, IReadOnlyList<StoredItemRef> contents, int proxies,
        IReadOnlyList<string> owned, IReadOnlyList<string> downstream, IReadOnlyList<GroupReference> inbound,
        IReadOnlyList<GroupIdentityBinding> bindings, IReadOnlyList<string> actions, IReadOnlyList<BaseEditIssue> issues)
        => new(key, cls, transform, contents, proxies, owned, downstream, inbound, bindings, actions, issues);

    private static string Short(string id) => id.Length > 40 ? "..." + id[^32..] : id;

    /// <summary>
    /// Applies a plan that has no blocking findings: unplugs the chosen inbound sockets, removes the owned
    /// outlet records, then removes the object entries. Returns the number of object entries removed.
    /// </summary>
    internal static int Commit(WorldSaveData data, DeletionPlan plan)
    {
        foreach (var (socketId, devices) in plan.UnplugEdits)
        {
            if (WorldMapAccessor.FindEntry(data.Raw, "PowerSocketMap", socketId) is { } props)
            {
                WorldSaveWriter.UnplugDevices(props, devices);
            }
        }
        foreach (var id in plan.SocketRecordsToRemove)
        {
            WorldSaveWriter.RemovePowerSocketRecord(data, id);
        }
        var removed = 0;
        foreach (var key in plan.DeleteKeys)
        {
            if (WorldSaveWriter.RemovePlacedObject(data, key)) removed++;
        }
        return removed;
    }
}
