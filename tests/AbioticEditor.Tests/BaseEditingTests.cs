using System.Globalization;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Base-building phase 5: delete, duplicate and group-transform of placed objects, proved against the
/// dedicated-server Facility save (power links, teleporters, claimed beds, containers). Each test
/// compares a deep fingerprint of EVERY entry of EVERY map before and after, so "exactly the intended
/// change and nothing else" is asserted, not assumed. Tests skip when the fixture is absent.
/// </summary>
public sealed class BaseEditingTests
{
    private static readonly PlacedVector Offset = new(400, 250, 0);

    // ---------- finders ----------

    internal static (string Owner, string Device, string SocketId) LinkedPair(WorldSaveData d)
    {
        var built = PlayerBuilt(d).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, owner, plugged) in Sockets(d))
        {
            if (owner is not null && plugged is not null && owner != plugged
                && built.Contains(owner) && built.Contains(plugged))
            {
                return (owner, plugged, id);
            }
        }
        throw new InvalidOperationException("Fixture has no player-built power link.");
    }

    /// <summary>A player-built object nothing points at and that points at nothing.</summary>
    internal static string Quiet(WorldSaveData d, Func<PlacedObjectSummary, bool>? filter = null)
    {
        var sockets = Sockets(d);
        var touched = sockets.Where(s => s.Plugged is not null).Select(s => s.Plugged!)
            .Concat(sockets.Where(s => s.Owner is not null).Select(s => s.Owner!)).ToHashSet(StringComparer.Ordinal);
        foreach (var o in PlayerBuilt(d))
        {
            if (touched.Contains(o.Key) || o.InventoryCount > 0 || o.CustomName is not null) continue;
            if (o.Transform is not { Translation: not null, Rotation: not null }) continue;
            if (o.ClassName!.Contains("Pad", StringComparison.Ordinal) || o.ClassName.Contains("Bed", StringComparison.Ordinal)) continue;
            if (filter is not null && !filter(o)) continue;
            var report = PlacedGroupReferenceAnalyzer.Analyze(d, [o.Key]);
            if (report.External.Count == 0 && report.IdentityBindings.Count == 0) return o.Key;
        }
        throw new InvalidOperationException("Fixture has no quiet object.");
    }

    private static BaseEditApplyResult AssertApplied(BaseEditApplyResult result)
    {
        Assert.True(result.Applied, string.Join(" | ", result.Issues.Where(i => i.IsBlocking).Select(i => i.Code + ": " + i.Message)));
        return result;
    }

    private static void AssertUnchanged(WorldSaveData data, byte[] before)
        => Assert.True(before.AsSpan().SequenceEqual(Serialize(data)), "the save changed although the edit was refused");

    private static WorldSaveData Reload(WorldSaveData data)
    {
        using var ms = new MemoryStream(Serialize(data));
        return WorldSaveReader.ReadFromStream(ms);
    }

    private static PlacedVector Translation(WorldSaveData d, string key) => PlacedObjectCensus.ReadTransform(Entry(d, key))!.Translation!.Value;

    private static List<string> Names(IList<FPropertyTag> props) => props.Select(t => t.Name!.Value).ToList();

    // ---------- delete ----------

    [Fact]
    public void Delete_of_an_unreferenced_object_removes_only_that_entry()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var key = Quiet(data);
        var before = Fingerprint(data.Raw);
        var count = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count();

        var edits = new StagedBaseEdits();
        edits.StageDelete([key]);
        var preview = edits.Preview(data);
        Assert.True(preview.CanApply);
        Assert.Equal(count, preview.ObjectsBefore);
        Assert.Equal(count - 1, preview.ObjectsAfter);

        var result = edits.ApplyTo(data);
        Assert.True(result.Applied);
        Assert.Equal(count - 1, result.ObjectsAfter);

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(changed);
        Assert.Equal(["DeployedObjectMap/" + key], removed);

        // Survives a write and re-read.
        var reread = Reload(data);
        Assert.Equal(count - 1, WorldMapAccessor.Entries(reread.Raw, "DeployedObjectMap").Count());
        Assert.Null(WorldMapAccessor.FindEntry(reread.Raw, "DeployedObjectMap", key));
        Assert.True(edits.IsEmpty);
    }

    [Fact]
    public void Delete_removes_the_outlet_records_the_object_owns_and_nothing_else()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var sockets = Sockets(data);
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var plugged = sockets.Where(s => s.Plugged is not null).Select(s => s.Plugged!).ToHashSet(StringComparer.Ordinal);
        // An owner that nothing plugs into, so only its own outlets are involved.
        var owner = sockets.Where(s => s.Owner is not null && built.Contains(s.Owner) && !plugged.Contains(s.Owner))
            .Select(s => s.Owner!).First(k => PlacedGroupReferenceAnalyzer.Analyze(data, [k]).External.All(e => e.Field != "(map key)"
                && e.Kind != GroupReferenceKind.PowerSocketTargetsSelection && e.Kind != GroupReferenceKind.KeyReference
                && e.OtherEnd is null));
        var owned = sockets.Where(s => s.Owner == owner).Select(s => s.Id).ToList();
        Assert.NotEmpty(owned);

        var before = Fingerprint(data.Raw);
        var edits = new StagedBaseEdits();
        edits.StageDelete([owner]);
        var row = Assert.Single(edits.Preview(data).Deletions);
        Assert.Equal(owned.Order(StringComparer.Ordinal), row.OwnedSocketIds.Order(StringComparer.Ordinal));

        AssertApplied(edits.ApplyTo(data));
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(changed);
        Assert.Equal(
            owned.Select(id => "PowerSocketMap/" + id).Append("DeployedObjectMap/" + owner).Order(StringComparer.Ordinal),
            removed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Delete_of_the_owner_can_keep_the_outlet_records()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var (owner, _, _) = LinkedPair(data);
        var before = Fingerprint(data.Raw);
        var edits = new StagedBaseEdits();
        edits.StageDelete([owner], new DeletePolicy { OwnedSocketRecords = ReferencePolicy.Keep, InboundPlugs = ReferencePolicy.Keep, OtherReferences = ReferencePolicy.Keep, BedClaims = ReferencePolicy.Keep, TeleporterPeers = ReferencePolicy.Keep });
        AssertApplied(edits.ApplyTo(data));
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(changed);
        Assert.Equal(["DeployedObjectMap/" + owner], removed);
    }

    [Fact]
    public void Delete_with_an_inbound_plug_is_refused_by_default_and_changes_no_bytes()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var inbound = Sockets(data).First(s => s.Plugged is not null && built.Contains(s.Plugged) && s.Owner != s.Plugged);
        var device = inbound.Plugged!;
        var original = Serialize(data);

        var edits = new StagedBaseEdits();
        edits.StageDelete([device]);
        var preview = edits.Preview(data);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Code == "inbound-plug" && i.IsBlocking);
        Assert.NotEmpty(preview.Deletions[0].InboundLinks);

        var result = edits.ApplyTo(data);
        Assert.False(result.Applied);
        AssertUnchanged(data, original);
        Assert.False(edits.IsEmpty); // still staged so the UI can show why
    }

    [Fact]
    public void Delete_can_unplug_inbound_sockets()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var device = Sockets(data).First(s => s.Plugged is not null && built.Contains(s.Plugged) && s.Owner != s.Plugged).Plugged!;
        var feeders = Sockets(data).Where(s => s.Plugged == device && s.Owner != device).Select(s => s.Id).ToList();
        var ownOutlets = Sockets(data).Where(s => s.Owner == device).Select(s => s.Id).ToList();
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits();
        edits.StageDelete([device], new DeletePolicy { InboundPlugs = ReferencePolicy.Drop, OtherReferences = ReferencePolicy.Keep, BedClaims = ReferencePolicy.Keep, TeleporterPeers = ReferencePolicy.Keep });
        var preview = edits.Preview(data);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        AssertApplied(edits.ApplyTo(data));

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Equal(feeders.Select(f => "PowerSocketMap/" + f).Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
        Assert.Equal(
            ownOutlets.Select(o => "PowerSocketMap/" + o).Append("DeployedObjectMap/" + device).Order(StringComparer.Ordinal),
            removed.Order(StringComparer.Ordinal));
        foreach (var f in feeders) Assert.Equal("-1", SocketPlugged(data, f));
    }

    [Fact]
    public void Delete_can_keep_a_dangling_inbound_plug_and_says_so()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var inbound = Sockets(data).First(s => s.Plugged is not null && built.Contains(s.Plugged) && s.Owner != s.Plugged);
        var edits = new StagedBaseEdits();
        edits.StageDelete([inbound.Plugged!], new DeletePolicy { InboundPlugs = ReferencePolicy.Keep, OtherReferences = ReferencePolicy.Keep, BedClaims = ReferencePolicy.Keep, TeleporterPeers = ReferencePolicy.Keep });
        var preview = edits.Preview(data);
        Assert.Contains(preview.Issues, i => i.Code == "dangling-plug");
        AssertApplied(edits.ApplyTo(data));
        Assert.Equal(inbound.Plugged, SocketPlugged(data, inbound.Id));
    }

    [Fact]
    public void Level_placed_objects_are_refused_by_every_operation_and_change_no_bytes()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var levelKey = PlacedObjectCensus.Build(data).Objects!.First(o => o.Key.Length != 32 && o.Transform?.Translation is not null).Key;
        var original = Serialize(data);

        var del = new StagedBaseEdits();
        del.StageDelete([levelKey]);
        Assert.Contains(del.Preview(data).Issues, i => i.Code == "level-placed" && i.IsBlocking);
        Assert.False(del.ApplyTo(data).Applied);

        var dup = new StagedBaseEdits(Counter());
        dup.StageDuplicate([levelKey], Offset);
        Assert.Contains(dup.Preview(data).Issues, i => i.Code == "level-placed" && i.IsBlocking);
        Assert.False(dup.ApplyTo(data).Applied);

        var move = new StagedBaseEdits();
        move.MoveBy(data, [levelKey], 10, 0, 0);
        Assert.Contains(move.Preview(data).Issues, i => i.Code == "transform-blocked");
        Assert.False(move.ApplyTo(data).Applied);

        var missing = new StagedBaseEdits();
        missing.StageDelete(["00000000000000000000000000000000"]);
        Assert.Contains(missing.Preview(data).Issues, i => i.Code == "not-in-save" && i.IsBlocking);
        Assert.False(missing.ApplyTo(data).Applied);

        AssertUnchanged(data, original);
    }

    [Fact]
    public void Delete_lists_stored_items_and_deletes_them_with_the_container()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var crate = PlayerBuilt(data).First(o => o.StoredItemCount > 0 && o.ClassName!.Contains("StorageCrate", StringComparison.Ordinal)
            && !o.ClassName.Contains("Void", StringComparison.Ordinal)
            && PlacedGroupReferenceAnalyzer.Analyze(data, [o.Key]).External.Count == 0);
        var expected = PlacedObjectContents.ReadItems(Entry(data, crate.Key));
        Assert.NotEmpty(expected);

        var edits = new StagedBaseEdits();
        edits.StageDelete([crate.Key]);
        var preview = edits.Preview(data);
        Assert.Equal(expected, preview.Deletions[0].Contents);
        Assert.Equal(expected.Sum(c => Math.Max(c.Count, 1)), preview.ItemsDeleted);
        Assert.Contains(preview.Issues, i => i.Code == "contents-deleted");

        var before = Fingerprint(data.Raw);
        AssertApplied(edits.ApplyTo(data));
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(changed);
        Assert.Contains("DeployedObjectMap/" + crate.Key, removed);
    }

    [Fact]
    public void Delete_of_a_claimed_bed_and_of_paired_teleporters_needs_an_explicit_policy()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var bed = data.Deployables.First(d => d.IsBed && d.HasClaimMarker && d.OwnerId is not null
            && PlacedGroupReferenceAnalyzer.Analyze(data, [d.Id]).External.Count == 0);
        var original = Serialize(data);

        var refused = new StagedBaseEdits();
        refused.StageDelete([bed.Id]);
        Assert.Contains(refused.Preview(data).Issues, i => i.Code == "bed-claim" && i.IsBlocking);
        Assert.False(refused.ApplyTo(data).Applied);
        AssertUnchanged(data, original);

        var allowed = new StagedBaseEdits();
        allowed.StageDelete([bed.Id], new DeletePolicy { BedClaims = ReferencePolicy.Drop });
        AssertApplied(allowed.ApplyTo(data));
        Assert.Null(WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", bed.Id));

        // Teleporters: a pad whose tag is shared with a pad that stays.
        var data2 = Load();
        var pads = TeleporterPads(data2);
        var pad = pads.First(p => p.Tag > 0 && pads.Any(q => q.Key != p.Key && q.Tag == p.Tag)).Key;
        var edits = new StagedBaseEdits();
        edits.StageDelete([pad]);
        var preview = edits.Preview(data2);
        Assert.Contains(preview.Issues, i => i.Code == "teleporter-peers" && i.IsBlocking);
        var original2 = Serialize(data2);
        Assert.False(edits.ApplyTo(data2).Applied);
        AssertUnchanged(data2, original2);

        var edits2 = new StagedBaseEdits();
        edits2.StageDelete([pad], new DeletePolicy { TeleporterPeers = ReferencePolicy.Keep });
        var p2 = edits2.Preview(data2);
        if (p2.CanApply) AssertApplied(edits2.ApplyTo(data2));
    }

    private static List<(string Key, int Tag)> TeleporterPads(WorldSaveData d)
        => new TeleporterPadFeature().Read(d.Raw)
            .Select(e => (e.Key, int.Parse(e.Fields.First(f => f.Id == "frequency").Value!.ToString()!, CultureInfo.InvariantCulture))).ToList();

    private static int TagOf(WorldSaveData d, string key) => TeleporterPads(d).Single(p => p.Key == key).Tag;

    // ---------- duplicate ----------

    [Fact]
    public void Duplicating_a_power_linked_pair_remaps_the_outlet_keys_and_the_plug_between_the_copies()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var (owner, device, socketId) = LinkedPair(data);
        var digit = socketId[32..];
        var ownerOutlets = Sockets(data).Where(s => s.Owner == owner).Select(s => s.Id).ToList();
        var deviceOutlets = Sockets(data).Where(s => s.Owner == device).Select(s => s.Id).ToList();
        var before = Fingerprint(data.Raw);
        var srcOwnerT = PlacedObjectCensus.ReadTransform(Entry(data, owner))!;
        var srcNames = Names(Entry(data, owner));

        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate([owner, device], Offset);
        var newOwner = dup.NewKeys[owner];
        var newDevice = dup.NewKeys[device];

        var preview = edits.Preview(data);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Equal(2, preview.Duplications.Count);
        var ownerRow = preview.Duplications.Single(r => r.SourceKey == owner);
        Assert.Equal(srcOwnerT.Translation!.Value.X + Offset.X, ownerRow.After!.Translation!.Value.X);
        Assert.NotNull(ownerRow.NewActorPath);
        Assert.Contains(ownerRow.Sockets, s => s.OldId == socketId && s.NewId == newOwner + digit && s.PluggedAfter == newDevice);

        var result = edits.ApplyTo(data);
        Assert.True(result.Applied);
        Assert.Equal(2, result.Created.Count);
        Assert.Equal(before["DeployedObjectMap"].Count + 2, WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count());

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(removed);
        Assert.Empty(changed);
        var expectedAdded = new List<string> { "DeployedObjectMap/" + newOwner, "DeployedObjectMap/" + newDevice };
        expectedAdded.AddRange(ownerOutlets.Select(id => "PowerSocketMap/" + newOwner + id[32..]));
        expectedAdded.AddRange(deviceOutlets.Select(id => "PowerSocketMap/" + newDevice + id[32..]));
        Assert.Equal(expectedAdded.Order(StringComparer.Ordinal), added.Order(StringComparer.Ordinal));

        // The internal plug follows the copies; the original still plugs the original.
        Assert.Equal(newDevice, SocketPlugged(data, newOwner + digit));
        Assert.Equal(device, SocketPlugged(data, socketId));

        // Identity and placement of the copy.
        var copy = Entry(data, newOwner);
        Assert.Equal(newOwner, ((StructProperty)copy.FindByPrefix("ChangableData_")!.Property!).Value is PropertiesStruct cd ? cd.Properties.GetString("AssetID_") : null);
        Assert.Equal(srcNames, Names(copy));
        var t = PlacedObjectCensus.ReadTransform(copy)!;
        Assert.Equal(srcOwnerT.Translation!.Value.X + Offset.X, t.Translation!.Value.X);
        Assert.Equal(srcOwnerT.Translation.Value.Y + Offset.Y, t.Translation.Value.Y);
        Assert.Equal(srcOwnerT.Rotation, t.Rotation);
        Assert.Equal(srcOwnerT.Scale3D, t.Scale3D);
        var paths = PlacedObjectCensus.Build(data).Objects!.Where(o => o.ActorPath is not null).Select(o => o.ActorPath!).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());

        // Survives a write and re-read, identically.
        Assert.Equal(Fingerprint(data.Raw).Count, Fingerprint(Reload(data).Raw).Count);
        var (a2, r2, c2) = Diff(Fingerprint(data.Raw), Fingerprint(Reload(data).Raw));
        Assert.Empty(a2);
        Assert.Empty(r2);
        Assert.Empty(c2);
    }

    [Fact]
    public void Duplicating_a_device_without_its_neighbour_follows_the_external_link_policy()
    {
        if (!HasServerFacility) return;
        var (owner, device, socketId) = LinkedPair(Load());

        // Drop (default): the copy's outlet is left unplugged.
        var data = Load();
        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate([owner], Offset);
        var preview = edits.Preview(data);
        Assert.Contains(preview.Issues, i => i.Code == "external-power-link-dropped");
        AssertApplied(edits.ApplyTo(data));
        Assert.Equal("-1", SocketPlugged(data, dup.NewKeys[owner] + socketId[32..]));
        Assert.Equal(device, SocketPlugged(data, socketId));

        // Keep: the copy keeps feeding the original device.
        var data2 = Load();
        var keep = new StagedBaseEdits(Counter());
        var d2 = keep.StageDuplicate([owner], Offset, policy: new DuplicatePolicy { ExternalPowerLinks = ReferencePolicy.Keep });
        Assert.Contains(keep.Preview(data2).Issues, i => i.Code == "external-power-link-kept");
        AssertApplied(keep.ApplyTo(data2));
        Assert.Equal(device, SocketPlugged(data2, d2.NewKeys[owner] + socketId[32..]));

        // Refuse: nothing is written.
        var data3 = Load();
        var original = Serialize(data3);
        var refuse = new StagedBaseEdits(Counter());
        refuse.StageDuplicate([owner], Offset, policy: new DuplicatePolicy { ExternalPowerLinks = ReferencePolicy.Refuse });
        Assert.Contains(refuse.Preview(data3).Issues, i => i.Code == "external-power-link" && i.IsBlocking);
        Assert.False(refuse.ApplyTo(data3).Applied);
        AssertUnchanged(data3, original);
    }

    [Fact]
    public void Duplicating_teleporter_pads_remaps_an_internal_pair_and_applies_the_external_policy()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var pads = TeleporterPads(data);
        var tagged = pads.Where(p => p.Tag > 0).GroupBy(p => p.Tag).First(g => g.Count() >= 2).ToList();
        var usedTags = pads.Select(p => p.Tag).ToHashSet();
        var pair = tagged.Take(2).Select(p => p.Key).ToList();

        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate(pair, Offset);
        AssertApplied(edits.ApplyTo(data));
        var newTags = pair.Select(k => TagOf(data, dup.NewKeys[k])).ToList();
        Assert.Equal(newTags[0], newTags[1]);
        Assert.DoesNotContain(newTags[0], usedTags);
        Assert.InRange(newTags[0], 1, TeleporterTagCatalog.MaxFrequency);
        foreach (var k in pair) Assert.Equal(tagged[0].Tag, TagOf(data, k));

        // A single pad from a pair: Drop (default) leaves the copy unassigned, Keep joins the network.
        var single = pair[0];
        var d2 = Load();
        var drop = new StagedBaseEdits(Counter());
        var sd = drop.StageDuplicate([single], Offset);
        AssertApplied(drop.ApplyTo(d2));
        Assert.Equal(0, TagOf(d2, sd.NewKeys[single]));

        var d3 = Load();
        var keep = new StagedBaseEdits(Counter());
        var sk = keep.StageDuplicate([single], Offset, policy: new DuplicatePolicy { ExternalTeleporterPeers = ReferencePolicy.Keep });
        AssertApplied(keep.ApplyTo(d3));
        Assert.Equal(tagged[0].Tag, TagOf(d3, sk.NewKeys[single]));

        var d4 = Load();
        var original = Serialize(d4);
        var refuse = new StagedBaseEdits(Counter());
        refuse.StageDuplicate([single], Offset, policy: new DuplicatePolicy { ExternalTeleporterPeers = ReferencePolicy.Refuse });
        Assert.False(refuse.ApplyTo(d4).Applied);
        AssertUnchanged(d4, original);
    }

    [Fact]
    public void A_copied_bed_is_never_claimed()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var bed = data.Deployables.First(d => d.IsBed && d.HasClaimMarker && d.OwnerId is not null);
        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate([bed.Id], Offset);
        Assert.Contains(edits.Preview(data).Issues, i => i.Code == "bed-claim-cleared");
        AssertApplied(edits.ApplyTo(data));

        var copyText = Entry(data, dup.NewKeys[bed.Id]).GetString("CustomTextDisplay_");
        Assert.Null(WorldDeployable.ParseClaim(copyText).OwnerId);
        Assert.Equal(WorldDeployable.ClaimSeparator, copyText);
        Assert.Equal(bed.OwnerId, WorldDeployable.ParseClaim(Entry(data, bed.Id).GetString("CustomTextDisplay_")).OwnerId);
    }

    [Fact]
    public void Container_contents_are_emptied_by_default_and_copied_with_new_item_ids_on_request()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var crate = PlayerBuilt(data).First(o => o.StoredItemCount > 1 && o.ClassName!.Contains("StorageCrate", StringComparison.Ordinal)
            && !o.ClassName.Contains("Void", StringComparison.Ordinal));
        var source = PlacedObjectContents.ReadItems(Entry(data, crate.Key));
        var slotCount = SlotCount(Entry(data, crate.Key));
        var gameEmptySlot = FirstEmptySlotFingerprint(data);

        // Default: empty.
        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate([crate.Key], Offset);
        Assert.Contains(edits.Preview(data).Issues, i => i.Code == "contents-not-copied");
        AssertApplied(edits.ApplyTo(data));
        var copy = Entry(data, dup.NewKeys[crate.Key]);
        Assert.Empty(PlacedObjectContents.ReadItems(copy));
        Assert.Equal(slotCount, SlotCount(copy));
        Assert.Equal(source, PlacedObjectContents.ReadItems(Entry(data, crate.Key))); // source untouched
        // Every emptied slot has exactly the game's own empty-slot shape.
        Assert.All(Slots(copy), s => Assert.Equal(gameEmptySlot, Fp(s)));

        // Copy: same items, new ids.
        var data2 = Load();
        var edits2 = new StagedBaseEdits(Counter());
        var dup2 = edits2.StageDuplicate([crate.Key], Offset, policy: new DuplicatePolicy { Contents = ContentsMode.Copy });
        var preview2 = edits2.Preview(data2);
        Assert.Contains(preview2.Issues, i => i.Code == "contents-copied");
        Assert.Equal(source.Sum(c => Math.Max(c.Count, 1)), preview2.ItemsCopied);
        AssertApplied(edits2.ApplyTo(data2));
        var copy2 = Entry(data2, dup2.NewKeys[crate.Key]);
        Assert.Equal(source, PlacedObjectContents.ReadItems(copy2));
        var srcIds = ItemAssetIds(Entry(data2, crate.Key));
        var newIds = ItemAssetIds(copy2);
        Assert.Equal(srcIds.Count, newIds.Count);
        Assert.Empty(srcIds.Intersect(newIds));
        Assert.Equal(newIds.Count, newIds.Distinct(StringComparer.Ordinal).Count());
        // Only this copy's id space is new: all ids unique across the save.
        var all = WorldMapAccessor.Entries(data2.Raw, "DeployedObjectMap").SelectMany(e => ItemAssetIds(e.Props)).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<IList<FPropertyTag>> Slots(IList<FPropertyTag> entry)
    {
        if (entry.FindByPrefix("ContainerInventories_")?.Property is not ArrayProperty { Value: { } invs }) yield break;
        foreach (var inv in invs.OfType<StructProperty>())
        {
            if (inv.Value is PropertiesStruct ips && ips.Properties.FindByPrefix("InventoryContent_")?.Property is ArrayProperty { Value: { } slots })
            {
                foreach (var s in slots.OfType<StructProperty>()) yield return ((PropertiesStruct)s.Value!).Properties;
            }
        }
    }

    private static int SlotCount(IList<FPropertyTag> entry) => Slots(entry).Count();

    private static List<string> ItemAssetIds(IList<FPropertyTag> entry)
        => Slots(entry).Select(s => (s.FindByPrefix("ChangeableData_")?.Property as StructProperty)?.Value as PropertiesStruct)
            .Where(p => p is not null).Select(p => p!.Properties.GetString("AssetID_") ?? string.Empty)
            .Where(id => id.Length == 32).ToList();

    private static string FirstEmptySlotFingerprint(WorldSaveData d)
    {
        foreach (var e in WorldMapAccessor.Entries(d.Raw, "DeployedObjectMap"))
        {
            foreach (var s in Slots(e.Props))
            {
                if (((s.FindByPrefix("ItemDataTable_")?.Property as StructProperty)?.Value as PropertiesStruct)?.Properties.GetString("RowName") == "Empty")
                {
                    return Fp(s);
                }
            }
        }
        throw new InvalidOperationException("No empty slot in fixture.");
    }

    [Fact]
    public void Copying_a_planted_garden_plot_can_empty_or_copy_its_crops()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var plot = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap")
            .First(e => e.Props.TryGetBool("DeployedByPlayer_") == true && PlacedObjectContents.ProxyCount(e.Props) > 0
                && e.Props.FindByPrefix("Class_")!.Property!.Value!.ToString()!.Contains("GardenPlot", StringComparison.Ordinal)).Key;
        var proxies = PlacedObjectContents.ProxyCount(Entry(data, plot));

        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate([plot], Offset);
        AssertApplied(edits.ApplyTo(data));
        Assert.Equal(0, PlacedObjectContents.ProxyCount(Entry(data, dup.NewKeys[plot])));
        Assert.Equal(proxies, PlacedObjectContents.ProxyCount(Entry(data, plot)));
        Assert.Equal(0, PlacedObjectContents.ProxyCount(Entry(Reload(data), dup.NewKeys[plot])));

        var data2 = Load();
        var edits2 = new StagedBaseEdits(Counter());
        var dup2 = edits2.StageDuplicate([plot], Offset, policy: new DuplicatePolicy { Contents = ContentsMode.Copy });
        AssertApplied(edits2.ApplyTo(data2));
        Assert.Equal(proxies, PlacedObjectContents.ProxyCount(Entry(Reload(data2), dup2.NewKeys[plot])));
    }

    [Fact]
    public void Every_player_built_class_can_be_duplicated_with_all_members_and_no_other_change()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var firstOfClass = PlayerBuilt(data).Where(o => o.Transform is { Translation: not null, Rotation: not null })
            .GroupBy(o => o.ClassPath).Select(g => g.First().Key).ToList();
        Assert.True(firstOfClass.Count > 10);
        var before = Fingerprint(data.Raw);
        var original = Serialize(data);
        var count = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count();
        var sourceNames = firstOfClass.ToDictionary(k => k, k => Names(Entry(data, k)), StringComparer.Ordinal);

        var edits = new StagedBaseEdits(Counter());
        var dup = edits.StageDuplicate(firstOfClass, new PlacedVector(0, 0, 20000), yawDegrees: 30, policy: new DuplicatePolicy { ExternalTeleporterPeers = ReferencePolicy.Drop });
        var preview = edits.Preview(data);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Equal(count + firstOfClass.Count, preview.ObjectsAfter);
        AssertApplied(edits.ApplyTo(data));

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(removed);
        Assert.Empty(changed);
        Assert.Equal(firstOfClass.Count, added.Count(a => a.StartsWith("DeployedObjectMap/", StringComparison.Ordinal)));
        Assert.All(added, a => Assert.True(a.StartsWith("DeployedObjectMap/", StringComparison.Ordinal) || a.StartsWith("PowerSocketMap/", StringComparison.Ordinal)));

        var reread = Reload(data);
        Assert.Equal(count + firstOfClass.Count, WorldMapAccessor.Entries(reread.Raw, "DeployedObjectMap").Count());
        foreach (var src in firstOfClass)
        {
            Assert.Equal(sourceNames[src], Names(Entry(reread, dup.NewKeys[src])));
        }

        // Deleting exactly the copies (and their outlets) restores the original file byte for byte.
        var undo = new StagedBaseEdits();
        undo.StageDelete(firstOfClass.Select(k => dup.NewKeys[k]));
        AssertApplied(undo.ApplyTo(reread));
        Assert.True(original.AsSpan().SequenceEqual(Serialize(reread)), "duplicate then delete did not restore the original bytes");
    }

    [Fact]
    public void Deleting_and_duplicating_together_keeps_the_map_count_consistent()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var gone = Quiet(data);
        var copied = Quiet(data, o => o.Key != gone);
        var count = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count();

        var edits = new StagedBaseEdits(Counter());
        edits.StageDelete([gone]);
        var dup = edits.StageDuplicate([copied], Offset);
        var dup2 = edits.StageDuplicate([copied], new PlacedVector(-400, 0, 0));
        var preview = edits.Preview(data);
        Assert.Equal(count, preview.ObjectsBefore);
        Assert.Equal(count + 1, preview.ObjectsAfter);

        var result = edits.ApplyTo(data);
        Assert.True(result.Applied);
        Assert.Equal(count + 1, result.ObjectsAfter);
        var keys = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(gone, keys);
        Assert.Contains(dup.NewKeys[copied], keys);
        Assert.Contains(dup2.NewKeys[copied], keys);
        Assert.Equal(count + 1, WorldMapAccessor.Entries(Reload(data).Raw, "DeployedObjectMap").Count());
    }

    [Fact]
    public void A_move_then_duplicate_copies_from_the_moved_place_and_moves_only_the_original()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var key = Quiet(data);
        var start = Translation(data, key);
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits(Counter());
        edits.MoveBy(data, [key], 1000, 0, 0);
        var dup = edits.StageDuplicate([key], new PlacedVector(0, 500, 0));
        AssertApplied(edits.ApplyTo(data));

        Assert.Equal(start.X + 1000, Translation(data, key).X);
        var copy = Translation(data, dup.NewKeys[key]);
        Assert.Equal(start.X + 1000, copy.X);
        Assert.Equal(start.Y + 500, copy.Y);
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(removed);
        Assert.Equal(["DeployedObjectMap/" + key], changed);
        Assert.Equal(["DeployedObjectMap/" + dup.NewKeys[key]], added);
    }

    [Fact]
    public void Duplication_validation_blocks_missing_members_and_key_collisions_without_changing_bytes()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var key = Quiet(data);
        var original = Serialize(data);

        // A minted key that already exists.
        var collide = new StagedBaseEdits(() => key);
        collide.StageDuplicate([key], Offset);
        Assert.Contains(collide.Preview(data).Issues, i => i.Code == "key-collision" && i.IsBlocking);
        Assert.False(collide.ApplyTo(data).Applied);

        // A source whose rotation member is omitted cannot be turned (the editor never creates members).
        var props = Entry(data, key);
        var tps = (PropertiesStruct)((StructProperty)props.FindByPrefix("Transform_")!.Property!).Value!;
        var rotation = tps.Properties.FindByPrefix("Rotation")!;
        tps.Properties.Remove(rotation);
        var turned = new StagedBaseEdits(Counter());
        turned.StageDuplicate([key], Offset, yawDegrees: 90);
        Assert.Contains(turned.Preview(data).Issues, i => i.Code == "no-rotation" && i.IsBlocking);
        var withoutRotation = Serialize(data);
        Assert.False(turned.ApplyTo(data).Applied);
        Assert.True(withoutRotation.AsSpan().SequenceEqual(Serialize(data)));
        // ...but a pure offset still works and preserves the layout that has no rotation.
        var straight = new StagedBaseEdits(Counter());
        var dup = straight.StageDuplicate([key], Offset);
        AssertApplied(straight.ApplyTo(data));
        Assert.Equal(Names(Entry(data, key)), Names(Entry(data, dup.NewKeys[key])));
        Assert.NotEqual(original.Length, Serialize(data).Length);
    }

    // ---------- staged model: preview, revert, transforms ----------

    [Fact]
    public void Revert_per_edit_and_revert_all_leave_nothing_to_apply_and_change_no_bytes()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var a = Quiet(data);
        var b = Quiet(data, o => o.Key != a);
        var original = Serialize(data);

        var edits = new StagedBaseEdits(Counter());
        edits.MoveBy(data, [a], 10, 0, 0);
        edits.StageDelete([b]);
        var dup = edits.StageDuplicate([a], Offset);
        Assert.False(edits.IsEmpty);
        var preview = edits.Preview(data);
        Assert.Single(preview.Transforms);
        Assert.Single(preview.Deletions);
        Assert.Single(preview.Duplications);

        Assert.True(edits.RevertTransform(a));
        Assert.True(edits.RevertDeletion(b));
        Assert.True(edits.RevertDuplication(dup.Id));
        Assert.True(edits.IsEmpty);
        var empty = edits.Preview(data);
        Assert.Empty(empty.Transforms);
        Assert.Empty(empty.Deletions);
        Assert.Empty(empty.Duplications);
        Assert.Equal(empty.ObjectsBefore, empty.ObjectsAfter);
        AssertApplied(edits.ApplyTo(data)); // nothing staged: a no-op
        AssertUnchanged(data, original);

        edits.MoveBy(data, [a], 10, 0, 0);
        edits.StageDelete([b]);
        edits.StageDuplicate([a], Offset);
        edits.RevertAll();
        Assert.True(edits.IsEmpty);
        AssertUnchanged(data, original);
    }

    [Fact]
    public void Group_rotation_about_the_centroid_keeps_the_shape_and_writes_only_the_moved_entries()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var keys = PlayerBuilt(data).Where(o => o.Transform is { Translation: not null, Rotation: not null })
            .Take(4).Select(o => o.Key).ToList();
        var pos0 = keys.ToDictionary(k => k, k => Translation(data, k), StringComparer.Ordinal);
        var yaw0 = keys.ToDictionary(k => k, k => PlacedObjectCensus.ReadTransform(Entry(data, k))!.Rotation!.Value.YawDegrees, StringComparer.Ordinal);
        var centroid = new PlacedVector(pos0.Values.Average(p => p.X), pos0.Values.Average(p => p.Y), pos0.Values.Average(p => p.Z));
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits();
        var staged = edits.RotateYaw(data, keys, 90);
        Assert.Equal(4, staged.Staged.Count);
        var preview = edits.Preview(data);
        Assert.All(preview.Transforms, r => Assert.InRange(Math.Abs(r.YawDeltaDegrees!.Value - 90), 0, 1e-6));
        AssertApplied(edits.ApplyTo(data));

        foreach (var k in keys)
        {
            var p = Translation(data, k);
            Assert.InRange(PlacementMath.Distance(p, centroid) - PlacementMath.Distance(pos0[k], centroid), -1e-6, 1e-6);
            Assert.Equal(pos0[k].Z, p.Z);
            var expected = PlacementMath.RotateAboutZ(pos0[k], centroid, 90);
            Assert.InRange(PlacementMath.Distance(p, expected), 0, 1e-6);
            var yaw = PlacedObjectCensus.ReadTransform(Entry(data, k))!.Rotation!.Value.YawDegrees;
            Assert.InRange(Math.Abs(PlacementMath.Normalize180(yaw - yaw0[k] - 90)), 0, 1e-6);
        }
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(removed);
        Assert.Equal(keys.Select(k => "DeployedObjectMap/" + k).Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Group_rotation_can_pivot_on_a_chosen_object_and_moves_compose()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var keys = PlayerBuilt(data).Where(o => o.Transform is { Translation: not null, Rotation: not null })
            .Take(3).Select(o => o.Key).ToList();
        var pivotObject = keys[0];
        var pivot = Translation(data, pivotObject);
        var p1 = Translation(data, keys[1]);

        var edits = new StagedBaseEdits();
        edits.MoveBy(data, keys, 100, 0, 0);
        edits.RotateYaw(data, keys, 180, GroupPivot.OfObject(pivotObject));
        AssertApplied(edits.ApplyTo(data));

        // The pivot object was itself moved by 100 first, so it stays at its moved place.
        Assert.InRange(PlacementMath.Distance(Translation(data, pivotObject), new PlacedVector(pivot.X + 100, pivot.Y, pivot.Z)), 0, 1e-6);
        var expected = PlacementMath.RotateAboutZ(new PlacedVector(p1.X + 100, p1.Y, p1.Z), new PlacedVector(pivot.X + 100, pivot.Y, pivot.Z), 180);
        Assert.InRange(PlacementMath.Distance(Translation(data, keys[1]), expected), 0, 1e-6);
    }

    [Fact]
    public void Snap_align_and_distribute_stage_through_the_same_model()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var keys = PlayerBuilt(data).Where(o => o.Transform is { Translation: not null, Rotation: not null })
            .Take(5).Select(o => o.Key).ToList();
        var edits = new StagedBaseEdits();
        edits.SnapToGrid(data, keys, 50, 10, 45);
        AssertApplied(edits.ApplyTo(data));
        foreach (var k in keys)
        {
            var t = PlacedObjectCensus.ReadTransform(Entry(data, k))!;
            Assert.InRange(Math.Abs(t.Translation!.Value.X % 50), 0, 1e-6);
            Assert.InRange(Math.Abs(t.Translation.Value.Y % 50), 0, 1e-6);
            Assert.InRange(Math.Abs(t.Translation.Value.Z % 10), 0, 1e-6);
            Assert.InRange(Math.Abs(t.Rotation!.Value.YawDegrees % 45), 0, 1e-6);
        }

        var align = new StagedBaseEdits();
        var res = align.AlignYaw(data, keys, keys[0]);
        Assert.Equal(keys.Count - 1, res.Staged.Count);
        AssertApplied(align.ApplyTo(data));
        var refYaw = PlacedObjectCensus.ReadTransform(Entry(data, keys[0]))!.Rotation!.Value.YawDegrees;
        foreach (var k in keys)
        {
            Assert.InRange(Math.Abs(PlacementMath.Normalize180(PlacedObjectCensus.ReadTransform(Entry(data, k))!.Rotation!.Value.YawDegrees - refYaw)), 0, 1e-6);
        }

        var spread = new StagedBaseEdits();
        spread.Distribute(data, keys, PlacementAxis.X);
        AssertApplied(spread.ApplyTo(data));
        var xs = keys.Select(k => Translation(data, k).X).Order().ToList();
        var gaps = xs.Zip(xs.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, g => Assert.InRange(g - gaps[0], -1e-6, 1e-6));
    }

    // ---------- pure math and hints ----------

    [Fact]
    public void Placement_math_snaps_aligns_distributes_and_rotates()
    {
        Assert.Equal(150, PlacementMath.SnapValue(137, 50));
        Assert.Equal(-100, PlacementMath.SnapValue(-120, 50));
        Assert.Equal(105, PlacementMath.SnapValue(103, 10, 5));
        Assert.Equal(new PlacedVector(100, 50, 7), PlacementMath.SnapPosition(new PlacedVector(96, 74, 7), 50));
        Assert.Equal(new PlacedVector(100, 50, 20), PlacementMath.SnapPosition(new PlacedVector(96, 74, 17), 50, 10));

        var q = PlacementMath.ComposeYaw(PlacedQuaternion.Identity, 37);
        Assert.InRange(Math.Abs(q.YawDegrees - 37), 0, 1e-9);
        Assert.InRange(Math.Abs(PlacementMath.SnapYaw(q, 45).YawDegrees - 45), 0, 1e-9);
        Assert.InRange(Math.Abs(PlacementMath.SnapYaw(PlacementMath.ComposeYaw(PlacedQuaternion.Identity, -100), 90).YawDegrees + 90), 0, 1e-9);
        Assert.InRange(Math.Abs(PlacementMath.AlignYaw(q, PlacementMath.ComposeYaw(PlacedQuaternion.Identity, -120)).YawDegrees + 120), 0, 1e-9);
        // Pitch survives a yaw snap.
        var pitched = new PlacedQuaternion(0, Math.Sin(0.2), 0, Math.Cos(0.2));
        Assert.InRange(Math.Abs(PlacementMath.SnapYaw(pitched, 45).PitchDegrees - pitched.PitchDegrees), 0, 1e-9);

        var rotated = PlacementMath.RotateAboutZ(new PlacedVector(10, 0, 5), new PlacedVector(0, 0, 0), 90);
        Assert.InRange(PlacementMath.Distance(rotated, new PlacedVector(0, 10, 5)), 0, 1e-9);

        var spread = PlacementMath.DistributeAlong(
            [("a", new PlacedVector(0, 1, 0)), ("b", new PlacedVector(10, 2, 0)), ("c", new PlacedVector(100, 3, 0))], PlacementAxis.X);
        Assert.Equal(new PlacedVector(50, 2, 0), spread["b"]);
        Assert.Equal(new PlacedVector(0, 1, 0), spread["a"]);
        Assert.Equal(new PlacedVector(100, 3, 0), spread["c"]);
    }

    [Fact]
    public void Proximity_hints_report_close_pairs_and_say_they_are_not_collision()
    {
        var points = new List<PlacedPoint>
        {
            new("a", "A", new PlacedVector(0, 0, 0)),
            new("b", "B", new PlacedVector(30, 0, 0)),
            new("c", "C", new PlacedVector(1000, 0, 0)),
            new("d", "D", new PlacedVector(1010, 0, 400)),
        };
        var hints = ProximityHints.Find(points, 50);
        var hint = Assert.Single(hints);
        Assert.Equal(("a", "b"), (hint.KeyA, hint.KeyB));
        Assert.InRange(hint.DistanceCm, 29.999, 30.001);
        Assert.Equal(2, ProximityHints.Find(points, 500).Count(h => h.KeyA is "c" or "a"));
        Assert.Empty(ProximityHints.Find(points, 50, ["c"]));
        Assert.Contains("not a collision", ProximityHints.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void Staged_preview_reports_proximity_hints_for_a_copy_placed_on_its_source()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var key = Quiet(data);
        var edits = new StagedBaseEdits(Counter());
        edits.StageDuplicate([key], new PlacedVector(0, 0, 0));
        var preview = edits.Preview(data);
        Assert.Contains(preview.ProximityHints, h => h.KeyA == key || h.KeyB == key);
        Assert.Contains(preview.Issues, i => i.Code == "proximity-hint" && !i.IsBlocking);
    }

    [Fact]
    public void Transform_only_apply_touches_only_the_transform_bytes()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var key = Quiet(data);
        var original = Serialize(data);
        var edits = new StagedBaseEdits();
        edits.MoveBy(data, [key], 123.5, -45.25, 10);
        edits.RotateYaw(data, [key], 15, GroupPivot.Centroid);
        AssertApplied(edits.ApplyTo(data));
        var after = Serialize(data);
        Assert.Equal(original.Length, after.Length);
        var changed = Enumerable.Range(0, original.Length).Count(i => original[i] != after[i]);
        Assert.InRange(changed, 1, 56);
    }

    [Fact]
    public void Value_typed_ids_from_the_default_factory_are_real_guid_keys()
    {
        var edits = new StagedBaseEdits();
        var dup = edits.StageDuplicate(["A", "B"], Offset);
        Assert.All(dup.NewKeys.Values, k =>
        {
            Assert.Equal(32, k.Length);
            Assert.All(k, c => Assert.True(Uri.IsHexDigit(c) && !char.IsLower(c)));
        });
        Assert.Equal(2, dup.NewKeys.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, dup.Id);
        Assert.Equal(2, edits.StageDuplicate(["A"], Offset).Id);
    }
}
