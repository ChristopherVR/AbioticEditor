using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Moving a base between worlds (GitHub issue 69): a group of player-built pieces is lifted into a
/// <see cref="BasePackage"/> (in memory, or as a file), put down in another world with everything
/// stored inside, and removed from the world it left. Proved against the dedicated-server Facility
/// save; a second fresh load of the same save stands in for "another world of the same area".
/// </summary>
public sealed class BaseMoveTests
{
    /// <summary>The largest detected base: the pieces a player would move.</summary>
    private static List<string> BiggestBase(WorldSaveData data)
    {
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var bases = BaseDetector.Detect(data.Deployables);
        return bases.OrderByDescending(b => b.Deployables.Count).First()
            .Deployables.Select(d => d.Id).Where(built.Contains).ToList();
    }

    [Fact]
    public void A_package_carries_only_player_built_pieces_and_round_trips_through_a_file()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var level = PlacedObjectCensus.Build(source).Objects!.First(o => o.DeployedByPlayer != true).Key;

        var package = BasePackage.Create(source, keys.Append(level).Append("not-a-key"), "Cascade", "WorldSave_Facility.sav", out var skipped);

        Assert.Equal(keys.Count, package.Pieces.Count);
        Assert.Contains(skipped, s => s.Key == level && s.Reason == "level-placed");
        Assert.Contains(skipped, s => s.Key == "not-a-key" && s.Reason == "not-in-save");
        Assert.NotEmpty(package.LevelPackages);
        Assert.EndsWith(BasePackage.FileExtension, package.SuggestedFileName(), StringComparison.Ordinal);

        var json = package.ToJson();
        var back = BasePackage.Parse(json);
        Assert.Equal(package.Pieces.Select(p => p.Key).Order(), back.Pieces.Select(p => p.Key).Order());
        Assert.Equal(package.StoredItems, back.StoredItems);
        Assert.Equal("Cascade", back.SourceWorld);
        Assert.Equal("WorldSave_Facility.sav", back.AreaFile);
        // The file is the carried part of the save, compressed (about 550 KB for this 505-piece base), not the whole ~16 MB area.
        Assert.True(json.Length < OriginalBytes.Length / 10, $"package is {json.Length} bytes, save {OriginalBytes.Length}, pieces {package.Pieces.Count} of {PlayerBuilt(source).Count()}");
    }

    [Fact]
    public void A_file_that_is_not_a_base_is_refused_with_a_plain_reason()
    {
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse("{\"hello\":1}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse("not json"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse(
            "{\"format\":\"abiotic-editor-base\",\"version\":1,\"save\":\"!!!\"}"u8.ToArray()));
    }

    [Fact]
    public void Putting_a_package_down_adds_every_piece_with_its_contents_and_leaves_the_rest_alone()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var package = BasePackage.Parse(BasePackage.Create(source, keys, "Cascade", "WorldSave_Facility.sav", out _).ToJson());

        var target = Load();
        Assert.Null(package.LevelMismatch(target));
        var before = Fingerprint(target.Raw);
        var edits = new StagedBaseEdits(Counter('A'));
        var staged = edits.StageTransplant(package, new PlacedVector(5000, -3000, 0), 90);
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Equal(keys.Count, preview.Duplications.Count);
        Assert.Equal(package.StoredItems, preview.ItemsCopied);
        Assert.DoesNotContain(preview.Issues, i => i.Code == "contents-copied");

        var result = edits.ApplyTo(target);
        Assert.True(result.Applied);
        Assert.Equal(keys.Count, result.Created.Count);

        // Exactly the new pieces (and their outlets) were added; nothing that was there changed.
        var reloaded = WorldSaveReader.ReadFromStream(new MemoryStream(Serialize(target)));
        var after = Fingerprint(reloaded.Raw);
        foreach (var (map, entries) in before)
        {
            foreach (var (key, value) in entries)
            {
                Assert.True(after[map].TryGetValue(key, out var now), $"{map}[{key}] went missing");
                if (key != "(value)") Assert.Equal(value, now);
            }
        }
        var newKeys = staged.NewKeys.Values.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(newKeys, after["DeployedObjectMap"].Keys.Except(before["DeployedObjectMap"].Keys).ToHashSet(StringComparer.Ordinal));
        var stored = newKeys.Sum(k => PlacedObjectContents.ReadItems(WorldMapAccessor.FindEntry(reloaded.Raw, "DeployedObjectMap", k)!).Sum(i => Math.Max(i.Count, 1)));
        Assert.Equal(package.StoredItems, stored);
    }

    [Fact]
    public void Restaging_moves_the_landing_spot_and_keeps_the_new_ids()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source).Take(4).ToList();
        var package = BasePackage.Create(source, keys, "Cascade", "WorldSave_Facility.sav", out _);
        var edits = new StagedBaseEdits(Counter('B'));
        var staged = edits.StageTransplant(package, new PlacedVector(0, 0, 0));
        var first = edits.Preview(Load()).Duplications.ToDictionary(r => r.NewKey, r => r.After!.Translation!.Value);

        var moved = edits.RestageDuplication(staged.Id, new PlacedVector(100, 200, 30), 0)!;

        Assert.Equal(staged.NewKeys, moved.NewKeys);
        foreach (var row in edits.Preview(Load()).Duplications)
        {
            var was = first[row.NewKey];
            var now = row.After!.Translation!.Value;
            Assert.Equal(was.X + 100, now.X, 3);
            Assert.Equal(was.Y + 200, now.Y, 3);
            Assert.Equal(was.Z + 30, now.Z, 3);
        }
        Assert.Null(edits.RestageDuplication(9999, new PlacedVector(0, 0, 0), 0));
    }

    [Fact]
    public void The_moved_pieces_can_be_removed_from_the_world_they_left()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var edits = new StagedBaseEdits();
        edits.StageDelete(keys, DeletePolicy.MovedAway);
        var preview = edits.Preview(source);
        // The fixture's biggest base (claimed beds, power, teleporters) moves without anything blocking.
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        var result = edits.ApplyTo(source);
        Assert.True(result.Applied);
        Assert.Equal(keys.Count, result.Deleted.Count);
        Assert.All(keys, k => Assert.Null(WorldMapAccessor.FindEntry(source.Raw, "DeployedObjectMap", k)));
    }
}
