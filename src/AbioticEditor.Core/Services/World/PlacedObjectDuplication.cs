using System.Globalization;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Whether a copy of a container gets its source's stored items.</summary>
public enum ContentsMode
{
    /// <summary>The copy's containers (and planted crops) start empty. Default: copying would duplicate items.</summary>
    Empty,

    /// <summary>The copy holds copies of the stored items, each with a new item id.</summary>
    Copy,
}

/// <summary>
/// Per-kind decisions for duplicating placed objects. Links between the copied objects are always
/// remapped together so the copy is self-contained; these settings cover what reaches outside the group.
/// </summary>
public sealed record DuplicatePolicy
{
    /// <summary>Default <see cref="ContentsMode.Empty"/>.</summary>
    public ContentsMode Contents { get; init; } = ContentsMode.Empty;

    /// <summary>
    /// An outlet of a copied object that feeds a device outside the copied group. <see cref="ReferencePolicy.Drop"/>
    /// (default) leaves the copy's outlet unplugged; <see cref="ReferencePolicy.Keep"/> keeps pointing at the original
    /// device (two outlets then feed one device, unverified in-game); <see cref="ReferencePolicy.Refuse"/> stops.
    /// </summary>
    public ReferencePolicy ExternalPowerLinks { get; init; } = ReferencePolicy.Drop;

    /// <summary>
    /// A copied teleporter pad whose tag is used by pads outside the copied group (or by no other copied pad).
    /// <see cref="ReferencePolicy.Drop"/> (default) gives the copy no tag; <see cref="ReferencePolicy.Keep"/> keeps the
    /// tag so the copy joins the original network; <see cref="ReferencePolicy.Refuse"/> stops.
    /// </summary>
    public ReferencePolicy ExternalTeleporterPeers { get; init; } = ReferencePolicy.Drop;

    /// <summary>
    /// When two or more copied pads share a tag, give the copies a fresh tag of their own so the copied pair links
    /// with each other and not with the originals. When false the copies keep the original tag.
    /// </summary>
    public bool RemapInternalTeleporterPairs { get; init; } = true;

    /// <summary>The safe default policy.</summary>
    public static DuplicatePolicy Default { get; } = new();
}

/// <summary>One staged duplication of a selection.</summary>
/// <param name="Id">Identifies the duplication for revert.</param>
/// <param name="SourceKeys">The objects copied.</param>
/// <param name="Offset">Translation applied to every copy (after the rotation), in centimetres.</param>
/// <param name="YawDegrees">Rotation of the whole copied group about <paramref name="Pivot"/>, in degrees.</param>
/// <param name="Pivot">Where the yaw turns around.</param>
/// <param name="Policy">Contents and external-link decisions.</param>
/// <param name="NewKeys">The keys minted for the copies (source key to new key), fixed at staging time.</param>
public sealed record StagedDuplication(
    int Id,
    IReadOnlyList<string> SourceKeys,
    PlacedVector Offset,
    double YawDegrees,
    GroupPivot Pivot,
    DuplicatePolicy Policy,
    IReadOnlyDictionary<string, string> NewKeys);

/// <summary>How one power outlet record of a source object is copied.</summary>
public sealed record SocketRemap(
    string OldId,
    string NewId,
    string? PluggedBefore,
    string? PluggedAfter,
    IReadOnlyList<string> ExtrasBefore,
    IReadOnlyList<string> ExtrasAfter,
    string Note);

/// <summary>What duplicating one object would create.</summary>
public sealed record DuplicationPreviewRow(
    int DuplicationId,
    string SourceKey,
    string NewKey,
    string? ClassName,
    PlacedObjectTransform? Before,
    PlacedObjectTransform? After,
    string? NewActorPath,
    IReadOnlyList<SocketRemap> Sockets,
    int? TeleporterTagBefore,
    int? TeleporterTagAfter,
    ContentsMode Contents,
    IReadOnlyList<StoredItemRef> SourceContents,
    bool BedClaimCleared,
    IReadOnlyList<BaseEditIssue> Issues)
{
    /// <summary>True when something stops this copy from being made.</summary>
    public bool Blocked => Issues.Any(i => i.IsBlocking);
}

/// <summary>One fully decided copy (internal: the apply step reads it).</summary>
internal sealed record DuplicationRowPlan(
    StagedDuplication Duplication,
    string SourceKey,
    string NewKey,
    string NewActorSubPath,
    PlacedVector? NewTranslation,
    PlacedQuaternion? NewRotation,
    IReadOnlyList<SocketRemap> Sockets,
    int? TeleporterTarget,
    bool ClearBedClaim,
    IReadOnlyDictionary<string, string> KeyMap,
    IReadOnlyDictionary<string, string> PathMap);

/// <summary>The result of planning duplications (reads only).</summary>
public sealed class DuplicationPlan
{
    internal DuplicationPlan(
        IReadOnlyList<DuplicationPreviewRow> rows, IReadOnlyList<BaseEditIssue> issues,
        IReadOnlyList<DuplicationRowPlan> rowPlans)
    {
        Rows = rows;
        Issues = issues;
        RowPlans = rowPlans;
    }

    /// <summary>One row per copy.</summary>
    public IReadOnlyList<DuplicationPreviewRow> Rows { get; }

    /// <summary>Every finding, including those on rows.</summary>
    public IReadOnlyList<BaseEditIssue> Issues { get; }

    internal IReadOnlyList<DuplicationRowPlan> RowPlans { get; }

    /// <summary>True when no finding blocks the duplication.</summary>
    public bool CanApply => !Issues.Any(i => i.IsBlocking);
}

/// <summary>Decides what a duplication would create. Reads only; the copies are built by <see cref="PlacedObjectCloner"/>.</summary>
public static partial class PlacedObjectDuplication
{
    [GeneratedRegex("_(?<n>\\d+)$")]
    private static partial Regex InstanceSuffix();

    /// <summary>
    /// Plans every duplication. <paramref name="currentTransform"/> supplies each source object's CURRENT
    /// (possibly staged) transform, so a move followed by a duplicate copies from the moved place.
    /// </summary>
    public static DuplicationPlan Plan(
        WorldSaveData data,
        IReadOnlyList<StagedDuplication> duplications,
        Func<string, PlacedObjectTransform?> currentTransform)
    {
        var rows = new List<DuplicationPreviewRow>();
        var rowPlans = new List<DuplicationRowPlan>();
        var all = new List<BaseEditIssue>();

        var existingKeys = new HashSet<string>(
            WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Select(e => e.Key), StringComparer.Ordinal);
        var sockets = PlacedPowerRecords.Read(data);
        var socketIds = new HashSet<string>(sockets.Select(s => s.Id), StringComparer.Ordinal);
        var usedPaths = new HashSet<string>(StringComparer.Ordinal);
        var nextInstance = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is not { } p) continue;
            if (PlacedObjectCensus.FullSoftPath(p.Package, p.Asset, p.SubPath) is { } full) usedPaths.Add(full);
            if (p.SubPath is { } sub && InstanceSuffix().Match(sub) is { Success: true } m
                && long.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                var prefix = PrefixOf(p.Package, p.Asset, sub);
                if (!nextInstance.TryGetValue(prefix, out var lowest) || n - 1 < lowest) nextInstance[prefix] = n - 1;
            }
        }

        var pads = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedTags = new HashSet<int>();
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            if (!TeleporterPadFeature.IsPad(e.Props)) continue;
            var f = TeleporterPadFeature.GetFrequency(e.Props) ?? 0;
            pads[e.Key] = f;
            if (f > 0) usedTags.Add(f);
        }

        var mintedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dup in duplications)
        {
            var selection = new HashSet<string>(dup.SourceKeys, StringComparer.Ordinal);
            var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in dup.SourceKeys)
            {
                if (dup.NewKeys.TryGetValue(k, out var nk)) keyMap[k] = nk;
            }

            var pivot = dup.YawDegrees == 0
                ? new PlacedVector(0, 0, 0)
                : dup.Pivot.Resolve(dup.SourceKeys, k => currentTransform(k)?.Translation) ?? new PlacedVector(0, 0, 0);
            var pivotResolved = dup.YawDegrees == 0
                || dup.Pivot.Resolve(dup.SourceKeys, k => currentTransform(k)?.Translation) is not null;
            var tagMap = new Dictionary<int, int>();
            var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);

            // Padded pads per source (for internal-pair detection).
            var selectedPadTags = dup.SourceKeys.Where(pads.ContainsKey).Select(k => pads[k]).Where(f => f > 0)
                .GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());

            var opRowPlans = new List<(DuplicationRowPlan Plan, List<BaseEditIssue> Issues, DuplicationPreviewRow Row)>();
            foreach (var source in dup.SourceKeys.Distinct(StringComparer.Ordinal))
            {
                var issues = new List<BaseEditIssue>();
                var props = WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", source);
                if (props is null)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "not-in-save", "Object is not in this save.", source));
                    rows.Add(EmptyRow(dup, source, issues));
                    all.AddRange(issues);
                    continue;
                }

                if (PlacedObjectCensus.KeyShape(source) != "guid32" || props.TryGetBool("DeployedByPlayer_") != true)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "level-placed",
                        "Level-placed object: the level owns it, so it cannot be copied. Refused.", source));
                }

                var newKey = keyMap.GetValueOrDefault(source);
                if (newKey is null || newKey.Length != 32 || !newKey.All(Uri.IsHexDigit))
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "bad-new-key", "No valid new key was minted for this copy.", source));
                    newKey ??= string.Empty;
                }
                else if (existingKeys.Contains(newKey) || !mintedKeys.Add(newKey)
                    || socketIds.Any(id => id.StartsWith(newKey, StringComparison.Ordinal)))
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "key-collision",
                        "The minted key is already in use; stage the duplication again.", source));
                }

                var before = currentTransform(source) ?? PlacedObjectCensus.ReadTransform(props);
                PlacedVector? newT = null;
                PlacedQuaternion? newR = null;
                if (before?.Translation is not { } t)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "no-translation",
                        "Object has no saved location member to place the copy with; the editor does not create it.", source));
                }
                else
                {
                    var moved = dup.YawDegrees == 0 ? t : PlacementMath.RotateAboutZ(t, pivot, dup.YawDegrees);
                    newT = new PlacedVector(moved.X + dup.Offset.X, moved.Y + dup.Offset.Y, moved.Z + dup.Offset.Z);
                }
                if (dup.YawDegrees != 0)
                {
                    if (before?.Rotation is { } r) newR = PlacementMath.ComposeYaw(r, dup.YawDegrees);
                    else
                    {
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "no-rotation",
                            "Rotation member is omitted in the save; the editor does not create it, so this object cannot be rotated.", source));
                    }
                }
                if (!pivotResolved)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "pivot", "The pivot could not be resolved (no position).", source));
                }

                // Actor path: same level, new unused instance number.
                string newSub = string.Empty;
                string? newFullPath = null;
                if (WorldMapAccessor.GetSoftObjectPath(props, "ActorPath_") is { SubPath: { } sub } ap
                    && InstanceSuffix().Match(sub) is { Success: true } sm)
                {
                    var prefix = PrefixOf(ap.Package, ap.Asset, sub);
                    var n = nextInstance.GetValueOrDefault(prefix, 2147483000L);
                    string candidate;
                    do
                    {
                        newSub = sub[..sm.Index] + "_" + n.ToString(CultureInfo.InvariantCulture);
                        candidate = PlacedObjectCensus.FullSoftPath(ap.Package, ap.Asset, newSub)!;
                        n--;
                    }
                    while (usedPaths.Contains(candidate) && n > 0);
                    nextInstance[prefix] = n;
                    usedPaths.Add(candidate);
                    newFullPath = candidate;
                    pathMap[PlacedObjectCensus.FullSoftPath(ap.Package, ap.Asset, sub)!] = candidate;
                }
                else
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "no-actor-path",
                        "Object has no actor path with an instance number to mint a new one from.", source));
                }

                var className = PlacedObjectCensus.ClassNameOf(PlacedObjectCensus.ClassPathOf(props));
                var contents = PlacedObjectContents.ReadItems(props);
                var proxies = PlacedObjectContents.ProxyCount(props);
                if (dup.Policy.Contents == ContentsMode.Empty)
                {
                    if (contents.Count > 0)
                    {
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "contents-not-copied",
                            $"{contents.Count} stored slot(s) are not copied; the copy starts empty.", source));
                    }
                    if (proxies > 0)
                    {
                        issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "proxies-not-copied",
                            $"{proxies} planted or spawned item proxy(ies) are not copied.", source));
                    }
                }
                else if (contents.Count > 0 || proxies > 0)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "contents-copied",
                        "Copying contents duplicates items. Item ids are re-minted, but text embedded inside item data is copied as is.", source));
                }

                // Power outlets owned by this object.
                var socketRemaps = new List<SocketRemap>();
                foreach (var s in sockets.Where(x => x.OwnerKey == source))
                {
                    var newId = newKey + s.Id[32..];
                    string? plugged = s.Plugged;
                    var notes = new List<string>();
                    if (plugged is not null)
                    {
                        if (keyMap.TryGetValue(plugged, out var np) && selection.Contains(plugged))
                        {
                            plugged = np;
                            notes.Add("plugged device is in the group: remapped to its copy");
                        }
                        else
                        {
                            plugged = ExternalLink(dup.Policy.ExternalPowerLinks, plugged, s.Id, source, issues, out var note, plugged);
                            notes.Add(note);
                        }
                    }
                    var extras = new List<string>();
                    foreach (var x in s.Extras)
                    {
                        if (keyMap.TryGetValue(x, out var nx) && selection.Contains(x))
                        {
                            extras.Add(nx);
                        }
                        else
                        {
                            var kept = ExternalLink(dup.Policy.ExternalPowerLinks, x, s.Id, source, issues, out var note, x);
                            notes.Add(note);
                            if (kept is not null) extras.Add(kept);
                        }
                    }
                    socketRemaps.Add(new SocketRemap(
                        s.Id, newId, s.Plugged, plugged ?? (s.Plugged is null ? null : WorldSaveWriter.NoPluggedDevice), s.Extras, extras,
                        notes.Count == 0 ? "unplugged outlet copied" : string.Join("; ", notes)));
                }

                // Teleporter pairing.
                int? tagBefore = null, tagAfter = null;
                int? tagTarget = null;
                if (pads.TryGetValue(source, out var freq))
                {
                    tagBefore = freq;
                    tagAfter = freq;
                    if (freq > 0)
                    {
                        if (selectedPadTags.GetValueOrDefault(freq) >= 2 && dup.Policy.RemapInternalTeleporterPairs)
                        {
                            if (!tagMap.TryGetValue(freq, out var fresh))
                            {
                                fresh = FreeTag(usedTags, out var beyond);
                                tagMap[freq] = fresh;
                                if (beyond)
                                {
                                    issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "tag-beyond-catalog",
                                        "Every known teleporter tag is in use; the copies get a number beyond the known list (unverified).", source));
                                }
                            }
                            tagTarget = tagAfter = fresh;
                            issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "teleporter-remapped",
                                $"Copied pads that shared tag {freq} share new tag {fresh} instead, so the copies pair with each other.", source));
                        }
                        else if (selectedPadTags.GetValueOrDefault(freq) >= 2)
                        {
                            issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "teleporter-joins-original",
                                $"Copies keep tag {freq}: they join the original pads' network.", source));
                        }
                        else
                        {
                            switch (dup.Policy.ExternalTeleporterPeers)
                            {
                                case ReferencePolicy.Refuse:
                                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "teleporter-external",
                                        $"This pad's tag ({freq}) is used outside the copied group.", source));
                                    break;
                                case ReferencePolicy.Drop:
                                    tagTarget = tagAfter = 0;
                                    issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "teleporter-unassigned",
                                        $"The copy gets no tag (the original keeps tag {freq}).", source));
                                    break;
                                default:
                                    issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "teleporter-joins-original",
                                        $"The copy keeps tag {freq}: it joins the original network.", source));
                                    break;
                            }
                        }
                    }
                }

                var clearBed = false;
                var claim = WorldDeployable.ParseClaim(props.GetString("CustomTextDisplay_"));
                if (claim.OwnerId is not null)
                {
                    clearBed = true;
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "bed-claim-cleared",
                        "The bed's player claim is never copied; the copy is unclaimed.", source));
                }
                if (className?.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase) == true)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "shared-inventory",
                        "Void chests all show one shared inventory; the copy shows the same one.", source));
                }
                if (!string.IsNullOrEmpty(newKey) && PlacedObjectCensus.ReadTransform(props) is null)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "no-transform", "Object has no transform.", source));
                }
                if (props.FindByPrefix("ChangableData_")?.Property is UeSaveGame.PropertyTypes.StructProperty { Value: UeSaveGame.StructData.PropertiesStruct cd }
                    && cd.Properties.GetString("AssetID_") is { } assetId && assetId != source)
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "asset-id-differs",
                        "The object's AssetID differs from its key; the copy's AssetID is set to its new key.", source));
                }

                PlacedObjectTransform? after = before is null
                    ? null
                    : before with { Translation = newT ?? before.Translation, Rotation = newR ?? before.Rotation };

                var keyMapSnapshot = keyMap;
                var plan = new DuplicationRowPlan(
                    dup, source, newKey, newSub, newT, newR, socketRemaps, tagTarget, clearBed, keyMapSnapshot, pathMap);
                var row = new DuplicationPreviewRow(
                    dup.Id, source, newKey, className, before, after, newFullPath, socketRemaps,
                    tagBefore, tagAfter, dup.Policy.Contents, contents, clearBed, issues);
                opRowPlans.Add((plan, issues, row));
            }

            foreach (var (plan, issues, row) in opRowPlans)
            {
                rows.Add(row);
                all.AddRange(issues);
                rowPlans.Add(plan);
            }
        }
        return new DuplicationPlan(rows, all, rowPlans);
    }

    private static string? ExternalLink(
        ReferencePolicy policy, string device, string socketId, string source, List<BaseEditIssue> issues,
        out string note, string original)
    {
        switch (policy)
        {
            case ReferencePolicy.Refuse:
                note = "device outside the group";
                issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "external-power-link",
                    $"Outlet {socketId[^Math.Min(3, socketId.Length)..]} feeds a device outside the copied group.", source));
                return original;
            case ReferencePolicy.Keep:
                note = "copy keeps feeding the original device";
                issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "external-power-link-kept",
                    "A copied outlet keeps pointing at a device outside the group: two outlets then feed one device (unverified in-game).", source));
                return original;
            default:
                note = "device outside the group: left unplugged";
                issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "external-power-link-dropped",
                    "A copied outlet that fed a device outside the group is left unplugged.", source));
                return null;
        }
    }

    private static int FreeTag(HashSet<int> used, out bool beyondCatalog)
    {
        for (var f = 1; f <= TeleporterTagCatalog.MaxFrequency; f++)
        {
            if (used.Add(f))
            {
                beyondCatalog = false;
                return f;
            }
        }
        var next = (used.Count == 0 ? 0 : used.Max()) + 1;
        used.Add(next);
        beyondCatalog = true;
        return next;
    }

    private static string PrefixOf(string? package, string? asset, string subPath)
    {
        var i = subPath.LastIndexOf('_');
        return $"{package}.{asset}:{(i >= 0 ? subPath[..i] : subPath)}";
    }

    private static DuplicationPreviewRow EmptyRow(StagedDuplication dup, string source, List<BaseEditIssue> issues)
        => new(dup.Id, source, dup.NewKeys.GetValueOrDefault(source, string.Empty), null, null, null, null, [], null, null,
            dup.Policy.Contents, [], false, issues);
}
