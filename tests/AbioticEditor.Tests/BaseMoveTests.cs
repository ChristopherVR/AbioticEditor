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
        Assert.False(package.IsOtherMap(target));
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

    /// <summary>Pretends <paramref name="save"/> is another map: every actor path moves to a made-up level.</summary>
    private static void MoveToOtherMap(WorldSaveData save, string package)
    {
        foreach (var e in WorldMapAccessor.Entries(save.Raw, "DeployedObjectMap"))
        {
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is { SubPath: { } sub })
            {
                var asset = package[(package.LastIndexOf('/') + 1)..];
                WorldMapAccessor.SetSoftObjectPath(e.Props, "ActorPath_", $"{package}.{asset}:{sub}");
            }
        }
    }

    [Fact]
    public void A_base_from_another_map_goes_into_the_level_the_target_keeps_its_own_pieces_in()
    {
        if (!HasServerFacility) return;
        var keys = BiggestBase(Load()).Take(12).ToList();
        var package = BasePackage.Create(Load(), keys, "Cascade", "WorldSave_Facility.sav", out _);
        var target = Load();
        MoveToOtherMap(target, "/Game/Maps/Elsewhere");

        Assert.True(package.IsOtherMap(target));
        Assert.DoesNotContain(BaseDestinations.For(package, target), d => d.Kind == BaseDestinationKind.SameSpot);

        var edits = new StagedBaseEdits(Counter('C'));
        var staged = edits.StageTransplant(package, new PlacedVector(1000, 0, 0));
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Contains(preview.Issues, i => i.Code == "other-map" && !i.IsBlocking);
        Assert.True(edits.ApplyTo(target).Applied);

        var reloaded = WorldSaveReader.ReadFromStream(new MemoryStream(Serialize(target)));
        var paths = staged.NewKeys.Values
            .Select(k => WorldMapAccessor.GetSoftObjectPath(WorldMapAccessor.FindEntry(reloaded.Raw, "DeployedObjectMap", k)!, "ActorPath_")!.Value)
            .ToList();
        Assert.All(paths, p => Assert.Equal("/Game/Maps/Elsewhere", p.Package));
        Assert.All(paths, p => Assert.Equal("Elsewhere", p.Asset));
        Assert.Equal(paths.Count, paths.Select(p => p.SubPath).Distinct().Count());
    }

    [Fact]
    public void A_portal_world_with_nothing_built_yet_takes_the_base_into_its_own_level()
    {
        if (!HasServerFacility) return;
        var portal = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_V_Alps.sav");
        if (!File.Exists(portal)) return;
        var target = WorldSaveReader.ReadFromFile(portal);
        var level = WorldMapAccessor.Entries(target.Raw, "DeployedObjectMap")
            .Select(e => WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_")?.Package).First(p => p is not null);
        var package = BasePackage.Create(Load(), BiggestBase(Load()).Take(3), "Cascade", "WorldSave_Facility.sav", out _);
        Assert.True(package.IsOtherMap(target));
        Assert.True(BasePackage.HasNoPlayerPieces(target));
        var places = BaseDestinations.For(package, target);
        Assert.DoesNotContain(places, d => d.Kind == BaseDestinationKind.Sector);
        Assert.DoesNotContain(places, d => d.Kind == BaseDestinationKind.SameSpot);
        var middle = Assert.Single(places, d => d.Kind == BaseDestinationKind.AreaMiddle);

        var edits = new StagedBaseEdits(Counter('D'));
        var staged = edits.StageTransplant(package, middle.OffsetFor(package));
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Contains(preview.Issues, i => i.Code == "other-map" && i.Severity == BaseEditSeverity.Warning);
        // Landed in the portal's own space, not at the Facility coordinates the pieces came from.
        var centre = preview.Duplications.Select(r => r.After!.Translation!.Value).ToList();
        Assert.InRange(centre.Average(p => p.X), middle.Point.X - 1, middle.Point.X + 1);
        Assert.InRange(centre.Average(p => p.Y), middle.Point.Y - 1, middle.Point.Y + 1);
        Assert.True(edits.ApplyTo(target).Applied);
        Assert.All(staged.NewKeys.Values, k => Assert.Equal(level,
            WorldMapAccessor.GetSoftObjectPath(WorldMapAccessor.FindEntry(target.Raw, "DeployedObjectMap", k)!, "ActorPath_")?.Package));
    }

    [Fact]
    public void Destinations_offer_the_same_spot_the_named_sectors_and_the_bases_already_there()
    {
        if (!HasServerFacility) return;
        var package = BasePackage.Create(Load(), BiggestBase(Load()), "Cascade", "WorldSave_Facility.sav", out _);
        var target = Load();

        var places = BaseDestinations.For(package, target);

        var same = Assert.Single(places, d => d.Kind == BaseDestinationKind.SameSpot);
        Assert.Equal(new PlacedVector(0, 0, 0), same.OffsetFor(package));
        Assert.Equal(10, places.Count(d => d.Kind == BaseDestinationKind.Sector));
        Assert.Contains(places, d => d.Kind == BaseDestinationKind.Sector && d.Name == "Manufacturing West");
        Assert.Contains(places, d => d.Kind == BaseDestinationKind.BesideBase && d.NearSector is not null);
        Assert.DoesNotContain(places, d => d.Name == "Outside any base");

        // The middle of the pieces lands on the place, and the lowest piece on its floor.
        var mines = places.First(d => d.Name == "The Mines");
        var shift = mines.OffsetFor(package);
        Assert.Equal(mines.Point.X, package.Centre!.Value.X + shift.X, 0);
        Assert.Equal(mines.Point.Y, package.Centre!.Value.Y + shift.Y, 0);
        Assert.Equal(mines.Point.Z, package.FloorZ!.Value + shift.Z, 0);
    }
}
